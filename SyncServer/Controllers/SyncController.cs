using Dotmim.Sync.Web.Server;
using Microsoft.AspNetCore.Mvc;

namespace SyncServer.Controllers
{
    /// <summary>
    /// Controller responsible for handling synchronization requests using Dotmim.Sync.
    /// Provides separate endpoints for synchronizing two distinct databases (DB1 and DB2).
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class SyncController : Controller
    {
        #region Fields
        /// <summary>
        /// WebServerAgent instance for handling synchronization requests for DB1.
        /// </summary>
        private readonly WebServerAgent _agentDb1;

        /// <summary>
        /// WebServerAgent instance for handling synchronization requests for DB2.
        /// </summary>
        private readonly WebServerAgent _agentDb2;

        /// <summary>
        /// Logger instance for logging synchronization details and errors.
        /// </summary>
        private readonly ILogger<SyncController> _logger;
        #endregion

        #region Constructor
        /// <summary>
        /// Initializes a new instance of the <see cref="SyncController"/> class.
        /// Accepts two WebServerAgent instances for DB1 and DB2, and a logger for logging.
        /// </summary>
        /// <param name="agentDb1">WebServerAgent for DB1 synchronization.</param>
        /// <param name="agentDb2">WebServerAgent for DB2 synchronization.</param>
        /// <param name="logger">Logger for logging synchronization details.</param>
        public SyncController(IEnumerable<WebServerAgent> agents, ILogger<SyncController> logger)
        {
            this._agentDb1 = agents.First(a => a.ScopeName == "PrimaryDatabaseScope");
            this._agentDb2 = agents.First(a => a.ScopeName == "SecondaryDatabaseScope");
            this._logger = logger;
        }
        #endregion

        #region Methods


        /// <summary>
        /// Endpoint for synchronizing data with DB1.
        /// Processes the synchronization request using the WebServerAgent for DB1.
        /// Logs detailed information about the synchronization process.
        /// </summary>
        /// <returns>HTTP response indicating the synchronization status for DB1.</returns>
        [HttpPost("db1")]
        public async Task<IActionResult> SyncDb1()
        {
            var startTime = DateTime.Now;
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
            _logger.LogInformation("Sync request for DB1 from {ClientIp}", clientIp);

            try
            {
                

                await _agentDb1.HandleRequestAsync(HttpContext);

                // Log synchronization details if the request is not a batch request
                if (!Request.Headers.ContainsKey("dotmim-sync-batch"))
                {
                    var endTime = DateTime.Now;
                    _logger.LogInformation("--- SYNC INFO (DB1) ---");
                    _logger.LogInformation("Request from: {ClientIp}", clientIp);
                    _logger.LogInformation("Sync start:   {StartTime}", startTime);
                    _logger.LogInformation("Sync end:     {EndTime}", endTime);
                    _logger.LogInformation("Duration:     {Duration}s", (endTime - startTime).TotalSeconds);
                    _logger.LogInformation("Sync completed at: {Timestamp}", DateTime.Now);
                }

                // Log request headers
                _logger.LogInformation("--- REQUEST HEADERS (DB1) ---");
                foreach (var header in Request.Headers)
                {
                    _logger.LogInformation("{HeaderKey}: {HeaderValue}", header.Key, header.Value);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "--- SYNC ERROR (DB1) ---");
                _logger.LogError("Request from: {ClientIp}", clientIp);
                _logger.LogError("Error: {Error}", ex.Message);
                return StatusCode(500, "Error during sync for DB1.");
            }

            return new EmptyResult();
        }

        /// <summary>
        /// Endpoint for synchronizing data with DB2.
        /// Processes the synchronization request using the WebServerAgent for DB2.
        /// Logs detailed information about the synchronization process.
        /// </summary>
        /// <returns>HTTP response indicating the synchronization status for DB2.</returns>
        [HttpPost("db2")]
        public async Task<IActionResult> SyncDb2()
        {
            var startTime = DateTime.Now;
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
            _logger.LogInformation("Sync request for DB2 from {ClientIp}", clientIp);

            try
            {
                await _agentDb2.HandleRequestAsync(HttpContext);

                // Log synchronization details if the request is not a batch request
                if (!Request.Headers.ContainsKey("dotmim-sync-batch"))
                {
                    var endTime = DateTime.Now;
                    _logger.LogInformation("--- SYNC INFO (DB2) ---");
                    _logger.LogInformation("Request from: {ClientIp}", clientIp);
                    _logger.LogInformation("Sync start:   {StartTime}", startTime);
                    _logger.LogInformation("Sync end:     {EndTime}", endTime);
                    _logger.LogInformation("Duration:     {Duration}s", (endTime - startTime).TotalSeconds);
                    _logger.LogInformation("Sync completed at: {Timestamp}", DateTime.Now);
                }

                // Log request headers
                _logger.LogInformation("--- REQUEST HEADERS (DB2) ---");
                foreach (var header in Request.Headers)
                {
                    _logger.LogInformation("{HeaderKey}: {HeaderValue}", header.Key, header.Value);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "--- SYNC ERROR (DB2) ---");
                _logger.LogError("Request from: {ClientIp}", clientIp);
                _logger.LogError("Error: {Error}", ex.Message);
                return StatusCode(500, "Error during sync for DB2.");
            }

            return new EmptyResult();
        }

        #endregion
    }
}


/*
http header created

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