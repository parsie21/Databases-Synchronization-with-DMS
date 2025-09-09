using Dotmim.Sync;
using Dotmim.Sync.SqlServer;
using Dotmim.Sync.Web.Client;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SyncClient.Database;
using System;
using System.Threading.Tasks;

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
        /// Manager per la gestione del database.
        /// </summary>
        private readonly DatabaseManager _databaseManager;

        /// <summary>
        /// Contatore dei cicli di sincronizzazione
        /// </summary>
        private int _syncCicleCount;

        /// <summary>
        /// Durata dell'ultima sincronizzazione del Primary Database per tracking delle performance
        /// </summary>
        private double _lastPrimaryDuration = 0;

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
            
            // Inizializza il DatabaseManager
            _databaseManager = new DatabaseManager(_logger);
        }

        #endregion

        #region Methods
        
        /// <summary>
        /// Avvia il ciclo infinito di sincronizzazione per i due database in sequenza.
        /// </summary>
        public async Task RunAsync()
        {
            // Verifica iniziale e abilitazione Change Tracking per entrambi i database
            _logger.LogInformation("Performing initial setup and Change Tracking verification...");
            
            var primaryCTResult = await _databaseManager.EnsureChangeTrackingIsEnabledAsync(_primaryClientConn, "Primary Database");
            if (!primaryCTResult)
            {
                _logger.LogError("Failed to ensure Change Tracking on Primary Database. Cannot proceed with synchronization.");
                return;
            }

            var secondaryCTResult = await _databaseManager.EnsureChangeTrackingIsEnabledAsync(_secondaryClientConn, "Secondary Database");
            if (!secondaryCTResult)
            {
                _logger.LogError("Failed to ensure Change Tracking on Secondary Database. Cannot proceed with synchronization.");
                return;
            }

            _logger.LogInformation("Initial setup completed successfully. Starting synchronization cycles...");

            while (true)
            {
                try
                {
                    _logger.LogInformation("####################################################");
                    
                    // contatore dei cicli di sync
                    _logger.LogInformation($"Starting synchronization cycle #{++_syncCicleCount}");

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
                    _logger.LogError(ex, "Error in synchronization cycle #{CycleNumber}: {Message}", _syncCicleCount, ex.Message);
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
                await PerformPreSyncDiagnostics(clientConn, databaseName);

                var localProvider = new SqlSyncChangeTrackingProvider(clientConn);
                var remoteOrchestrator = new WebRemoteOrchestrator(serviceUrl);
                remoteOrchestrator.HttpClient.Timeout = TimeSpan.FromMinutes(20);
                
                // Opzioni sync con disabilitazione constraint automatica
                var syncOptions = new SyncOptions
                {
                    DisableConstraintsOnApplyChanges = true,  // Dotmim.Sync gestisce automaticamente
                    //BatchSize = 1000,
                    //DbCommandTimeout = (int)TimeSpan.FromMinutes(10).TotalSeconds
                };
                
                var agent = new SyncAgent(localProvider, remoteOrchestrator, syncOptions);

                // Gestione conflitti
                agent.LocalOrchestrator.OnConflictingSetup(async args =>
                {
                    if (args.ServerScopeInfo != null)
                    {
                        args.ClientScopeInfo = await agent.LocalOrchestrator.ProvisionAsync(args.ServerScopeInfo, overwrite: true);
                        args.Action = ConflictingSetupAction.Continue;
                        return;
                    }
                    args.Action = ConflictingSetupAction.Abort; 
                });

                // Determina lo scope corretto in base al database
                string scopeName = databaseName == "Primary Database" ? "PrimaryDatabaseScope" : "SecondaryDatabaseScope";

                _logger.LogInformation("Using scope name: {ScopeName} for {DatabaseName}", scopeName, databaseName);

                // Esegue la sincronizzazione specificando lo scope name
                var syncStart = DateTime.Now;
                var summary = await agent.SynchronizeAsync(scopeName: scopeName);
                var syncEnd = DateTime.Now;

                // Traccia durata per diagnostica
                var duration = (syncEnd - syncStart).TotalSeconds;
                if (databaseName == "Primary Database")
                {
                    _lastPrimaryDuration = duration;
                }

                // Log dei risultati usando il metodo dedicato
                LogSyncResults(summary, databaseName, syncStart, syncEnd);

                // === DIAGNOSTICA POST-SINCRONIZZAZIONE ===
                await PerformPostSyncDiagnostics(clientConn, databaseName, duration);

                // Cleanup finale
                await _databaseManager.PerformConnectionCleanup(clientConn, databaseName);
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

                // === DIAGNOSTICA DI ERRORE ===
                await PerformErrorDiagnostics(clientConn, databaseName, ex);

                // Cleanup anche in caso di errore
                await _databaseManager.PerformConnectionCleanup(clientConn, databaseName);

                throw;
            }
        }

        /// <summary>
        /// Esegue diagnostica prima della sincronizzazione con focus sui problemi di rete
        /// </summary>
        private async Task PerformPreSyncDiagnostics(string clientConn, string databaseName)
        {
            try
            {
                // Ottieni la durata dell'ultima sincronizzazione per questo database
                double lastDuration = databaseName == "Primary Database" ? _lastPrimaryDuration : 0;
                
                // Se non ci sono problemi di performance, salta la diagnostica
                if (lastDuration <= 30 && _syncCicleCount > 1) // <= 30 secondi e non primo ciclo
                {
                    _logger.LogDebug("Skipping diagnostics for {DatabaseName} - no issues detected (last duration: {Duration:F2}s)", 
                        databaseName, lastDuration);
                    return;
                }

                // Logica unificata di escalation per entrambi i database
                if (lastDuration > 300) // > 5 minuti
                {
                    _logger.LogWarning("Previous sync took {Duration:F2}s for {DatabaseName} - performing critical diagnostics", 
                        lastDuration, databaseName);
                    await _databaseManager.PerformCriticalDiagnosticsAsync(clientConn, databaseName);
                    
                    // Diagnostica specifica per sync estremi
                    if (lastDuration > 600) // > 10 minuti
                    {
                        _logger.LogError("EXTREMELY SLOW SYNC detected for {DatabaseName} - investigating network/service issues", 
                            databaseName);
                        await InvestigateNetworkAndServiceIssues(databaseName);
                    }
                }
                else if (lastDuration > 60 || _syncCicleCount % 5 == 0) // > 1 minuto o ogni 5 cicli
                {
                    _logger.LogInformation("Performing advanced diagnostics for {DatabaseName}", databaseName);
                    await _databaseManager.PerformAdvancedDiagnosticsAsync(clientConn, databaseName);
                }
                else if (_syncCicleCount == 1 || _syncCicleCount % 10 == 0) // Primo ciclo o ogni 10 cicli
                {
                    _logger.LogInformation("Performing comprehensive diagnostics for {DatabaseName}", databaseName);
                    await _databaseManager.PerformDatabaseDiagnosticsAsync(clientConn, databaseName);
                }
                else
                {
                    // Controllo rapido per situazioni normali
                    _logger.LogDebug("Performing quick health check for {DatabaseName}", databaseName);
                    await _databaseManager.PerformQuickHealthCheckAsync(clientConn, databaseName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Pre-sync diagnostics failed for {DatabaseName}: {Error}", databaseName, ex.Message);
                // Non bloccare la sincronizzazione per errori di diagnostica
            }
        }

        /// <summary>
        /// Investiga problemi di rete e servizio quando il DB sembra OK ma sync è lenta
        /// </summary>
        private async Task InvestigateNetworkAndServiceIssues(string databaseName)
        {
            try
            {
                _logger.LogInformation("=== NETWORK & SERVICE INVESTIGATION FOR {DatabaseName} ===", databaseName);
                
                // 1. Verifica connettività di base al server
                var serviceUrl = databaseName == "Primary Database" ? _primaryServiceUrl : _secondaryServiceUrl;
                await TestServiceConnectivity(serviceUrl, databaseName);
                
                // 2. Verifica status della memoria del processo
                await CheckMemoryUsage();
                
                // 3. Verifica GC pressure
                await CheckGarbageCollectionPressure();
                
                _logger.LogInformation("=== END NETWORK & SERVICE INVESTIGATION ===");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Network investigation failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Testa la connettività al servizio di sync
        /// </summary>
        private async Task TestServiceConnectivity(Uri serviceUrl, string databaseName)
        {
            try
            {
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(30);
                
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                
                try
                {
                    // Test basic connectivity
                    var response = await httpClient.GetAsync(serviceUrl.ToString().TrimEnd('/') + "/health");
                    stopwatch.Stop();
                    
                    _logger.LogInformation("Service connectivity test for {Database}: {Duration}ms (Status: {Status})",
                        databaseName, stopwatch.ElapsedMilliseconds, response.StatusCode);
                        
                    if (stopwatch.ElapsedMilliseconds > 5000) // > 5 secondi
                    {
                        _logger.LogWarning("SLOW SERVICE RESPONSE: {Duration}ms - network latency issue detected", 
                            stopwatch.ElapsedMilliseconds);
                    }
                }
                catch (TaskCanceledException)
                {
                    stopwatch.Stop();
                    _logger.LogError("SERVICE TIMEOUT: No response after 30 seconds - network connectivity issue");
                }
                catch (HttpRequestException ex)
                {
                    stopwatch.Stop();
                    _logger.LogError("SERVICE CONNECTION FAILED: {Error}", ex.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Service connectivity test failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Verifica l'uso della memoria del processo
        /// </summary>
        private async Task CheckMemoryUsage()
        {
            try
            {
                var process = System.Diagnostics.Process.GetCurrentProcess();
                var workingSetMB = process.WorkingSet64 / 1024 / 1024;
                var privateMemoryMB = process.PrivateMemorySize64 / 1024 / 1024;
                
                _logger.LogInformation("Memory Usage - Working Set: {WorkingSet}MB, Private: {Private}MB",
                    workingSetMB, privateMemoryMB);
                    
                if (workingSetMB > 1000) // > 1GB
                {
                    _logger.LogWarning("HIGH MEMORY USAGE: {Memory}MB working set - potential memory pressure", workingSetMB);
                }
                
                // Force async per consistenza con altri metodi
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Memory usage check failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Verifica la pressure del Garbage Collector
        /// </summary>
        private async Task CheckGarbageCollectionPressure()
        {
            try
            {
                var gen0 = GC.CollectionCount(0);
                var gen1 = GC.CollectionCount(1);
                var gen2 = GC.CollectionCount(2);
                var totalMemory = GC.GetTotalMemory(false) / 1024 / 1024; // MB
                
                _logger.LogInformation("GC Stats - Gen0: {Gen0}, Gen1: {Gen1}, Gen2: {Gen2}, Total Memory: {Memory}MB",
                    gen0, gen1, gen2, totalMemory);
                    
                if (gen2 > 10) // Molte collezioni Gen2 indicano pressure
                {
                    _logger.LogWarning("HIGH GC PRESSURE: {Gen2} Gen2 collections - memory pressure detected", gen2);
                }
                
                if (totalMemory > 500) // > 500MB
                {
                    _logger.LogWarning("HIGH MANAGED MEMORY: {Memory}MB - potential memory leak", totalMemory);
                }
                
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("GC pressure check failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Esegue diagnostica dopo la sincronizzazione
        /// </summary>
        private async Task PerformPostSyncDiagnostics(string clientConn, string databaseName, double duration)
        {
            try
            {
                // Diagnostica post-sync solo se ci sono stati problemi di performance
                if (databaseName == "Primary Database" && duration > 30)
                {
                    _logger.LogInformation("Sync duration {Duration:F2}s - performing post-sync analysis", duration);
                    await _databaseManager.PerformAdvancedDiagnosticsAsync(clientConn, databaseName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Post-sync diagnostics failed for {DatabaseName}: {Error}", databaseName, ex.Message);
            }
        }

        /// <summary>
        /// Esegue diagnostica in caso di errore
        /// </summary>
        private async Task PerformErrorDiagnostics(string clientConn, string databaseName, Exception syncException)
        {
            try
            {
                _logger.LogWarning("Performing error diagnostics for {DatabaseName} due to sync failure", databaseName);

                // Diagnostica specifica per tipo di errore
                var errorMessage = syncException.Message.ToLower();
                
                if (errorMessage.Contains("timeout") || errorMessage.Contains("connection"))
                {
                    // Problemi di connessione - diagnostica di rete e connessioni
                    await _databaseManager.PerformDatabaseDiagnosticsAsync(clientConn, databaseName);
                }
                else if (errorMessage.Contains("lock") || errorMessage.Contains("deadlock"))
                {
                    // Problemi di lock - diagnostica avanzata
                    await _databaseManager.PerformAdvancedDiagnosticsAsync(clientConn, databaseName);
                }
                else
                {
                    // Altri errori - diagnostica completa
                    await _databaseManager.PerformCriticalDiagnosticsAsync(clientConn, databaseName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error diagnostics failed for {DatabaseName}: {Error}", databaseName, ex.Message);
            }
        }

        private async Task SynchronizeWithRetryAsync(string clientConn, Uri serviceUrl, string databaseName)
        {
            const int maxRetries = 3;
            const int delayBetweenRetriesMs = 5000;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
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
                catch (Exception ex) when (attempt < maxRetries)
                {
                    _logger.LogWarning("Sync attempt {Attempt}/{MaxRetries} failed for {Database}. " +
                                     "Retrying in {Delay}ms. Error: {Error}",
                                     attempt, maxRetries, databaseName, delayBetweenRetriesMs, ex.Message);
                    await Task.Delay(delayBetweenRetriesMs);
                }
                catch (Exception ex) when (attempt >= maxRetries)
                {
                    _logger.LogWarning("All {MaxRetries} retry attempts failed for {Database} at cycle #{CycleNumber}. " +
                                     "Error: {Error}", maxRetries, databaseName, _syncCicleCount, ex.Message);
                    throw;
                }
            }
        }

        /// <summary>
        /// Registra i risultati della sincronizzazione con analisi delle performance
        /// </summary>
        /// <param name="summary">Risultato della sincronizzazione</param>
        /// <param name="databaseName">Nome del database sincronizzato</param>
        /// <param name="syncStart">Timestamp di inizio sincronizzazione</param>
        /// <param name="syncEnd">Timestamp di fine sincronizzazione</param>
        private void LogSyncResults(SyncResult summary, string databaseName, DateTime syncStart, DateTime syncEnd)
        {
            var duration = (syncEnd - syncStart).TotalSeconds;
            
            // Log dei risultati della sincronizzazione
            _logger.LogInformation("......................................");
            _logger.LogInformation("... SYNC SUMMARY FOR {DatabaseName} ...", databaseName);
            _logger.LogInformation("Total changes downloaded: {Downloaded}", summary.TotalChangesAppliedOnClient);
            _logger.LogInformation("Total changes uploaded:   {Uploaded}", summary.TotalChangesAppliedOnServer);
            _logger.LogInformation("Conflicts:                {Conflicts}", summary.TotalResolvedConflicts);
            _logger.LogInformation("Duration:                 {Duration:F2}s", duration);

            // AGGIUNGI: Informazioni dettagliate per sync molto lente
            if (duration > 300 && databaseName == "Primary Database")
            {
                var changesPerSecond = (summary.TotalChangesAppliedOnClient + summary.TotalChangesAppliedOnServer) / duration;
                _logger.LogWarning("PERFORMANCE ANALYSIS: {Changes} total changes in {Duration:F2}s = {Rate:F2} changes/sec", 
                    summary.TotalChangesAppliedOnClient + summary.TotalChangesAppliedOnServer, duration, changesPerSecond);
                
                if (changesPerSecond < 0.1 && (summary.TotalChangesAppliedOnClient + summary.TotalChangesAppliedOnServer) > 0)
                {
                    _logger.LogError("EXTREMELY LOW THROUGHPUT: {Rate:F3} changes/sec - severe performance issue", changesPerSecond);
                }
                else if ((summary.TotalChangesAppliedOnClient + summary.TotalChangesAppliedOnServer) == 0)
                {
                    _logger.LogError("NO DATA TRANSFER but SLOW SYNC: {Duration:F2}s for 0 changes - network/service issue likely", duration);
                }
            }

            // Additional logging 
            _logger.LogInformation("......................................");
            _logger.LogInformation("Additional info:");

            // Monitoring sync duration con soglie più specifiche
            if (duration > 600) // > 10 minuti
            {
                _logger.LogError("EXTREME Slow Sync detected for {Database}: {Duration:F2}s (Cycle #{Cycle}) - CRITICAL NETWORK/SERVICE ISSUE",
                    databaseName, duration, _syncCicleCount);
            }
            else if (duration > 300) // > 5 minuti
            {
                _logger.LogError("CRITICAL Slow Sync detected for {Database}: {Duration:F2}s (Cycle #{Cycle}) - IMMEDIATE ATTENTION REQUIRED",
                    databaseName, duration, _syncCicleCount);
            }
            else if (duration > 120) // > 2 minuti
            {
                _logger.LogWarning("SEVERE Slow Sync detected for {Database}: {Duration:F2}s (Cycle #{Cycle}) - Performance severely degraded",
                    databaseName, duration, _syncCicleCount);
            }
            else if (duration > 60) // > 1 minuto
            {
                _logger.LogWarning("Slow Sync detected for {Database}: {Duration:F2}s (Cycle #{Cycle}) - Performance degraded",
                    databaseName, duration, _syncCicleCount);
            }
            else if (duration > 30)
            {
                _logger.LogWarning("Moderate slow Sync for {Database}: {Duration:F2}s (Cycle #{Cycle})",
                    databaseName, duration, _syncCicleCount);
            }
            else if (duration > 15)
            {
                _logger.LogInformation("Slightly slow Sync for {Database}: {Duration:F2}s (Cycle #{Cycle})",
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
        }

        #endregion
    }
}