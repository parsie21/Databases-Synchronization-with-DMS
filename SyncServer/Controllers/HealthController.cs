using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SyncServer.Configurations;

namespace SyncServer.Controllers
{
    [ApiController]
    [Route("api/[Controller]")]
    public class HealthController : ControllerBase
    {
        private readonly SyncConfiguration _syncConfig;

        public HealthController(IOptions<SyncConfiguration> syncConfig)
        {
            _syncConfig = syncConfig.Value;
        }

        /// <summary>
        /// Restituisce lo stato di salute del server.
        /// </summary>

        [HttpGet]
        public IActionResult Get()
        {
            var db1Status = !string.IsNullOrWhiteSpace(_syncConfig.PrimaryDatabaseConnectionString) ? "Healthy" : "Unhealthy";
            var db2Status = !string.IsNullOrWhiteSpace(_syncConfig.SecondaryDatabaseConnectionString) ? "Healthy" : "Unhealthy";

            return Ok(new
            {
                server_status = "Healthy",
                databases = new
                {
                    PrimaryDatabase = db1Status,
                    SecondaryDatabase = db2Status
                },
                timestamp = DateTime.Now
            });
        }
    }
}
