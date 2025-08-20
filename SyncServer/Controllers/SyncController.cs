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
        #endregion

        #region Costruttore
        /// <summary>
        /// Costruttore che riceve il WebServerAgent tramite dependency injection.
        /// </summary>
        /// <param name="agent">Istanza di WebServerAgent fornita dai servizi.</param>
        public SyncController(WebServerAgent agent)
        {
            this._agent = agent;
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

            await _agent.HandleRequestAsync(HttpContext);

            // Scrivi i log solo se la richiesta non è una chiamata interna Dotmim.Sync (esempio: solo se non è una richiesta batch)
            if (!Request.Headers.ContainsKey("dotmim-sync-batch"))
            {
                var endTime = DateTime.Now;
                Console.WriteLine("--- SYNC INFO ---");
                Console.WriteLine($"Richiesta da: {clientIp}");
                Console.WriteLine($"Inizio sync:  {startTime}");
                Console.WriteLine($"Fine sync:    {endTime}");
                Console.WriteLine($"Durata:       {(endTime - startTime).TotalSeconds} secondi");
                Console.WriteLine($"Sincronizzazione completata: {DateTime.Now}");
            }
   
            return new EmptyResult();
            
        }
        #endregion
    }
}
