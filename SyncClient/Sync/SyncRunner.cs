using Dotmim.Sync;
using Dotmim.Sync.SqlServer;
using Dotmim.Sync.Web.Client;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using System.Xml.Schema;

namespace SyncClient.Sync
{
    /// <summary>
    /// Classe che gestisce il ciclo di sincronizzazione periodica tra due database locali e server remoti
    /// utilizzando Dotmim.Sync. Esegue la sincronizzazione in sequenza e registra i risultati tramite ILogger.
    /// </summary>
    public class SyncRunner
    {
        #region Fields

        /// <summary>
        /// Stringa di connessione al database primario.
        /// </summary>
        private readonly string _primaryClientConn;

        /// <summary>
        /// URL del servizio di sincronizzazione remoto per il database primario.
        /// </summary>
        private readonly Uri _primaryServiceUrl;

        /// <summary>
        /// Stringa di connessione al database secondario.
        /// </summary>
        private readonly string _secondaryClientConn;

        /// <summary>
        /// URL del servizio di sincronizzazione remoto per il database secondario.
        /// </summary>
        private readonly Uri _secondaryServiceUrl;

        /// <summary>
        /// Logger per la registrazione delle informazioni e degli errori.
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// Intervallo di attesa (in millisecondi) tra una sincronizzazione e la successiva.
        /// </summary>
        private readonly int _delayMs;

        /// <summary>
        /// Contatore dei cicli di sincronizzazione
        /// </summary>
        private int _syncCicleCount;

        #endregion

        #region Constructor

