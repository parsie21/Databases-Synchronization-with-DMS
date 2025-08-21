using Dotmim.Sync;
using Dotmim.Sync.SqlServer;
using Dotmim.Sync.SqlServer.ChangeTracking;
using Dotmim.Sync.Web.Client;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Logging;
using SyncClient.Console.Configuration;
using SyncClient.Sync;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SyncClient
{
    public class Program
    {
        public static async Task Main()
        {
            #region Logger Configuration
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
            });
            var configLogger = loggerFactory.CreateLogger<DmsJsonConfiguration>();
            var syncLogger = loggerFactory.CreateLogger<Program>();
            #endregion
            
            try
            {
                #region Configuration and Validation
                var config = new DmsJsonConfiguration("appsetting.json", configLogger);

                syncLogger.LogInformation("Configuration loaded successfully.");
                syncLogger.LogInformation("Connection string: {Conn}", config.GetConnectionString("ClientDb"));
                syncLogger.LogInformation("Service URL: {Url}", config.GetValue("Sync:ServiceUrl"));

                var clientConnBuilder = new SqlConnectionStringBuilder(config.GetConnectionString("ClientDb"))
                {
                    ConnectTimeout = 180
                };
                var clientConn = clientConnBuilder.ConnectionString;

                var serviceUrlString = config.GetValue("Sync:ServiceUrl");
                if (string.IsNullOrWhiteSpace(serviceUrlString))
                    throw new InvalidOperationException("The 'Sync:ServiceUrl' key is not properly configured.");

                var serviceUrl = new Uri(serviceUrlString);
                #endregion


                #region Synchronization Runner
                var syncRunner = new SyncRunner(clientConn, serviceUrl, syncLogger, 50000);
                await syncRunner.RunAsync();
                #endregion
            }
            #region Startup Error Handling
            catch (Exception ex)
            {
                syncLogger.LogCritical(ex, "Fatal error during startup: {Message}", ex.Message);
                Environment.Exit(1);
            }
            #endregion
        }
    }
}