using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SyncServer.Configurations;

namespace SyncServer.Controllers
{
    /// <summary>
    /// Controller for providing general information about the synchronization server.
    /// Exposes a GET endpoint that returns data such as version, environment, and sync configurations.
    /// </summary>
    [ApiController]
    [Route("api/[Controller]")]
    public class InfoController : ControllerBase
    {
        private static readonly DateTime _startTime = DateTime.Now;
        private readonly SyncConfiguration _syncConfig;

        public InfoController(IOptions<SyncConfiguration> syncConfig)
        {
            _syncConfig = syncConfig.Value;
        }

        [HttpGet]
        public IActionResult Get()
        {
            var info = new
            {
                server = "Dotmim.Sync Server",
                environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
                timestamp = DateTime.Now,
                uptime = (DateTime.Now - _startTime).ToString(@"dd\.hh\:mm\:ss"),
                hostname = Environment.MachineName,
                os = Environment.OSVersion.ToString(),
                dotnet_version = Environment.Version.ToString(),
                memory_usage_mb = (GC.GetTotalMemory(false) / (1024 * 1024)),
                sync_options = new
                {
                    batch_size = _syncConfig.SyncOptions.BatchSize,
                    db_command_timeout = _syncConfig.SyncOptions.DbCommandTimeout,
                    conflict_resolution_policy = _syncConfig.SyncOptions.ConflictResolutionPolicy
                },
                databases = new
                {
                    PrimaryDatabase = new
                    {
                        connection_string = _syncConfig.PrimaryDatabaseConnectionString,
                        tables_to_sync = _syncConfig.DatabaseTables["PrimaryDatabase"]
                    },
                    SecondaryDatabase = new
                    {
                        connection_string = _syncConfig.SecondaryDatabaseConnectionString,
                        tables_to_sync = _syncConfig.DatabaseTables["SecondaryDatabase"]
                    }
                }
            };
            return Ok(info);
        }
    }
}
