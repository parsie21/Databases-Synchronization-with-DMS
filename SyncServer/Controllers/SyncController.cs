using Dotmim.Sync.Web.Server;
using Microsoft.AspNetCore.Mvc;

namespace SyncServer.Controllers
{
    /// <summary>
    /// Controller dedicato alla gestione delle richieste di sincronizzazione Dotmim.Sync.
    /// Espone un endpoint POST che riceve le richieste dai client e le processa tramite WebServerAgent.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class SyncController : Controller
    {
        #region Campi
        private readonly WebServerAgent _agent;
        private readonly ILogger<SyncController> _logger;
        #endregion

        #region Costruttore
        /// <summary>
        /// Costruttore che riceve il WebServerAgent e il logger tramite dependency injection.
        /// </summary>
        /// <param name="agent">Istanza di WebServerAgent fornita dai servizi.</param>
        /// <param name="logger">Istanza di ILogger per la registrazione dei log.</param>
        public SyncController(WebServerAgent agent, ILogger<SyncController> logger)
        {
            this._agent = agent;
            this._logger = logger;
        }
        #endregion

        #region Metodi
        /// <summary>
        /// Endpoint POST per la sincronizzazione dei dati.
        /// Riceve la richiesta dal client e la processa tramite Dotmim.Sync.
        /// Stampa su console un messaggio di completamento sincronizzazione.
        /// </summary>
        /// <returns>Risposta HTTP con lo stato della sincronizzazione.</returns>
        [HttpPost]
        public async Task<IActionResult> Sync()
        {
            var startTime = DateTime.Now;
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();

            try
            {
                await _agent.HandleRequestAsync(HttpContext);

                // Scrivi i log solo se la richiesta non è una chiamata interna Dotmim.Sync (esempio: solo se non è una richiesta batch)
                if (!Request.Headers.ContainsKey("dotmim-sync-batch"))
                {
                    var endTime = DateTime.Now;
                    _logger.LogInformation("--- SYNC INFO ---");
                    _logger.LogInformation("Richiesta da: {ClientIp}", clientIp);
                    _logger.LogInformation("Inizio sync:  {StartTime}", startTime);
                    _logger.LogInformation("Fine sync:    {EndTime}", endTime);
                    _logger.LogInformation("Durata:       {Duration}s", (endTime - startTime).TotalSeconds);
                    _logger.LogInformation("Sincronizzazione completata: {Timestamp}", DateTime.Now);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "--- SYNC ERROR ---");
                _logger.LogError("Richiesta da: {ClientIp}", clientIp);
                _logger.LogError("Errore: {Error}", ex.Message);
            }

            return new EmptyResult();
        }
        #endregion
    }
}
