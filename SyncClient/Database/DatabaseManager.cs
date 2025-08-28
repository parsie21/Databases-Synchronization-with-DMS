using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClient.Database
{
    internal class DatabaseManager
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

        #region Methods

        #region main method
        public async Task PerformDatabaseDiagnosticsAsync(string connectionString, string databaseName)
        {
            try
            {
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                _logger.LogInformation("=== DIAGNOSTICS FOR {DatabaseName} ===", databaseName);

                // eseguiamo varie verifiche 

                await CheckActiveConnectionsAsync(connection);
                //await CheckSessionBreakdownAsync(connection);
                //await CheckSessionStatusAsync(connection);
                //await CheckBlockedProcessesAsync(connection);
                //await CheckChangeTrackingStatusAsync(connection);
                //await CheckSyncTablesAsync(connection, databaseName);

                _logger.LogInformation("=== END DIAGNOSTICS ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Diagnostic failed for {DatabaseName}: {Error}", databaseName, ex.Message);
            }
        }
        #endregion

        #region support methods
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
            if(await reader.ReadAsync())
            {
                var activeConnections = (int)reader["ActiceConnections"];
                var maxConnections = (int)reader["MaxConnections"];
                var userLimit = (int)reader["UserConnectionLimit"];

                _logger.LogInformation("Connections - Active: {Active}, Max: {Max}, UserLimit: {UserLimit}",
                    activeConnections, maxConnections, userLimit);
                // alert if we get close to max connections available
                if(activeConnections > (maxConnections * 0.8))
                {
                    _logger.LogWarning("HIGH CONNECTION USAGE: {Active}/{Max} ({Percentage:F1}%)",
                        activeConnections, maxConnections, (activeConnections * 100.0 / maxConnections));
                }
            }
            await cmd.DisposeAsync();
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
                WHERE database_id > 0  -- Esclude le sessioni di sistema senza database specifico
                GROUP BY database_id, status, program_name
                ORDER BY COUNT(*) DESC";

            _logger.LogInformation("--- SESSION BREAKDOWN ---");

            using var reader = await cmd.ExecuteReaderAsync();
            while(await reader.ReadAsync())
            {
                var dbName = reader["DatabaseName"]?.ToString() ?? "NULL";
                var sessionCount = (int)reader["SessionCount"];
                var status = reader["status"]?.ToString() ?? "NULL";
                var programName = reader["program_name"]?.ToString() ?? "NULL";

                _logger.LogInformation("DB: {Database}, Sessions: {Count}, Status: {Status}, Program: {Program}",
                    dbName, sessionCount, status, programName);
            }
            await cmd.DisposeAsync();
        }


        #endregion


        #endregion



    }
}
