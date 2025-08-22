using Dotmim.Sync;
using Dotmim.Sync.SqlServer;
using Dotmim.Sync.Web.Client;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace SyncClient.Sync
{
    /// <summary>
    /// Classe che gestisce il ciclo di sincronizzazione periodica tra il database locale e il server remoto
    /// utilizzando Dotmim.Sync. Esegue la sincronizzazione ad intervalli regolari e registra i risultati tramite ILogger.
    /// </summary>
    public class SyncRunner
    {
        #region Fields

        /// <summary>
        /// Stringa di connessione al database client.
        /// </summary>
        private readonly string _clientConn;

        /// <summary>
        /// URL del servizio di sincronizzazione remoto.
        /// </summary>
        private readonly Uri _serviceUrl;

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
        /// <param name="clientConn">Stringa di connessione al database client.</param>
        /// <param name="serviceUrl">URL del servizio di sincronizzazione remoto.</param>
        /// <param name="logger">Istanza di ILogger per la registrazione dei log.</param>
        /// <param name="delayMs">Intervallo di attesa tra le sincronizzazioni (default: 30 secondi).</param>
        public SyncRunner(string clientConn, Uri serviceUrl, ILogger logger, int delayMs = 30000)
        {
            this._clientConn = clientConn ?? throw new ArgumentNullException(nameof(clientConn));
            this._serviceUrl = serviceUrl ?? throw new ArgumentNullException(nameof(serviceUrl));
            this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this._delayMs = delayMs;
        }
        #endregion

        #region Methods

        /// <summary>
        /// Avvia il ciclo infinito di sincronizzazione.
        /// Ad ogni iterazione:
        /// - Esegue la sincronizzazione tra il database locale e il server remoto.
        /// - Registra i risultati (numero di modifiche scaricate/caricate, conflitti, durata).
        /// - In caso di errori, li registra tramite il logger.
        /// - Attende il tempo configurato prima di ripetere la sincronizzazione.
        /// </summary>
        public async Task RunAsync()
        {
            while (true)
            {
                try
                {
                    // Log di inizio sincronizzazione
                    _logger.LogInformation("------------------------------------------");
                    _logger.LogInformation("Starting synchronization...");

                    // Crea i provider Dotmim.Sync per client e server
                    var localProvider = new SqlSyncChangeTrackingProvider(_clientConn);
                    var remoteOrchestrator = new WebRemoteOrchestrator(_serviceUrl);
                    remoteOrchestrator.HttpClient.Timeout = TimeSpan.FromMinutes(20);
                    var agent = new SyncAgent(localProvider, remoteOrchestrator);

                    // Esegue la sincronizzazione e misura la durata
                    var syncStart = DateTime.Now;
                    var summary = await agent.SynchronizeAsync();
                    var syncEnd = DateTime.Now;

                    // Log dei risultati della sincronizzazione
                    _logger.LogInformation("--- SYNC SUMMARY ---");
                    _logger.LogInformation("Total changes downloaded: {Downloaded}", summary.TotalChangesAppliedOnClient);
                    _logger.LogInformation("Total changes uploaded:   {Uploaded}", summary.TotalChangesAppliedOnServer);
                    _logger.LogInformation("Conflicts:                {Conflicts}", summary.TotalResolvedConflicts);
                    _logger.LogInformation("Duration:                 {Duration}s", (syncEnd - syncStart).TotalSeconds);

                    // Avviso se nessuna modifica è stata applicata
                    if (summary.TotalChangesAppliedOnClient == 0 && summary.TotalChangesAppliedOnServer == 0)
                        _logger.LogWarning("No changes applied during synchronization.");

                    _logger.LogInformation("------------------------------------------");
                }
                catch (Exception ex)
                {
                    // Log degli errori di sincronizzazione
                    _logger.LogError(ex, "Synchronization failed: {Message}", ex.Message);
                }

                // Attende prima di ripetere la sincronizzazione
                await Task.Delay(millisecondsDelay: _delayMs);
            }
        }

        #endregion
    }
}
