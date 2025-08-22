using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SyncServer.Controllers
{
    /// <summary>
    /// Controller per fornire informazioni generali sul server di sincronizzazione.
    /// Espone un endpoint GET che restituisce dati come versione, ambiente e orario.
    /// </summary>
    [ApiController]
    [Route("api/[Controller]")]
    public class InfoController : ControllerBase
    {
        // Memorizza l'ora di avvio dell'applicazione
        private static readonly DateTime _startTime = DateTime.Now;
        private readonly IConfiguration _configuration;
        private readonly ILogger<InfoController> _logger;

        public InfoController(IConfiguration configuration, ILogger<InfoController> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Get()
        {
            // Lettura parametri SyncOptions
            var syncOptions = _configuration.GetSection("SyncOptions");
            var batchSize = syncOptions.GetValue<int>("BatchSize", 800);
            var dbCommandTimeout = syncOptions.GetValue<int>("DbCommandTimeout", 300);
            var conflictPolicy = syncOptions.GetValue<string>("ConflictResolutionPolicy", "ClientWins");

            // Lettura tabelle sincronizzate
            var tables = _configuration.GetSection("Sync:Tables_Negozio").Get<string[]>();

            _logger.LogInformation("Info endpoint called. BatchSize: {BatchSize}, Tables: {Tables}", batchSize, tables);

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
                batch_size = batchSize,
                db_command_timeout = dbCommandTimeout,
                conflict_resolution_policy = conflictPolicy,
                tables_to_sync = tables
            };
            return Ok(info);
        }
    }
}
