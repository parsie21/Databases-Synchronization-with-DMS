using Dotmim.Sync.SqlServer;
using Dotmim.Sync.Web.Client;
using Microsoft.Extensions.Logging;
using SyncClient.Console.Configuration;
using SyncClient.Sync;
using System;
using System.Collections.Generic;
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
        /// 3. Prepara i dati per il SyncRunner.
        /// 4. Gestisce eventuali errori critici all'avvio.
        /// </summary>
        public static async Task Main()
        {
            #region Logger Setup
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
            var configLogger = loggerFactory.CreateLogger<DmsJsonConfiguration>();
            var syncLogger = loggerFactory.CreateLogger<Program>();
            #endregion

            try
            {
                #region Configuration Loading
                // Carica la configurazione da file JSON e variabili d'ambiente.
                syncLogger.LogInformation("Loading configuration from appsetting.json...");
                var config = new DmsJsonConfiguration("appsetting.json", configLogger);
                syncLogger.LogInformation("Configuration loaded successfully.");
                #endregion

                #region Database Connection Strings Extraction
                // Estrai le stringhe di connessione dai database con priorità alle variabili d'ambiente
                var primaryDbConn = Environment.GetEnvironmentVariable("PrimaryDatabaseConnectionString") 
                    ?? config.GetValue("LocalDatabases:PrimaryDatabase");

                var secondaryDbConn = Environment.GetEnvironmentVariable("SecondaryDatabaseConnectionString") 
                    ?? config.GetValue("LocalDatabases:SecondaryDatabase");

                // Verifica che le stringhe di connessione siano state estratte correttamente
                if (string.IsNullOrEmpty(primaryDbConn))
                    throw new InvalidOperationException("Primary database connection string is missing or empty.");

                if (string.IsNullOrEmpty(secondaryDbConn))
                    throw new InvalidOperationException("Secondary database connection string is missing or empty.");

                // Log dell'origine delle stringhe di connessione
                syncLogger.LogInformation("Primary database connection from: {Source}", 
                    Environment.GetEnvironmentVariable("PrimaryDatabaseConnectionString") != null ? "Environment Variable" : "Configuration File");
                syncLogger.LogInformation("Secondary database connection from: {Source}", 
                    Environment.GetEnvironmentVariable("SecondaryDatabaseConnectionString") != null ? "Configuration File" : "Environment Variable");
                #endregion

                #region Sync Endpoints Extraction
                // Estrai gli URL dei servizi di sincronizzazione con priorità alle variabili d'ambiente
                var primaryEndpoint = Environment.GetEnvironmentVariable("PrimarySyncEndpoint") 
                    ?? config.GetValue("SyncEndpoints:PrimaryDatabase");

                var secondaryEndpoint = Environment.GetEnvironmentVariable("SecondarySyncEndpoint") 
                    ?? config.GetValue("SyncEndpoints:SecondaryDatabase");

                // Verifica che gli endpoint siano stati estratti correttamente
                if (string.IsNullOrEmpty(primaryEndpoint))
                    throw new InvalidOperationException("Primary endpoint URL is missing or empty.");

                if (string.IsNullOrEmpty(secondaryEndpoint))
                    throw new InvalidOperationException("Secondary endpoint URL is missing or empty.");

                // Log dell'origine degli endpoint
                syncLogger.LogInformation("Primary endpoint from: {Source}", 
                    Environment.GetEnvironmentVariable("PrimarySyncEndpoint") != null ? "Environment Variable" : "Configuration File");
                syncLogger.LogInformation("Secondary endpoint from: {Source}", 
                    Environment.GetEnvironmentVariable("SecondarySyncEndpoint") != null ? "Environment Variable" : "Configuration File");

                // Converti le stringhe degli endpoint in oggetti Uri
                var primaryEndpointUri = new Uri(primaryEndpoint);
                var secondaryEndpointUri = new Uri(secondaryEndpoint);
                #endregion

                #region Connection Timeout Configuration
                // Estrai il timeout di connessione
                int connectionTimeout;
                try {
                    var timeoutStr = config.GetValue("ConnectionSettings:ConnectionTimeout");
                    connectionTimeout = string.IsNullOrEmpty(timeoutStr) ? 180 : int.Parse(timeoutStr);
                    syncLogger.LogInformation("Connection timeout set to {Timeout} seconds", connectionTimeout);
                } 
                catch (Exception ex) {
                    syncLogger.LogWarning("Failed to parse connection timeout, using default: {Message}", ex.Message);
                    connectionTimeout = 180;
                }
                #endregion

                #region SQL Connection Enhancement
                // Applica il timeout di connessione alle stringhe di connessione
                var primaryDbConnBuilder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(primaryDbConn)
                {
                    ConnectTimeout = connectionTimeout,
                    CommandTimeout = 800
                };
                var secondaryDbConnBuilder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(secondaryDbConn)
                {
                    ConnectTimeout = connectionTimeout,
                    CommandTimeout = 800

                };

                // Aggiorna le stringhe di connessione con il timeout configurato
                primaryDbConn = primaryDbConnBuilder.ConnectionString;
                secondaryDbConn = secondaryDbConnBuilder.ConnectionString;
                #endregion

                #region Configuration Verification Logging
                // Log dei valori estratti per verifica
                syncLogger.LogInformation("Primary Database Connection: {Connection}", primaryDbConn);
                syncLogger.LogInformation("Primary Endpoint: {Endpoint}", primaryEndpointUri);
                syncLogger.LogInformation("Secondary Database Connection: {Connection}", secondaryDbConn);
                syncLogger.LogInformation("Secondary Endpoint: {Endpoint}", secondaryEndpointUri);
                #endregion

                #region SyncRunner Initialization
                // A questo punto, abbiamo tutte le informazioni necessarie per creare un SyncRunner
                syncLogger.LogInformation("Configuration prepared for synchronization of two databases.");
                syncLogger.LogInformation("Creating SyncRunner instance...");

                // Ritardo casuale per evitare start simultanei
                var random = new Random();
                var startupDelay = random.Next(5000, 15000); // 5-15 secondi di ritardo casuale
                syncLogger.LogInformation("Applying startup delay of {Delay}ms to avoid concurrent startup issues...", startupDelay);
                await Task.Delay(startupDelay);

                
                // Crea un'istanza di SyncRunner con i dati configurati
                var syncRunner = new SyncRunner(
                    primaryDbConn,
                    primaryEndpointUri,
                    secondaryDbConn,
                    secondaryEndpointUri,
                    syncLogger,
                    50000  // 50 secondi di delay tra le sincronizzazioni
                );

                // Avvia il processo di sincronizzazione
                syncLogger.LogInformation("Starting synchronization process...");
                await syncRunner.RunAsync();
                #endregion


            }
            #region Global Error Handling
            catch (Exception ex)
            {
                syncLogger.LogCritical(ex, "Fatal error during startup: {Message}", ex.Message);
                Environment.Exit(1);
            }
            #endregion
        }
    }
}