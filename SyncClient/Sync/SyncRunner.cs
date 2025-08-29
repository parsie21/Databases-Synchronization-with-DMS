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
                // Utilizza DatabaseManager per la diagnostica
                await _databaseManager.PerformDatabaseDiagnosticsAsync(clientConn, databaseName);

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

                // Log dei risultati usando il metodo dedicato
                LogSyncResults(summary, databaseName, syncStart, syncEnd);

                // cleanup
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

                // cleanup
                await _databaseManager.PerformConnectionCleanup(clientConn, databaseName);

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
        }
        #endregion
    }
}