using Microsoft.AspNetCore.Mvc;

namespace SyncServer.Controllers
{
    /// <summary>
    /// Controller per fornire informazioni generali sul server di sincronizzazione.
    /// Espone un endpoint GET che restituisce dati come versione, ambiente e orario.
    /// </summary>
    /// 
    [ApiController]
    [Route("api/[Controller]")]
    public class InfoController : ControllerBase
    {

        // Memorizza l'ora di avvio dell'applicazione
        private static readonly DateTime _startTime = DateTime.Now;
        [HttpGet]
        public IActionResult Get()
        {
            var info = new
            {
                server = "Dotmim.Sync Server",
                // version = typeof(InfoController).Assembly.GetName().Version?.ToString(),
                environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
                timestamp = DateTime.Now,
                uptime = (DateTime.Now - _startTime).ToString(@"dd\.hh\:mm\:ss"),
                hostname = Environment.MachineName,
                os = Environment.OSVersion.ToString(),
                dotnet_version = Environment.Version.ToString(),
                memory_usage_mb = (GC.GetTotalMemory(false) / (1024 * 1024)),
                batch_size = 2000 // Valore di esempio, puoi recuperarlo dinamicamente se necessario
            };
            return Ok(info);
        }
    }
}