        /// <summary>
        /// Costruisce una nuova istanza di SyncRunner.
        /// </summary>
        /// <param name="primaryClientConn">Stringa di connessione al database primario.</param>
        /// <param name="primaryServiceUrl">URL del servizio di sincronizzazione remoto per il database primario.</param>
        /// <param name="secondaryClientConn">Stringa di connessione al database secondario.</param>
        /// <param name="secondaryServiceUrl">URL del servizio di sincronizzazione remoto per il database secondario.</param>
        /// <param name="logger">Istanza di ILogger per la registrazione dei log.</param>
        /// <param name="delayMs">Intervallo di attesa tra le sincronizzazioni (default: 30 secondi).</param>
        public SyncRunner(
            string primaryClientConn,
            Uri primaryServiceUrl,
            string secondaryClientConn,
            Uri secondaryServiceUrl,
            ILogger logger,
            int delayMs = 30000)
        {
            _primaryClientConn = primaryClientConn ?? throw new ArgumentNullException(nameof(primaryClientConn));
            _primaryServiceUrl = primaryServiceUrl ?? throw new ArgumentNullException(nameof(primaryServiceUrl));
            _secondaryClientConn = secondaryClientConn ?? throw new ArgumentNullException(nameof(secondaryClientConn));
            _secondaryServiceUrl = secondaryServiceUrl ?? throw new ArgumentNullException(nameof(secondaryServiceUrl));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _delayMs = delayMs;
            _syncCicleCount = 0;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Avvia il ciclo infinito di sincronizzazione per i due database in sequenza.
        /// </summary>
        public async Task RunAsync()
        {
            

            while (true)
            {
                try
                {
                    _logger.LogInformation("####################################################");
                    
                    // contatore dei cicli di sync
                    _logger.LogInformation($"Starting syncronization cycle #{++_syncCicleCount}");

                    // Sincronizzazione del database primario
                    _logger.LogInformation("Starting synchronization for Primary Database...");
                    await SynchronizeWithRetryAsync(_primaryClientConn, _primaryServiceUrl, "Primary Database");

                    // Sincronizzazione del database secondario
                    _logger.LogInformation("Starting synchronization for Secondary Database...");
                    await SynchronizeWithRetryAsync(_secondaryClientConn, _secondaryServiceUrl, "Secondary Database");
                    _logger.LogInformation("####################################################");
                }
                catch (Exception ex)
                {
                    // Log degli errori generali con numero del ciclo 
                    _logger.LogError(ex, "Error in synchronization cycle #{CycleNumber}: {Message}",_syncCicleCount, ex.Message);
                }

                // Attende prima di ripetere la sincronizzazione
                _logger.LogInformation("Waiting for {DelayMs}ms before the next synchronization cycle...", _delayMs);
                await Task.Delay(_delayMs);
            }
        }

        /// <summary>
        /// Esegue la sincronizzazione per un singolo database.
        /// </summary>
        /// <param name="clientConn">Stringa di connessione al database client.</param>
        /// <param name="serviceUrl">URL del servizio di sincronizzazione remoto.</param>
        /// <param name="databaseName">Nome del database (per i log).</param>
        private async Task SynchronizeAsync(string clientConn, Uri serviceUrl, string databaseName)
        {
            try
            {

                await PerformSqlDiagnosticsAsync(clientConn, databaseName);

                // Crea i provider Dotmim.Sync per client e server
                var localProvider = new SqlSyncChangeTrackingProvider(clientConn);
                var remoteOrchestrator = new WebRemoteOrchestrator(serviceUrl);
                remoteOrchestrator.HttpClient.Timeout = TimeSpan.FromMinutes(20);
                var agent = new SyncAgent(localProvider, remoteOrchestrator);

                // Determina lo scope corretto in base al database
                string scopeName = databaseName == "Primary Database"
                    ? "PrimaryDatabaseScope"
                    : "SecondaryDatabaseScope";

                _logger.LogInformation("Using scope name: {ScopeName} for {DatabaseName}", scopeName, databaseName);

                // Esegue la sincronizzazione specificando lo scope name
                var syncStart = DateTime.Now;
                var summary = await agent.SynchronizeAsync(scopeName: scopeName);
                var syncEnd = DateTime.Now;

                // Log dei risultati della sincronizzazione
                _logger.LogInformation("......................................");
                _logger.LogInformation("... SYNC SUMMARY FOR {DatabaseName} ...", databaseName);
                _logger.LogInformation("Total changes downloaded: {Downloaded}", summary.TotalChangesAppliedOnClient);
                _logger.LogInformation("Total changes uploaded:   {Uploaded}", summary.TotalChangesAppliedOnServer);
                _logger.LogInformation("Conflicts:                {Conflicts}", summary.TotalResolvedConflicts);
                var duration = (syncEnd - syncStart).TotalSeconds;
                _logger.LogInformation("Duration:                 {Duration:F2}s", duration);

                // Additional logging 
                _logger.LogInformation("......................................");
                _logger.LogInformation("Additional info:");

                // Monitoring sync duration
                if (duration > 30)
                {
                    _logger.LogWarning("Slow Sync detected for {Database}: {Duration:F2}s (Cycle #{Cycle})",
                        databaseName, duration, _syncCicleCount);
                }
                else if (duration > 15)
                {
                    _logger.LogWarning("Moderate slow Sync for {Database}: {Duration:F2}s (Cycle #{Cycle})",
                        databaseName, duration, _syncCicleCount);
                }
                else if (duration > 5)
                {
                    _logger.LogInformation("Normal Sync duration for {Database}: {Duration:F2}s",
                        databaseName, duration);
                }
                else
                {
                    _logger.LogInformation("Fast sync duration for {Database}: {Duration:F2}s",
                        databaseName, duration);
                }

                // Avviso se nessuna modifica è stata applicata
                if (summary.TotalChangesAppliedOnClient == 0 && summary.TotalChangesAppliedOnServer == 0)
                    _logger.LogWarning("No changes applied during synchronization for {DatabaseName}.", databaseName);
                _logger.LogInformation("\n");

                // cleanup
                SafeConnectionCleanup(clientConn, databaseName);
            }
            catch (Exception ex)
            {
                // Log degli errori di sincronizzazione
                _logger.LogError(ex, "Synchronization failed for {DatabaseName}: {Message}", databaseName, ex.Message);

                // Log dell'eccezione interna se presente
                if (ex.InnerException != null)
                {
                    _logger.LogError("Inner exception: {Message}", ex.InnerException.Message);
                }

                // cleanup di emergenza in caso di errore 
                SafeConnectionCleanup(clientConn, databaseName);

                throw;
            }
            
            
        }

        private async Task SynchronizeWithRetryAsync(string clientConn, Uri serviceUrl, string databaseName)
        {
            const int maxRetries = 3;
            const int delayBetweenRetriesMs = 5000;

            for (int attempt=1; attempt<=maxRetries; attempt++)
            {
                try
                {
                    await SynchronizeAsync(clientConn, serviceUrl, databaseName);
                    // Log di successo se non è il primo tentativo
                    if (attempt > 1)
                    {
                        _logger.LogInformation("Sync succeeded on attempt {Attempt} for {Database}", attempt, databaseName);
                    }
                    return;
                }
                catch(Exception ex) when (attempt<maxRetries)
                {
                    _logger.LogWarning("Sync attempt {Attempt}/{MaxRetries} failed for {Database}. " +
                             "Retrying in {Delay}ms. Error: {Error}",
                             attempt, maxRetries, databaseName, delayBetweenRetriesMs, ex.Message);
                    await Task.Delay(delayBetweenRetriesMs);
                }
                catch(Exception ex) when (attempt >= maxRetries)
                {
                    _logger.LogWarning("All {MaxRetries} retry attempts failed for {Database} at cycle #{CycleNumber}. " +
                           "Error: {Error}", maxRetries, databaseName, _syncCicleCount, ex.Message);
                    throw;
                }
            }
        }

        private async Task PerformSqlDiagnosticsAsync(string connectionString, string databaseName)
        {
            try
            {
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();
                
                _logger.LogInformation("=== DIAGNOSTICS FOR {DatabaseName} ===", databaseName);
                
                // 1. Verifica connessioni attive E limiti
                using var connCmd = connection.CreateCommand();
                connCmd.CommandTimeout = 30;
                connCmd.CommandText = @"
                    SELECT 
                        COUNT(*) as ActiveConnections,
                        @@MAX_CONNECTIONS as MaxConnections,
                        (SELECT value FROM sys.configurations WHERE name = 'user connections') as UserConnectionsLimit
                    FROM sys.dm_exec_connections";
                
                using var connReader = await connCmd.ExecuteReaderAsync();
                if (await connReader.ReadAsync())
                {
                    var activeConnections = (int)connReader["ActiveConnections"];
                    var maxConnections = (int)connReader["MaxConnections"];
                    var userLimit = (int)connReader["UserConnectionsLimit"];
                    
                    _logger.LogInformation("Connections - Active: {Active}, Max: {Max}, UserLimit: {UserLimit}", 
                        activeConnections, maxConnections, userLimit);
                    
                    // Alert se ci stiamo avvicinando ai limiti
                    if (activeConnections > maxConnections * 0.8)
                    {
                        _logger.LogWarning("HIGH CONNECTION USAGE: {Active}/{Max} ({Percentage:F1}%)", 
                            activeConnections, maxConnections, (activeConnections * 100.0 / maxConnections));
                    }
                }
                
                // 2. Verifica sessioni per database specifico
                using var dbConnCmd = connection.CreateCommand();
                dbConnCmd.CommandTimeout = 30;
                dbConnCmd.CommandText = @"
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
                using var dbConnReader = await dbConnCmd.ExecuteReaderAsync();
                while (await dbConnReader.ReadAsync())
                {
                    _logger.LogInformation("DB: {Database}, Sessions: {Count}, Status: {Status}, Program: {Program}",
                        dbConnReader["DatabaseName"], dbConnReader["SessionCount"], 
                        dbConnReader["status"], dbConnReader["program_name"]);
                }
                
                // 3. Verifica connessioni bloccate/dormienti
                using var idleCmd = connection.CreateCommand();
                idleCmd.CommandTimeout = 30;
                idleCmd.CommandText = @"
                    SELECT 
                        status,
                        COUNT(*) as Count
                    FROM sys.dm_exec_sessions 
                    WHERE session_id > 50  -- Esclude sessioni di sistema
                    GROUP BY status";
                
                _logger.LogInformation("--- SESSION STATUS ---");
                using var idleReader = await idleCmd.ExecuteReaderAsync();
                while (await idleReader.ReadAsync())
                {
                    var status = idleReader["status"].ToString();
                    var count = (int)idleReader["Count"];
                    _logger.LogInformation("Status: {Status}, Count: {Count}", status, count);
                    
                    // Alert per troppe sessioni dormienti
                    if (status == "sleeping" && count > 50)
                    {
                        _logger.LogWarning("HIGH SLEEPING SESSIONS: {Count} sleeping connections detected", count);
                    }
                }
                
                // 4. Verifica processi bloccati (come prima)
                using var blockCmd = connection.CreateCommand();
                blockCmd.CommandTimeout = 30;
                blockCmd.CommandText = @"
                    SELECT 
                        blocking_session_id,
                        wait_type,
                        wait_time,
                        command
                    FROM sys.dm_exec_requests 
                    WHERE blocking_session_id > 0 OR session_id IN (
                        SELECT DISTINCT blocking_session_id 
                        FROM sys.dm_exec_requests 
                        WHERE blocking_session_id > 0
                    )";
                
                using var blockReader = await blockCmd.ExecuteReaderAsync();
                bool hasBlocks = false;
                while (await blockReader.ReadAsync())
                {
                    hasBlocks = true;
                    _logger.LogWarning("BLOCKED PROCESS: SessionId blocking: {BlockingId}, WaitType: {WaitType}, WaitTime: {WaitTime}ms, Command: {Command}",
                        blockReader["blocking_session_id"], blockReader["wait_type"], 
                        blockReader["wait_time"], blockReader["command"]);
                }
                
                if (!hasBlocks)
                {
                    _logger.LogInformation("No blocked processes detected");
                }
                
                // 5. Se è Primary Database, verifica Change Tracking
                if (databaseName == "Primary Database")
                {
                    await CheckChangeTrackingStatusAsync(connection);
                }
                
                // 6. Verifica tabelle di sincronizzazione DMS
                await CheckSyncTablesAsync(connection, databaseName);
                
                _logger.LogInformation("=== END DIAGNOSTICS ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Diagnostics failed for {DatabaseName}: {Error}", databaseName, ex.Message);
            }
        }

        private async Task CheckChangeTrackingStatusAsync(SqlConnection connection)
        {
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandTimeout = 30;
                cmd.CommandText = @"
                    SELECT 
                        OBJECT_NAME(ct.object_id) AS TableName,
                        ct.min_valid_version,
                        ct.cleanup_version,
                        p.rows AS EstimatedRows,
                        CHANGE_TRACKING_CURRENT_VERSION() as CurrentVersion
                    FROM sys.change_tracking_tables ct
                    JOIN sys.partitions p ON ct.object_id = p.object_id AND p.index_id IN (0, 1)
                    WHERE OBJECT_NAME(ct.object_id) IN ('ana_Clienti', 'ana_Fornitori', 'mag_Banchi')
                    ORDER BY p.rows DESC";
                
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var tableName = reader["TableName"].ToString();
                    var rows = Convert.ToInt64(reader["EstimatedRows"]);
                    var minVersion = reader["min_valid_version"];
                    var cleanupVersion = reader["cleanup_version"];
                    var currentVersion = reader["CurrentVersion"];
                    
                    _logger.LogInformation("ChangeTraking - Table: {Table}, Rows: {Rows:N0}, MinVer: {MinVer}, CleanupVer: {CleanupVer}, CurrentVer: {CurrentVer}",
                        tableName, rows, minVersion, cleanupVersion, currentVersion);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Change Tracking check failed: {Error}", ex.Message);
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
                        TABLE_NAME 
                    FROM INFORMATION_SCHEMA.TABLES 
                    WHERE TABLE_NAME LIKE '%_tracking%' 
                    OR TABLE_NAME LIKE 'scope_info%'
                    ORDER BY TABLE_NAME";
                
                using var reader = await cmd.ExecuteReaderAsync();
                var syncTables = new List<string>();
                while (await reader.ReadAsync())
                {
                    var tableName = reader["TABLE_NAME"]?.ToString();
                    if (!string.IsNullOrEmpty(tableName))
                    {
                        syncTables.Add(tableName);
                    }
                }
                
                if (syncTables.Count > 0)
                {
                    _logger.LogInformation("DMS Sync tables found: {Tables}", string.Join(", ", syncTables));
                }
                else
                {
                    _logger.LogWarning("No DMS sync tables found - this might indicate sync metadata corruption");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Sync tables check failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Cleanup sicuro con diagnostica prima/dopo
        /// </summary>
        private void SafeConnectionCleanup(string connectionString, string databaseName)
        {
            try
            {
                _logger.LogInformation("Starting connection cleanup for {DatabaseName}...", databaseName);
                
                // NUOVO: Diagnostica PRE-cleanup
                var preCleanupConnections = GetConnectionCount(connectionString, databaseName);
                
                // 1. Cleanup del connection pool solo per questa connection string
                using var tempConnection = new SqlConnection(connectionString);
                SqlConnection.ClearPool(tempConnection);

                // 2. Forza garbage collection per rilasciare oggetti .NET non utilizzati
                GC.Collect();
                GC.WaitForPendingFinalizers();
                
                // NUOVO: Breve pausa per permettere il cleanup
                Thread.Sleep(1000);
                
                // NUOVO: Diagnostica POST-cleanup
                var postCleanupConnections = GetConnectionCount(connectionString, databaseName);
                
                _logger.LogInformation("Cleanup completed for {DatabaseName}: {Before} → {After} connections", 
                    databaseName, preCleanupConnections, postCleanupConnections);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Safe connection cleanup failed for {DatabaseName}: {Error}", databaseName, ex.Message);
            }
        }

        /// <summary>
        /// Ottieni il numero di connessioni attive per verificare l'efficacia del cleanup
        /// </summary>
        private int GetConnectionCount(string connectionString, string databaseName)
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
                return -1; // Errore nel conteggio
            }
        }

        #endregion
    }
}
