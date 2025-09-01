using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace SyncClient.Database
{
    /// <summary>
    /// Gestisce la diagnostica delle performance del database
    /// </summary>
    public class PerformanceDiagnostics
    {
        private readonly ILogger _logger;

        public PerformanceDiagnostics(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Analizza le statistiche di wait del database
        /// </summary>
        public async Task PerformWaitStatisticsAnalysisAsync(SqlConnection connection)
        {
            _logger.LogInformation("=== WAIT STATISTICS ANALYSIS ===");
            
            await CheckWaitStatistics(connection);
            
            _logger.LogInformation("=== END WAIT STATISTICS ANALYSIS ===");
        }

        /// <summary>
        /// Verifica le statistiche di wait più critiche
        /// </summary>
        private async Task CheckWaitStatistics(SqlConnection connection)
        {
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandTimeout = 30;
                cmd.CommandText = @"
                    SELECT TOP 10
                        wait_type,
                        waiting_tasks_count,
                        wait_time_ms,
                        CAST(100.0 * wait_time_ms / SUM(wait_time_ms) OVER() AS DECIMAL(5,2)) AS percentage
                    FROM sys.dm_os_wait_stats
                    WHERE wait_type NOT IN (
                        'CLR_SEMAPHORE', 'LAZYWRITER_SLEEP', 'RESOURCE_QUEUE', 'SLEEP_TASK',
                        'SLEEP_SYSTEMTASK', 'WAITFOR', 'BROKER_EVENTHANDLER', 'BROKER_RECEIVE_WAITFOR',
                        'BROKER_TASK_STOP', 'BROKER_TO_FLUSH', 'CHECKPOINT_QUEUE'
                    )
                    AND waiting_tasks_count > 0
                    ORDER BY wait_time_ms DESC";

                using var reader = await cmd.ExecuteReaderAsync();
                
                _logger.LogInformation("--- TOP WAIT TYPES ---");

                var criticalWaits = 0;
                while (await reader.ReadAsync())
                {
                    var waitType = reader["wait_type"]?.ToString() ?? "UNKNOWN";
                    var waitingTasks = reader["waiting_tasks_count"] != DBNull.Value ? Convert.ToInt64(reader["waiting_tasks_count"]) : 0;
                    var waitTimeMs = reader["wait_time_ms"] != DBNull.Value ? Convert.ToInt64(reader["wait_time_ms"]) : 0;
                    var percentage = reader["percentage"] != DBNull.Value ? Convert.ToDecimal(reader["percentage"]) : 0;

                    // Log wait type con percentuale
                    _logger.LogInformation("Wait: {WaitType} | Tasks: {Tasks} | Time: {Time}ms ({Percentage}%)",
                        waitType, waitingTasks, waitTimeMs, percentage);

                    // Identifica wait critici
                    if (waitType.StartsWith("LCK_") || waitType.StartsWith("PAGEIO") || 
                        waitType.StartsWith("WRITELOG") || waitType.Contains("LATCH"))
                    {
                        criticalWaits++;
                        _logger.LogWarning("CRITICAL WAIT DETECTED: {WaitType} - performance impact possible", waitType);
                    }
                }

                // Sommario
                if (criticalWaits > 3)
                {
                    _logger.LogWarning("PERFORMANCE ALERT: {Count} critical wait types detected", criticalWaits);
                }
                else if (criticalWaits == 0)
                {
                    _logger.LogInformation("No critical wait types detected");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Wait statistics check failed: {Error}", ex.Message);
            }
        }
    }
}