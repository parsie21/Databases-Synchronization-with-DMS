using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace SyncClient.Database
{
    /// <summary>
    /// Gestisce la diagnostica e manutenzione delle connessioni database
    /// </summary>
    public class ConnectionDiagnostics
    {
        private readonly ILogger _logger;

        public ConnectionDiagnostics(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Verifica le connessioni attive del database
        /// </summary>
        public async Task CheckActiveConnectionsAsync(SqlConnection connection)
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

        /// <summary>
        /// Analizza lo stato delle sessioni
        /// </summary>
        public async Task CheckSessionStatusAsync(SqlConnection connection)
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

        /// <summary>
        /// Analizza la distribuzione delle sessioni per database
        /// </summary>
        public async Task CheckSessionBreakdownAsync(SqlConnection connection)
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
                
                if (preCleanupConnections > 20)
                {
                    _logger.LogInformation("Performing conditional GC for {DatabaseName} due to high connection count: {Count}", 
                        databaseName, preCleanupConnections);
                    GC.Collect(1, GCCollectionMode.Optimized);
                    await Task.Delay(500);
                }
                else
                {
                    await Task.Delay(500);
                }

                var postCleanupConnections = GetConnectionCount(connectionString, databaseName);

                _logger.LogInformation("Connection cleanup for {DatabaseName}: {Before} -> {After} connections",
                    databaseName, preCleanupConnections, postCleanupConnections);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Connection cleanup failed for {DatabaseName}: {Error}", databaseName, ex.Message);
            }
        }

        /// <summary>
        /// Ottiene il numero di connessioni attive per un database
        /// </summary>
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
    }
}
