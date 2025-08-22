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
    /// <summary>
    /// Entry point dell'applicazione di sincronizzazione client.
    /// Configura il logging, carica la configurazione, valida i parametri e avvia il ciclo di sincronizzazione.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Metodo principale asincrono.
        /// 1. Configura il logger.
        /// 2. Carica e valida la configurazione.
        /// 3. Avvia il runner di sincronizzazione.
        /// 4. Gestisce eventuali errori critici all'avvio.
        /// </summary>
        public static async Task Main()
        {
            #region Logger Configuration
            // Configurazione del logger console con formato semplice e timestamp.
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddSimpleConsole(options =>
                {
                    options.IncludeScopes = true;
                    options.SingleLine = true;
                    options.TimestampFormat = "HH:mm:ss ";
                });
            });
            // Logger per la configurazione e per la sincronizzazione.
            var configLogger = loggerFactory.CreateLogger<DmsJsonConfiguration>();
            var syncLogger = loggerFactory.CreateLogger<Program>();
            #endregion
            
            try
            {
                #region Configuration and Validation
                // Carica la configurazione da file JSON e variabili d'ambiente.
                var config = new DmsJsonConfiguration("appsetting.json", configLogger);

                // Log di conferma caricamento configurazione e parametri principali.
                syncLogger.LogInformation("Configuration loaded successfully.");
                syncLogger.LogInformation("Connection string: {Conn}", config.GetConnectionString("ClientDb"));
                syncLogger.LogInformation("Service URL: {Url}", config.GetValue("Sync:ServiceUrl"));

                // Costruisce la stringa di connessione SQL con timeout personalizzato.
                var clientConnBuilder = new SqlConnectionStringBuilder(config.GetConnectionString("ClientDb"))
                {
                    ConnectTimeout = 180
                };
                var clientConn = clientConnBuilder.ConnectionString;

                // Recupera e valida l'URL del servizio di sincronizzazione.
                var serviceUrlString = config.GetValue("Sync:ServiceUrl");
                if (string.IsNullOrWhiteSpace(serviceUrlString))
                    throw new InvalidOperationException("The 'Sync:ServiceUrl' key is not properly configured.");

                var serviceUrl = new Uri(serviceUrlString);
                #endregion

                #region Synchronization Runner
                // Istanzia e avvia il ciclo di sincronizzazione periodica.
                // Il delay tra le sincronizzazioni è impostato a 50 secondi.
                var syncRunner = new SyncRunner(clientConn, serviceUrl, syncLogger, 50000);
                await syncRunner.RunAsync();
                #endregion
            }
            #region Startup Error Handling
            // Gestione degli errori critici all'avvio: logga e termina il processo.
            catch (Exception ex)
            {
                syncLogger.LogCritical(ex, "Fatal error during startup: {Message}", ex.Message);
                Environment.Exit(1);
            }
            #endregion
        }
    }
}