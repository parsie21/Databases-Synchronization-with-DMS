using Dotmim.Sync;
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

        /// <summary>
        /// The Dotmim.Sync agent responsible for synchronization.
        /// </summary>
        private readonly SyncAgent _agent;

        /// <summary>
        /// Logger for synchronization events and errors.
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// Delay in milliseconds between synchronization cycles.
        /// </summary>
        private readonly int _delayMs;

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of <see cref="SyncRunner"/>.
        /// </summary>
        /// <param name="agent">The Dotmim.Sync SyncAgent instance.</param>
        /// <param name="logger">Logger for synchronization events.</param>
        /// <param name="delayMs">Delay in milliseconds between sync cycles (default: 30000 ms).</param>
        public SyncRunner(SyncAgent agent, ILogger logger, int delayMs = 30000)
        {
            _agent = agent ?? throw new ArgumentNullException(nameof(agent));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _delayMs = delayMs;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Starts the synchronization loop.
        /// Runs indefinitely, performing sync cycles and logging results.
        /// </summary>
        public async Task RunAsync()
        {
            while (true)
            {
                try
                {
                    _logger.LogInformation("Starting synchronization...");
                    var syncStart = DateTime.Now;
                    var summary = await _agent.SynchronizeAsync();
                    var syncEnd = DateTime.Now;

                    _logger.LogInformation("--- SYNC SUMMARY ---");
                    _logger.LogInformation("Total changes downloaded: {Downloaded}", summary.TotalChangesAppliedOnClient);
                    _logger.LogInformation("Total changes uploaded:   {Uploaded}", summary.TotalChangesAppliedOnServer);
                    _logger.LogInformation("Conflicts:                {Conflicts}", summary.TotalResolvedConflicts);
                    _logger.LogInformation("Duration:                 {Duration}s", (syncEnd - syncStart).TotalSeconds);

                    // Log a warning if no changes were applied
                    if (summary.TotalChangesAppliedOnClient == 0 && summary.TotalChangesAppliedOnServer == 0)
                        _logger.LogWarning("No changes applied during synchronization.");
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
