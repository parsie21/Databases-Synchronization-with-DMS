using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncClient.Database
{
    /// <summary>
    /// Gestisce la diagnostica e configurazione del Change Tracking
    /// </summary>
    public class ChangeTrackingDiagnostics
    {
        private readonly ILogger _logger;

        public ChangeTrackingDiagnostics(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Verifica e abilita Change Tracking se necessario
        /// </summary>
        public async Task<bool> EnsureChangeTrackingIsEnabledAsync(string connectionString, string databaseName)
        {
            try
            {
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                _logger.LogInformation("Checking Change Tracking status for {DatabaseName}...", databaseName);

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
        /// Verifica lo stato del Change Tracking
        /// </summary>
        public async Task CheckChangeTrackingStatusAsync(SqlConnection connection)
        {
            try
            {
                _logger.LogInformation("--- CHANGE TRACKING STATUS ---");

                await CheckDatabaseChangeTrackingAsync(connection);
                await CheckTableChangeTrackingAsync(connection);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Change Tracking check failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Esegue analisi completa del Change Tracking
        /// </summary>
        public async Task PerformChangeTrackingAnalysisAsync(SqlConnection connection)
        {
            _logger.LogInformation("=== ADVANCED CHANGE TRACKING ANALYSIS ===");
            
            await CheckChangeTrackingCleanup(connection);
            await CheckChangeTrackingVersions(connection);
            await CheckChangeTrackingSize(connection);
            
            _logger.LogInformation("=== END CHANGE TRACKING ANALYSIS ===");
        }

        /// <summary>
        /// Verifica le tabelle di sincronizzazione DMS
        /// </summary>
        public async Task CheckSyncTablesAsync(SqlConnection connection, string databaseName)
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

        #region Private Methods

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

        /// <summary>
        /// Verifica lo stato del cleanup del Change Tracking
        /// </summary>
        private async Task CheckChangeTrackingCleanup(SqlConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandTimeout = 30;
            cmd.CommandText = @"
                SELECT 
                    CHANGE_TRACKING_CURRENT_VERSION() as current_version,
                    (SELECT MIN(min_valid_version) FROM sys.change_tracking_tables) as min_valid_version,
                    (SELECT MAX(cleanup_version) FROM sys.change_tracking_tables) as max_cleanup_version,
                    (SELECT retention_period FROM sys.change_tracking_databases WHERE database_id = DB_ID()) as retention_period_value
                ";

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var currentVersion = reader["current_version"] != DBNull.Value ? Convert.ToInt64(reader["current_version"]) : 0;
                var minValidVersion = reader["min_valid_version"] != DBNull.Value ? Convert.ToInt64(reader["min_valid_version"]) : 0;
                var maxCleanupVersion = reader["max_cleanup_version"] != DBNull.Value ? Convert.ToInt64(reader["max_cleanup_version"]) : 0;
                var retentionPeriod = reader["retention_period_value"] != DBNull.Value ? (int)reader["retention_period_value"] : 0;

                var versionSpread = currentVersion - minValidVersion;
                var cleanupLag = currentVersion - maxCleanupVersion;

                _logger.LogInformation("Change Tracking Versions - Current: {Current}, MinValid: {MinValid}, MaxCleanup: {MaxCleanup}",
                    currentVersion, minValidVersion, maxCleanupVersion);

                _logger.LogInformation("Version Spread: {Spread}, Cleanup Lag: {Lag}, Retention: {Retention} days",
                    versionSpread, cleanupLag, retentionPeriod);

                if (versionSpread > 1000000) // 1 milione di versioni
                {
                    _logger.LogWarning("HIGH VERSION SPREAD: {Spread} versions between current and min valid - cleanup may be slow",
                        versionSpread);
                }

                if (cleanupLag > 100000) // 100k versioni non ripulite
                {
                    _logger.LogWarning("CLEANUP LAG DETECTED: {Lag} versions not yet cleaned up - performance impact possible",
                        cleanupLag);
                }
            }
        }

        /// <summary>
        /// Verifica le versioni del Change Tracking per tabella
        /// </summary>
        private async Task CheckChangeTrackingVersions(SqlConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandTimeout = 30;
            cmd.CommandText = @"
                SELECT 
                    OBJECT_NAME(ct.object_id) AS table_name,
                    ct.min_valid_version,
                    ct.cleanup_version,
                    CHANGE_TRACKING_CURRENT_VERSION() as current_version,
                    (CHANGE_TRACKING_CURRENT_VERSION() - ct.min_valid_version) as version_span,
                    (CHANGE_TRACKING_CURRENT_VERSION() - ct.cleanup_version) as cleanup_pending
                FROM sys.change_tracking_tables ct
                ORDER BY version_span DESC";

            using var reader = await cmd.ExecuteReaderAsync();
            
            _logger.LogInformation("--- CHANGE TRACKING TABLE VERSIONS ---");

            while (await reader.ReadAsync())
            {
                var tableName = reader["table_name"]?.ToString() ?? "UNKNOWN";
                var minValidVersion = reader["min_valid_version"] != DBNull.Value ? Convert.ToInt64(reader["min_valid_version"]) : 0;
                var cleanupVersion = reader["cleanup_version"] != DBNull.Value ? Convert.ToInt64(reader["cleanup_version"]) : 0;
                var currentVersion = reader["current_version"] != DBNull.Value ? Convert.ToInt64(reader["current_version"]) : 0;
                var versionSpan = reader["version_span"] != DBNull.Value ? Convert.ToInt64(reader["version_span"]) : 0;
                var cleanupPending = reader["cleanup_pending"] != DBNull.Value ? Convert.ToInt64(reader["cleanup_pending"]) : 0;

                _logger.LogInformation("Table {Table}: MinValid={MinValid}, Cleanup={Cleanup}, Span={Span}, Pending={Pending}",
                    tableName, minValidVersion, cleanupVersion, versionSpan, cleanupPending);

                if (versionSpan > 500000)
                {
                    _logger.LogWarning("HIGH VERSION SPAN for {Table}: {Span} versions - potential performance impact",
                        tableName, versionSpan);
                }

                if (cleanupPending > 100000)
                {
                    _logger.LogWarning("HIGH CLEANUP PENDING for {Table}: {Pending} versions waiting for cleanup",
                        tableName, cleanupPending);
                }
            }
        }

        /// <summary>
        /// Verifica la dimensione delle tabelle Change Tracking
        /// </summary>
        private async Task CheckChangeTrackingSize(SqlConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandTimeout = 30;
            cmd.CommandText = @"
                SELECT 
                    t.name as table_name,
                    p.row_count as estimated_rows,
                    (p.reserved_page_count * 8) as reserved_kb,
                    (p.used_page_count * 8) as used_kb
                FROM sys.tables t
                JOIN sys.dm_db_partition_stats p ON t.object_id = p.object_id
                WHERE t.name LIKE '%_tracking%'
                OR t.object_id IN (SELECT object_id FROM sys.change_tracking_tables)
                ORDER BY p.reserved_page_count DESC";

            using var reader = await cmd.ExecuteReaderAsync();
            var totalSizeKB = 0L;
            
            _logger.LogInformation("--- CHANGE TRACKING STORAGE SIZE ---");

            while (await reader.ReadAsync())
            {
                var tableName = reader["table_name"]?.ToString() ?? "UNKNOWN";
                var estimatedRows = reader["estimated_rows"] != DBNull.Value ? Convert.ToInt64(reader["estimated_rows"]) : 0;
                var reservedKB = reader["reserved_kb"] != DBNull.Value ? Convert.ToInt64(reader["reserved_kb"]) : 0;
                var usedKB = reader["used_kb"] != DBNull.Value ? Convert.ToInt64(reader["used_kb"]) : 0;

                totalSizeKB += reservedKB;

                _logger.LogInformation("Table {Table}: {Rows:N0} rows, {Reserved:N0} KB reserved, {Used:N0} KB used",
                    tableName, estimatedRows, reservedKB, usedKB);

                if (reservedKB > 1024 * 1024) // > 1 GB
                {
                    _logger.LogWarning("LARGE CHANGE TRACKING TABLE: {Table} uses {Size:N0} MB",
                        tableName, reservedKB / 1024);
                }
            }

            _logger.LogInformation("Total Change Tracking Storage: {Total:N0} MB", totalSizeKB / 1024);

            if (totalSizeKB > 5 * 1024 * 1024) // > 5 GB
            {
                _logger.LogWarning("HIGH CHANGE TRACKING STORAGE USAGE: {Total:N0} MB total",
                    totalSizeKB / 1024);
            }
        }

        #endregion
    }
}
