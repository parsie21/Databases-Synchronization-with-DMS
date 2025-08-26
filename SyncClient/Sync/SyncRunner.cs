using Dotmim.Sync;
using Dotmim.Sync.SqlServer;
using Dotmim.Sync.Web.Client;
using Microsoft.Extensions.Logging;
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
                    // Sincronizzazione del database primario
                    _logger.LogInformation("Starting synchronization for Primary Database...");
                    await SynchronizeAsync(_primaryClientConn, _primaryServiceUrl, "Primary Database");

                    // Sincronizzazione del database secondario
                    _logger.LogInformation("Starting synchronization for Secondary Database...");
                    await SynchronizeAsync(_secondaryClientConn, _secondaryServiceUrl, "Secondary Database");
                }
                catch (Exception ex)
                {
                    // Log degli errori generali
                    _logger.LogError(ex, "An error occurred during synchronization: {Message}", ex.Message);
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
                _logger.LogInformation("--- SYNC SUMMARY FOR {DatabaseName} ---", databaseName);
                _logger.LogInformation("Total changes downloaded: {Downloaded}", summary.TotalChangesAppliedOnClient);
                _logger.LogInformation("Total changes uploaded:   {Uploaded}", summary.TotalChangesAppliedOnServer);
                _logger.LogInformation("Conflicts:                {Conflicts}", summary.TotalResolvedConflicts);
                _logger.LogInformation("Duration:                 {Duration}s", (syncEnd - syncStart).TotalSeconds);

                // Avviso se nessuna modifica è stata applicata
                if (summary.TotalChangesAppliedOnClient == 0 && summary.TotalChangesAppliedOnServer == 0)
                    _logger.LogWarning("No changes applied during synchronization for {DatabaseName}.", databaseName);
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
            }
        }

        #endregion
    }
}
