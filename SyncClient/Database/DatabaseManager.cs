using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClient.Database
{
    /// <summary>
    /// Classe responsabile della gestione e controllo del database.
    /// Fornisce diagnostica, manutenzione e gestione delle connessioni.
    /// </summary>
    public class DatabaseManager
    {
        #region Fields
        private readonly ILogger _logger;
        #endregion

        #region Constructor
        public DatabaseManager(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        #endregion

        #region Public Methods

        /// <summary>
        /// Esegue diagnostica completa del database
        /// </summary>
        public async Task PerformDatabaseDiagnosticsAsync(string connectionString, string databaseName)
        {
            try
            {
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                _logger.LogInformation("=== DIAGNOSTICS FOR {DatabaseName} ===", databaseName);

                // Eseguiamo varie verifiche 
                await CheckActiveConnectionsAsync(connection);
                await CheckSessionBreakdownAsync(connection);
                await CheckSessionStatusAsync(connection);
                await CheckBlockedProcessesAsync(connection);
                await CheckChangeTrackingStatusAsync(connection);
                await CheckSyncTablesAsync(connection, databaseName);

                _logger.LogInformation("=== END DIAGNOSTICS ===");
                // NOTA: connection.DisposeAsync() chiamato automaticamente da "using var"
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Diagnostic failed for {DatabaseName}: {Error}", databaseName, ex.Message);
            }
        }

        /// <summary>
        /// Verifica e abilita Change Tracking se necessario (solo a livello database)
        /// </summary>
        public async Task<bool> EnsureChangeTrackingIsEnabledAsync(string connectionString, string databaseName)
        {
            try
            {
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                _logger.LogInformation("Checking Change Tracking status for {DatabaseName}...", databaseName);

                // Verifica se Change Tracking è abilitato a livello database
                var isDatabaseCTEnabled = await IsChangeTrackingEnabledAsync(connection);

                if (!isDatabaseCTEnabled)
                {
                    _logger.LogWarning("Change Tracking not enabled on {DatabaseName}. Attempting to enable...", databaseName);
                    var enableResult = await EnableChangeTrackingAsync(connection);

                    if (!enableResult)
                    {
                        _logger.LogError("Failed to enable Change Tracking on {DatabaseName}", databaseName);
                        return false;
                    }

                    _logger.LogInformation("Change Tracking enabled successfully on {DatabaseName}", databaseName);
                }
                else
                {
                    _logger.LogInformation("Change Tracking already enabled on {DatabaseName}", databaseName);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking/enabling Change Tracking for {DatabaseName}: {Error}",
                    databaseName, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Esegue cleanup delle connessioni del database
        /// </summary>
        public async Task PerformConnectionCleanup(string connectionString, string databaseName)
        {
            try
            {
                _logger.LogInformation("Starting connection cleanup for {DatabaseName}...", databaseName);

                var preCleanupConnections = GetConnectionCount(connectionString, databaseName);

                using var tempConnection = new SqlConnection(connectionString);
                SqlConnection.ClearPool(tempConnection);
                SqlConnection.ClearAllPools();

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                await Task.Delay(3000);

                var postCleanupConnections = GetConnectionCount(connectionString, databaseName);

                _logger.LogInformation("Connection cleanup for {DatabaseName}: {Before} -> {After} connections",
                    databaseName, preCleanupConnections, postCleanupConnections);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Connection cleanup failed for {DatabaseName}: {Error}", databaseName, ex.Message);
            }
        }
        public int GetConnectionCount(string connectionString, string databaseName)
        {
            try
            {
                using var connection = new SqlConnection(connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandTimeout = 10;
                cmd.CommandText = @"
                    SELECT COUNT(*) 
                    FROM sys.dm_exec_sessions 
                    WHERE database_id = DB_ID() 
                    AND session_id > 50 
                    AND program_name LIKE '%.Net SqlClient Data Provider%'";

                return (int)cmd.ExecuteScalar();
            }
            catch
            {
                return -1;
            }
        }
        #endregion

        #region Private Diagnostic Methods

        private async Task CheckActiveConnectionsAsync(SqlConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandTimeout = 20;
            cmd.CommandText = @"
                SELECT 
                    COUNT(*) as ActiveConnections,
                    @@MAX_CONNECTIONS as MaxConnections,
                    (SELECT value FROM sys.configurations WHERE name = 'user connections') as UserConnectionsLimit
                FROM sys.dm_exec_connections";

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                // CORREZIONI: Nomi dei campi corretti
                var activeConnections = (int)reader["ActiveConnections"];
                var maxConnections = (int)reader["MaxConnections"];
                var userLimit = (int)reader["UserConnectionsLimit"];

                _logger.LogInformation("Connections - Active: {Active}, Max: {Max}, UserLimit: {UserLimit}",
                    activeConnections, maxConnections, userLimit);

                if (activeConnections > (maxConnections * 0.8))
                {
                    _logger.LogWarning("HIGH CONNECTION USAGE: {Active}/{Max} ({Percentage:F1}%)",
                        activeConnections, maxConnections, (activeConnections * 100.0 / maxConnections));
                }
            }

        }

        private async Task CheckSessionBreakdownAsync(SqlConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandTimeout = 20;
            cmd.CommandText = @"
                SELECT 
                    DB_NAME(database_id) as DatabaseName,
                    COUNT(*) as SessionCount,
                    status,
                    program_name
                FROM sys.dm_exec_sessions 
                WHERE database_id > 0
                GROUP BY database_id, status, program_name
                ORDER BY COUNT(*) DESC";

            _logger.LogInformation("--- SESSION BREAKDOWN ---");

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var dbName = reader["DatabaseName"]?.ToString() ?? "NULL";
                var sessionCount = (int)reader["SessionCount"];
                var status = reader["status"]?.ToString() ?? "NULL";
                var programName = reader["program_name"]?.ToString() ?? "NULL";

                _logger.LogInformation("DB: {Database}, Sessions: {Count}, Status: {Status}, Program: {Program}",
                    dbName, sessionCount, status, programName);
            }
        }

        private async Task CheckSessionStatusAsync(SqlConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandTimeout = 20;
            cmd.CommandText = @"
                SELECT 
                    status,
                    COUNT(*) as Count
                FROM sys.dm_exec_sessions 
                WHERE session_id > 50
                GROUP BY status
                ORDER BY COUNT(*) DESC";

            _logger.LogInformation("--- SESSION STATUS ---");

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var status = reader["status"]?.ToString() ?? "UNKNOWN";
                var count = (int)reader["Count"];

                _logger.LogInformation("Status: {Status}, Count: {Count}", status, count);

                switch (status.ToLower())
                {
                    case "sleeping" when count > 50:
                        _logger.LogWarning("HIGH SLEEPING SESSION: {Count} sleeping connections detected", count);
                        break;
                    case "suspended" when count > 5:
                        _logger.LogWarning("SUSPENDED SESSION DETECTED: {Count} suspended sessions", count);
                        break;
                    case "dormant" when count > 10:
                        _logger.LogWarning("HIGH DORMANT SESSIONS: {Count} dormant sessions detected", count);
                        break;
                    case "running" when count > 20:
                        _logger.LogInformation("HIGH ACTIVITY: {Count} running sessions (normal under load)", count);
                        break;
                }
            }
        }

        private async Task CheckBlockedProcessesAsync(SqlConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandTimeout = 30;
            cmd.CommandText = @"
                SELECT 
                    r.session_id AS blocked_session_id,
                    r.blocking_session_id,
                    r.wait_type,
                    r.wait_time,
                    r.command,
                    r.wait_resource,
                    s.program_name,
                    s.host_name,
                    s.login_name,
                    DB_NAME(r.database_id) as database_name
                FROM sys.dm_exec_requests r
                INNER JOIN sys.dm_exec_sessions s ON r.session_id = s.session_id
                WHERE r.blocking_session_id > 0
                   OR r.session_id IN (
                       SELECT DISTINCT blocking_session_id 
                       FROM sys.dm_exec_requests 
                       WHERE blocking_session_id > 0
                   )
                ORDER BY r.blocking_session_id, r.session_id";

            using var reader = await cmd.ExecuteReaderAsync();
            bool hasBlocks = false;

            while (await reader.ReadAsync())
            {
                hasBlocks = true;

                var blockedSessionId = (int)reader["blocked_session_id"];
                var blockingSessionId = reader["blocking_session_id"] != DBNull.Value
                    ? (int)reader["blocking_session_id"]
                    : 0;
                var waitType = reader["wait_type"]?.ToString() ?? "NULL";
                var waitTime = reader["wait_time"] != DBNull.Value ? (int)reader["wait_time"] : 0;
                var command = reader["command"]?.ToString() ?? "NULL";
                var waitResource = reader["wait_resource"]?.ToString() ?? "NULL";
                var programName = reader["program_name"]?.ToString() ?? "NULL";
                var hostName = reader["host_name"]?.ToString() ?? "NULL";
                var loginName = reader["login_name"]?.ToString() ?? "NULL";
                var databaseName = reader["database_name"]?.ToString() ?? "NULL";

                if (blockingSessionId > 0)
                {
                    _logger.LogWarning("BLOCKED PROCESS: Session {BlockedSession} blocked by {BlockingSession} | " +
                                     "WaitType: {WaitType} | WaitTime: {WaitTime}ms | Command: {Command} | " +
                                     "Resource: {Resource} | Program: {Program} | Host: {Host} | " +
                                     "Login: {Login} | DB: {Database}",
                        blockedSessionId, blockingSessionId, waitType, waitTime, command,
                        waitResource, programName, hostName, loginName, databaseName);
                }
                else
                {
                    _logger.LogWarning("BLOCKING PROCESS: Session {SessionId} is blocking others | " +
                                     "Program: {Program} | Host: {Host} | Login: {Login} | DB: {Database}",
                        blockedSessionId, programName, hostName, loginName, databaseName);
                }
            }

            if (!hasBlocks)
            {
                _logger.LogInformation("No blocked processes detected");
            }
            else
            {
                _logger.LogWarning("BLOCKING DETECTED: Consider investigating these blocked processes");
            }
        }

        private async Task CheckChangeTrackingStatusAsync(SqlConnection connection)
        {
            try
            {
                _logger.LogInformation("--- CHANGE TRACKING STATUS ---");

                // 1. Prima verifica se Change Tracking è abilitato a livello database
                await CheckDatabaseChangeTrackingAsync(connection);

                // 2. Poi verifica le tabelle specifiche con Change Tracking
                await CheckTableChangeTrackingAsync(connection);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Change Tracking check failed: {Error}", ex.Message);
            }
        }

        private async Task CheckDatabaseChangeTrackingAsync(SqlConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandTimeout = 30;
            cmd.CommandText = @"
                SELECT 
                    DB_NAME(database_id) AS DATABASE_NAME,
                    database_id,
                    is_auto_cleanup_on,
                    retention_period,
                    retention_period_units,
                    retention_period_units_desc
                FROM sys.change_tracking_databases
                WHERE database_id = DB_ID()";

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var dbName = reader["DATABASE_NAME"]?.ToString() ?? "UNKNOWN";
                var retentionPeriod = reader["retention_period"];
                var retentionUnits = reader["retention_period_units_desc"]?.ToString() ?? "UNKNOWN";
                var autoCleanupValue = reader["is_auto_cleanup_on"];
                var autoCleanup = autoCleanupValue != DBNull.Value && Convert.ToBoolean(autoCleanupValue);

                _logger.LogInformation("Change Tracking ENABLED - DB: {DatabaseName}, Retention: {Period} {Units}, AutoCleanup: {AutoCleanup}",
                    dbName, retentionPeriod, retentionUnits, autoCleanup);
            }
            else
            {
                _logger.LogWarning("Change Tracking NOT ENABLED at database level - this will cause sync failures!");
            }
        }

        private async Task CheckTableChangeTrackingAsync(SqlConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandTimeout = 30;
            cmd.CommandText = @"
                SELECT 
                    OBJECT_NAME(ct.object_id) AS TableName,
                    ct.min_valid_version,
                    ct.cleanup_version,
                    p.rows AS EstimatedRows,
                    CHANGE_TRACKING_CURRENT_VERSION() as CurrentVersion,
                    ct.is_track_columns_updated_on
                FROM sys.change_tracking_tables ct
                JOIN sys.partitions p ON ct.object_id = p.object_id AND p.index_id IN (0, 1)
                ORDER BY p.rows DESC";

            using var reader = await cmd.ExecuteReaderAsync();
            bool hasChangeTrackingTables = false;

            while (await reader.ReadAsync())
            {
                hasChangeTrackingTables = true;

                var tableName = reader["TableName"]?.ToString() ?? "UNKNOWN";
                var rows = reader["EstimatedRows"] != DBNull.Value ? Convert.ToInt64(reader["EstimatedRows"]) : 0;
                var minVersion = reader["min_valid_version"];
                var cleanupVersion = reader["cleanup_version"];
                var currentVersion = reader["CurrentVersion"];
                var trackColumnsUpdated = reader["is_track_columns_updated_on"] != DBNull.Value && (bool)reader["is_track_columns_updated_on"];

                _logger.LogInformation("ChangeTracking Table: {Table} | Rows: {Rows:N0} | " +
                                     "MinVer: {MinVer} | CleanupVer: {CleanupVer} | CurrentVer: {CurrentVer} | " +
                                     "TrackColumns: {TrackColumns}",
                    tableName, rows, minVersion, cleanupVersion, currentVersion, trackColumnsUpdated);

                var importantTables = new[] { "ana_Clienti", "ana_Fornitori", "mag_Banchi" };
                if (importantTables.Contains(tableName))
                {
                    _logger.LogInformation("Critical sync table '{Table}' has Change Tracking enabled", tableName);
                }

                if (rows > 100000)
                {
                    _logger.LogWarning("Large table detected: {Table} has {Rows:N0} rows - monitor sync performance",
                        tableName, rows);
                }
            }

            if (!hasChangeTrackingTables)
            {
                _logger.LogWarning("NO tables found with Change Tracking enabled - sync will not work!");
            }
            else
            {
                _logger.LogInformation("Change Tracking tables detected and configured");
            }
        }

        private async Task CheckSyncTablesAsync(SqlConnection connection, string databaseName)
        {
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandTimeout = 30;
                cmd.CommandText = @"
                    SELECT 
                        TABLE_NAME,
                        TABLE_SCHEMA,
                        TABLE_TYPE
                    FROM INFORMATION_SCHEMA.TABLES 
                    WHERE TABLE_NAME LIKE '%_tracking%' 
                       OR TABLE_NAME LIKE 'scope_info%'
                       OR TABLE_NAME LIKE '%dms_%'
                       OR TABLE_NAME LIKE '%sync_%'
                    ORDER BY TABLE_SCHEMA, TABLE_NAME";

                using var reader = await cmd.ExecuteReaderAsync();
                var syncTables = new List<string>();
                var trackingTables = new List<string>();
                var metadataTables = new List<string>();

                _logger.LogInformation("--- DMS SYNC TABLES ---");

                while (await reader.ReadAsync())
                {
                    var tableName = reader["TABLE_NAME"]?.ToString();
                    var tableSchema = reader["TABLE_SCHEMA"]?.ToString() ?? "dbo";
                    var tableType = reader["TABLE_TYPE"]?.ToString() ?? "TABLE";

                    if (!string.IsNullOrEmpty(tableName))
                    {
                        var fullTableName = $"{tableSchema}.{tableName}";
                        syncTables.Add(fullTableName);

                        if (tableName.Contains("_tracking"))
                        {
                            trackingTables.Add(fullTableName);
                            _logger.LogInformation("DMS Tracking Table: {Table} ({Type})", fullTableName, tableType);
                        }
                        else if (tableName.StartsWith("scope_info") || tableName.Contains("scope"))
                        {
                            metadataTables.Add(fullTableName);
                            _logger.LogInformation("DMS Metadata Table: {Table} ({Type})", fullTableName, tableType);
                        }
                        else
                        {
                            _logger.LogInformation("DMS Related Table: {Table} ({Type})", fullTableName, tableType);
                        }
                    }
                }

                // Analisi e report finale
                if (syncTables.Count > 0)
                {
                    _logger.LogInformation("DMS Sync infrastructure detected:");
                    _logger.LogInformation("- Total DMS tables: {TotalCount}", syncTables.Count);
                    _logger.LogInformation("- Tracking tables: {TrackingCount}", trackingTables.Count);
                    _logger.LogInformation("- Metadata tables: {MetadataCount}", metadataTables.Count);

                    // Verifica presenza tabelle essenziali
                    var hasMetadata = metadataTables.Any(t => t.Contains("scope_info"));
                    var hasTracking = trackingTables.Count > 0;

                    if (hasMetadata && hasTracking)
                    {
                        _logger.LogInformation("SYNC INFRASTRUCTURE STATUS: Complete - Both metadata and tracking tables present");
                    }
                    else if (hasMetadata && !hasTracking)
                    {
                        _logger.LogWarning("SYNC INFRASTRUCTURE STATUS: Partial - Metadata present but no tracking tables found");
                    }
                    else if (!hasMetadata && hasTracking)
                    {
                        _logger.LogWarning("SYNC INFRASTRUCTURE STATUS: Partial - Tracking tables present but no metadata found");
                    }
                    else
                    {
                        _logger.LogWarning("SYNC INFRASTRUCTURE STATUS: Incomplete - Neither metadata nor tracking tables detected properly");
                    }
                }
                else
                {
                    _logger.LogWarning("NO DMS sync tables found - Database '{DatabaseName}' might not be initialized for synchronization", databaseName);
                    _logger.LogInformation("This is normal for a fresh database before the first sync operation");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Sync tables check failed for {DatabaseName}: {Error}", databaseName, ex.Message);
            }
        }

        #endregion

        #region Private Change Tracking Methods

        /// <summary>
        /// Verifica se Change Tracking è abilitato usando sys.change_tracking_databases
        /// </summary>
        private async Task<bool> IsChangeTrackingEnabledAsync(SqlConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandTimeout = 30;
            cmd.CommandText = @"
                SELECT 
                    DB_NAME(database_id) AS DATABASE_NAME,
                    database_id,
                    is_auto_cleanup_on,
                    retention_period,
                    retention_period_units,
                    retention_period_units_desc
                FROM sys.change_tracking_databases
                WHERE database_id = DB_ID()";

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var dbName = reader["DATABASE_NAME"].ToString();
                var retentionPeriod = reader["retention_period"];
                var retentionUnits = reader["retention_period_units_desc"].ToString();
                var autoCleanupValue = reader["is_auto_cleanup_on"];
                var autoCleanup = autoCleanupValue != DBNull.Value && Convert.ToBoolean(autoCleanupValue);

                _logger.LogInformation("Change Tracking found - DB: {DatabaseName}, Retention: {Period} {Units}, AutoCleanup: {AutoCleanup}",
                    dbName, retentionPeriod, retentionUnits, autoCleanup);

                return true;
            }

            return false;
        }

        /// <summary>
        /// Abilita Change Tracking a livello database
        /// </summary>
        private async Task<bool> EnableChangeTrackingAsync(SqlConnection connection)
        {
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandTimeout = 60; // Timeout più lungo per operazioni DDL
                cmd.CommandText = @"
                    ALTER DATABASE CURRENT 
                    SET CHANGE_TRACKING = ON 
                    (CHANGE_RETENTION = 7 DAYS, AUTO_CLEANUP = ON)";

                await cmd.ExecuteNonQueryAsync();

                // Verifica che l'abilitazione sia avvenuta con successo
                await Task.Delay(1000); // Breve pausa per permettere l'attivazione
                return await IsChangeTrackingEnabledAsync(connection);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enable Change Tracking at database level: {Error}", ex.Message);
                return false;
            }
        }

        #endregion
    }
}
