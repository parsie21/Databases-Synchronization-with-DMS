using Dotmim.Sync;
using Dotmim.Sync.SqlServer;
using Dotmim.Sync.Web.Client;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace SyncClient.Sync
{
    /// <summary>
    /// Handles the synchronization loop using Dotmim.Sync SyncAgent.
    /// Responsible for executing periodic sync cycles and logging results.
    /// </summary>
    public class SyncRunner
    {
        #region Fields

        private readonly string _clientConn;
        private readonly Uri _serviceUrl;
        private readonly ILogger _logger;
        private readonly int _delayMs;

        #endregion

        #region Constructor

        
        public SyncRunner (string clientConn, Uri serviceUrl, ILogger logger, int delayMs = 30000)
        {
            this._clientConn = clientConn ?? throw new ArgumentNullException(nameof(clientConn)); ;
            this._serviceUrl = serviceUrl ?? throw new ArgumentNullException(nameof(serviceUrl)); ;
            this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this._delayMs = delayMs;
        }
        #endregion

        #region Methods

       
        public async Task RunAsync()
        {
            while (true)
            {
                try
                {
                    _logger.LogInformation("------------------------------------------");
                    _logger.LogInformation("Starting synchronization...");

                    var localProvider = new SqlSyncChangeTrackingProvider(_clientConn);
                    var remoteOrchestrator = new WebRemoteOrchestrator(_serviceUrl);
                    var agent = new SyncAgent(localProvider, remoteOrchestrator);

                    var syncStart = DateTime.Now;
                    var summary = await agent.SynchronizeAsync();
                    var syncEnd = DateTime.Now;

                    _logger.LogInformation("--- SYNC SUMMARY ---");
                    _logger.LogInformation("Total changes downloaded: {Downloaded}", summary.TotalChangesAppliedOnClient);
                    _logger.LogInformation("Total changes uploaded:   {Uploaded}", summary.TotalChangesAppliedOnServer);
                    _logger.LogInformation("Conflicts:                {Conflicts}", summary.TotalResolvedConflicts);
                    _logger.LogInformation("Duration:                 {Duration}s", (syncEnd - syncStart).TotalSeconds);

                    // Log a warning if no changes were applied
                    if (summary.TotalChangesAppliedOnClient == 0 && summary.TotalChangesAppliedOnServer == 0) _logger.LogWarning("No changes applied during synchronization.");
                    _logger.LogInformation("------------------------------------------");

                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Synchronization failed: {Message}", ex.Message);
                }

                await Task.Delay(_delayMs);
            }
        }

        #endregion
    }
}
