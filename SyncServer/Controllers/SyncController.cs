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



                /*
                 * 
                 * LOGGER PER HEADERS
                _logger.LogInformation("--- REQUEST HEADERS ---");
                foreach (var header in Request.Headers)
                {
                    _logger.LogInformation("{HeaderKey}: {HeaderValue}", header.Key, header.Value);
                }
                */
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




/*
 Host: syncserver-tds:8080
Accept-Encoding: gzip, deflate
Content-Type: application/json
Cookie: .AspNetCore.Session=CfDJ8H7yX0DcqY9OqX60FznPlbt9bCpolF%2FMFvVrpLUcxpG5kRrLh%2FmoWepQcYoNqcSiIKs2WVo%2FlHptD%2B1oA9VjdEnkw4gDFfmmODmJxi1hwKzT31oaZwtUisLhFw5RO4vvTt4s0WBHnyROKI4eNgL8lfmffx8yn0GaqFKh7TJgp%2FXq
Content-Length: 170
dotmim-sync-session-id: ca9d808e-659c-4708-99e6-00dd58950f89
dotmim-sync-scope-id: e237f589-ced4-43ea-8cf3-1ade760ddb3e
dotmim-sync-scope-name: DefaultScope
dotmim-sync-step: 11
dotmim-sync-serialization-format: {"serializerKey":"json","clientBatchSize":0}
dotmim-sync-version: 1.3.0.0
dotmim-sync-hash: Tmke02LhMi+ohxCpkezgDkjN3QIF3x+qn18Uf/lLToA=
*/




/*
Host: syncserver-tds:8080
Accept-Encoding: gzip, deflate
Content-Type: application/json
Content-Length: 102
dotmim-sync-session-id: b70957f7-4cb0-4bf7-af57-950aafc9c353
dotmim-sync-scope-id: 
dotmim-sync-scope-name: DefaultScope
dotmim-sync-step: 2
dotmim-sync-serialization-format: {"serializerKey":"json","clientBatchSize":0}
dotmim-sync-version: 1.3.0.0
dotmim-sync-hash: Yfut/i9K85PioEn7F6b4UKpFrOu16VjdVLnhm3C56u0=
 */