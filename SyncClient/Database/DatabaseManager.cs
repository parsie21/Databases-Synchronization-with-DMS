using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace SyncClient.Database
{
    /// <summary>
    /// Classe principale per la gestione e controllo del database.
    /// Coordina le varie operazioni di diagnostica, manutenzione e gestione delle connessioni.
    /// </summary>
    public class DatabaseManager
    {
        #region Fields
        private readonly ILogger _logger;
        private readonly ConnectionDiagnostics _connectionDiagnostics;
        private readonly LockDiagnostics _lockDiagnostics;
        private readonly ChangeTrackingDiagnostics _changeTrackingDiagnostics;
        private readonly PerformanceDiagnostics _performanceDiagnostics;
        #endregion

        #region Constructor
        public DatabaseManager(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Inizializza i componenti diagnostici
            _connectionDiagnostics = new ConnectionDiagnostics(_logger);
            _lockDiagnostics = new LockDiagnostics(_logger);
            _changeTrackingDiagnostics = new ChangeTrackingDiagnostics(_logger);
            _performanceDiagnostics = new PerformanceDiagnostics(_logger);
        }
        #endregion

        #region Public Methods

        /// <summary>
        /// Esegue diagnostica completa del database per situazioni normali
        /// </summary>
        public async Task PerformDatabaseDiagnosticsAsync(string connectionString, string databaseName)
        {
            try
            {
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                _logger.LogInformation("=== COMPREHENSIVE DIAGNOSTICS FOR {DatabaseName} ===", databaseName);

                // Diagnostica delle connessioni
                await _connectionDiagnostics.CheckActiveConnectionsAsync(connection);
                await _connectionDiagnostics.CheckSessionStatusAsync(connection);
                await _connectionDiagnostics.CheckSessionBreakdownAsync(connection);

                // Diagnostica dei lock (solo processi bloccati per diagnostica base)
                await _lockDiagnostics.CheckBlockedProcessesAsync(connection);

                // Diagnostica Change Tracking (stato base)
                await _changeTrackingDiagnostics.CheckChangeTrackingStatusAsync(connection);
                await _changeTrackingDiagnostics.CheckSyncTablesAsync(connection, databaseName);

                _logger.LogInformation("=== END COMPREHENSIVE DIAGNOSTICS ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Comprehensive diagnostic failed for {DatabaseName}: {Error}", databaseName, ex.Message);
            }
        }

        /// <summary>
        /// Esegue diagnostica avanzata per problemi di performance critici
        /// </summary>
        public async Task PerformAdvancedDiagnosticsAsync(string connectionString, string databaseName)
        {
            try
            {
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                _logger.LogInformation("=== ADVANCED PERFORMANCE DIAGNOSTICS FOR {DatabaseName} ===", databaseName);

                // Diagnostica lock avanzata (analisi completa)
                await _lockDiagnostics.PerformLockAnalysisAsync(connection);

                // Diagnostica Change Tracking avanzata (versioni, cleanup, sizing)
                await _changeTrackingDiagnostics.PerformChangeTrackingAnalysisAsync(connection);

                // Diagnostica delle performance (wait statistics)
                await _performanceDiagnostics.PerformWaitStatisticsAnalysisAsync(connection);

                _logger.LogInformation("=== END ADVANCED DIAGNOSTICS ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Advanced diagnostic failed for {DatabaseName}: {Error}", databaseName, ex.Message);
            }
        }

        /// <summary>
        /// Esegue diagnostica completa per problemi critici di performance (utilizza tutti i metodi avanzati)
        /// </summary>
        public async Task PerformCriticalDiagnosticsAsync(string connectionString, string databaseName)
        {
            try
            {
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                _logger.LogInformation("=== CRITICAL ISSUE DIAGNOSTICS FOR {DatabaseName} ===", databaseName);

                // Tutte le diagnostiche di connessione
                await _connectionDiagnostics.CheckActiveConnectionsAsync(connection);
                await _connectionDiagnostics.CheckSessionStatusAsync(connection);
                await _connectionDiagnostics.CheckSessionBreakdownAsync(connection);

                // Analisi completa dei lock
                await _lockDiagnostics.PerformLockAnalysisAsync(connection);

                // Analisi completa del Change Tracking
                await _changeTrackingDiagnostics.CheckChangeTrackingStatusAsync(connection);
                await _changeTrackingDiagnostics.PerformChangeTrackingAnalysisAsync(connection);
                await _changeTrackingDiagnostics.CheckSyncTablesAsync(connection, databaseName);

                // Analisi delle performance
                await _performanceDiagnostics.PerformWaitStatisticsAnalysisAsync(connection);

                _logger.LogInformation("=== END CRITICAL ISSUE DIAGNOSTICS ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical diagnostic failed for {DatabaseName}: {Error}", databaseName, ex.Message);
            }
        }

        /// <summary>
        /// Verifica e abilita Change Tracking se necessario
        /// </summary>
        public async Task<bool> EnsureChangeTrackingIsEnabledAsync(string connectionString, string databaseName)
        {
            return await _changeTrackingDiagnostics.EnsureChangeTrackingIsEnabledAsync(connectionString, databaseName);
        }

        /// <summary>
        /// Esegue cleanup delle connessioni del database
        /// </summary>
        public async Task PerformConnectionCleanup(string connectionString, string databaseName)
        {
            await _connectionDiagnostics.PerformConnectionCleanup(connectionString, databaseName);
        }

        /// <summary>
        /// Ottiene il numero di connessioni attive
        /// </summary>
        public int GetConnectionCount(string connectionString, string databaseName)
        {
            return _connectionDiagnostics.GetConnectionCount(connectionString, databaseName);
        }

        /// <summary>
        /// Diagnostica rapida per identificare problemi comuni
        /// </summary>
        public async Task PerformQuickHealthCheckAsync(string connectionString, string databaseName)
        {
            try
            {
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                _logger.LogInformation("=== QUICK HEALTH CHECK FOR {DatabaseName} ===", databaseName);

                // Verifica connessioni e sessioni bloccate
                await _connectionDiagnostics.CheckActiveConnectionsAsync(connection);
                await _lockDiagnostics.CheckBlockedProcessesAsync(connection);

                // Verifica stato Change Tracking
                await _changeTrackingDiagnostics.CheckChangeTrackingStatusAsync(connection);

                _logger.LogInformation("=== END QUICK HEALTH CHECK ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Quick health check failed for {DatabaseName}: {Error}", databaseName, ex.Message);
            }
        }

        #endregion
    }
}
