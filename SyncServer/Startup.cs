using Dotmim.Sync;
using Dotmim.Sync.SqlServer;
using Microsoft.Extensions.DependencyInjection;
using SyncServer.Configurations;
using System.Threading.Tasks;

namespace SyncServer
{
    /// <summary>
    /// Startup class for configuring the ASP.NET Core application.
    /// Manages the configuration of services and the HTTP request pipeline.
    /// </summary>
    public class Startup
    {
        #region Fields
        /// <summary>
        /// Provides access to application settings, such as connection strings and sync options, defined in appsettings.json.
        /// </summary>
        public IConfiguration Configuration { get; }
        #endregion

        #region Constructor
        /// <summary>
        /// Initializes a new instance of the <see cref="Startup"/> class.
        /// Sets up the configuration for the application.
        /// </summary>
        /// <param name="configuration">Configuration provided by the ASP.NET Core hosting system.</param>
        public Startup(IConfiguration configuration)
        {
            this.Configuration = configuration;
        }
        #endregion

        #region Methods
        /// <summary>
        /// Configures the services required by the application and registers them in the IoC container.
        /// Adds services such as Dotmim.Sync, session management, controllers, and caching.
        /// </summary>
        /// <param name="services">Collection of services to configure for the application.</param>
        public void ConfigureServices(IServiceCollection services)
        {
            // TODO: Refactoring - Questo codice ha troppa duplicazione e troppe responsabilità
            // Idealmente, dovrebbe essere spostato in una classe dedicata del tipo SyncConfigurationService
            // che gestisca la configurazione, validazione e il setup dei provider di sincronizzazione.
            #region Add distributed memory cache and session management
            services.AddDistributedMemoryCache();
            services.AddSession();
            services.AddControllers();
            #endregion

            #region Bind SyncConfiguration from appsettings.json
            services.Configure<SyncConfiguration>(Configuration.GetSection("SyncConfiguration"));
            #endregion

            #region Retrieve configuration
            var syncConfig = Configuration.GetSection("SyncConfiguration").Get<SyncConfiguration>();
            #endregion

            #region Perform validation checks
            
            ValidateConfiguration(syncConfig);
            #endregion

            #region Try to read connection strings from environment variables first, fallback to appsettings.json
            var connectionStringDb1 = Environment.GetEnvironmentVariable("PrimaryDatabaseConnectionString") 
                ?? syncConfig.PrimaryDatabaseConnectionString;
            var connectionStringDb2 = Environment.GetEnvironmentVariable("SecondaryDatabaseConnectionString") 
                ?? syncConfig.SecondaryDatabaseConnectionString;
            #endregion

            #region Log source of connection strings
            var logger = services.BuildServiceProvider().GetRequiredService<ILogger<Startup>>();
            logger.LogInformation("Primary database connection string from: {Source}", 
                Environment.GetEnvironmentVariable("PrimaryDatabaseConnectionString") != null ? "Environment Variable" : "Configuration File");
            logger.LogInformation("Secondary database connection string from: {Source}", 
                Environment.GetEnvironmentVariable("SecondaryDatabaseConnectionString") != null ? "Environment Variable" : "Configuration File");
            #endregion

            #region Configure synchronization for the primary database
            var tablesDb1 = syncConfig.DatabaseTables["PrimaryDatabase"];
            var setupDb1 = new SyncSetup(tablesDb1);
            var optionsDb1 = new SyncOptions
            {
                BatchSize = syncConfig.SyncOptions.BatchSize,
                DbCommandTimeout = syncConfig.SyncOptions.DbCommandTimeout,
                ConflictResolutionPolicy = syncConfig.SyncOptions.ConflictResolutionPolicy == "ServerWins"
                    ? Dotmim.Sync.Enumerations.ConflictResolutionPolicy.ServerWins
                    : Dotmim.Sync.Enumerations.ConflictResolutionPolicy.ClientWins
            };
            var providerDb1 = new SqlSyncChangeTrackingProvider(connectionStringDb1);
            var orchestratorDb1 = new RemoteOrchestrator(providerDb1);
            var flagsDb1 = Dotmim.Sync.Enumerations.SyncProvision.ScopeInfo | Dotmim.Sync.Enumerations.SyncProvision.Triggers | Dotmim.Sync.Enumerations.SyncProvision.ScopeInfoClient;
            orchestratorDb1.DeprovisionAsync(flagsDb1);
            orchestratorDb1.ProvisionAsync(setupDb1, Dotmim.Sync.Enumerations.SyncProvision.NotSet );
            var scopeDb1 = "PrimaryDatabaseScope";
            services.AddSyncServer(providerDb1, setupDb1, optionsDb1, null, scopeDb1);
            logger.LogInformation("Added syncServer for db1");
            #endregion
            
            #region Configure synchronization for the secondary database
            var tablesDb2 = syncConfig.DatabaseTables["SecondaryDatabase"];
            var setupDb2 = new SyncSetup(tablesDb2);
            var optionsDb2 = new SyncOptions
            {
                BatchSize = syncConfig.SyncOptions.BatchSize,
                DbCommandTimeout = syncConfig.SyncOptions.DbCommandTimeout,
                ConflictResolutionPolicy = syncConfig.SyncOptions.ConflictResolutionPolicy == "ServerWins"
                    ? Dotmim.Sync.Enumerations.ConflictResolutionPolicy.ServerWins
                    : Dotmim.Sync.Enumerations.ConflictResolutionPolicy.ClientWins
            };
            var providerDb2 = new SqlSyncChangeTrackingProvider(connectionStringDb2);
            var orchestratorDb2 = new RemoteOrchestrator(providerDb2);
            var flagsDb2 = Dotmim.Sync.Enumerations.SyncProvision.ScopeInfo | Dotmim.Sync.Enumerations.SyncProvision.Triggers | Dotmim.Sync.Enumerations.SyncProvision.ScopeInfoClient;
            orchestratorDb2.DeprovisionAsync(flagsDb2);
            orchestratorDb2.ProvisionAsync(setupDb2, Dotmim.Sync.Enumerations.SyncProvision.NotSet );
            var scopeDb2 = "SecondaryDatabaseScope";
            services.AddSyncServer(providerDb2, setupDb2, optionsDb2, null, scopeDb2);
            logger.LogInformation("Added syncServer for db2");
            #endregion


        }

        /// <summary>
        /// Configures the HTTP request pipeline for the application.
        /// Defines middleware such as error handling, session management, and routing.
        /// Maps endpoints for controllers and health checks.
        /// </summary>
        /// <param name="app">Object for configuring the request pipeline.</param>
        /// <param name="env">Provides information about the hosting environment (e.g., development, production).</param>
        /// <param name="logger">Logger for logging application events.</param>
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger)
        {
            // Enable developer exception page in development mode
            if (env.IsDevelopment())
                app.UseDeveloperExceptionPage();

            // Retrieve SyncConfiguration
            var syncConfig = Configuration.GetSection("SyncConfiguration").Get<SyncConfiguration>();

            // Log tables configured for synchronization for the primary database
            if (syncConfig.DatabaseTables.TryGetValue("PrimaryDatabase", out var primaryTables) && primaryTables.Length > 0)
            {
                logger.LogInformation("Tables configured for synchronization in Primary Database: {Tables}", string.Join(", ", primaryTables));
            }
            else
            {
                logger.LogWarning("No tables configured for synchronization in Primary Database.");
            }


            // Log tables configured for synchronization for the secondary database
            if (syncConfig.DatabaseTables.TryGetValue("SecondaryDatabase", out var secondaryTables) && secondaryTables.Length > 0)
            {
                logger.LogInformation("Tables configured for synchronization in Secondary Database: {Tables}", string.Join(", ", secondaryTables));
            }
            else
            {
                logger.LogWarning("No tables configured for synchronization in Secondary Database.");
            }


            // Add session and routing middleware
            app.UseSession();
            app.UseRouting();

            // Map controller endpoints
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }

        /// <summary>
        /// Validates the SyncConfiguration to ensure all required settings are properly configured.
        /// </summary>
        /// <param name="syncConfig">The SyncConfiguration object to validate.</param>
        private void ValidateConfiguration(SyncConfiguration syncConfig)
        {
            // Check connection strings
            if (string.IsNullOrWhiteSpace(syncConfig.PrimaryDatabaseConnectionString))
                throw new InvalidOperationException("PrimaryDatabaseConnectionString is not configured or is empty.");

            if (string.IsNullOrWhiteSpace(syncConfig.SecondaryDatabaseConnectionString))
                throw new InvalidOperationException("SecondaryDatabaseConnectionString is not configured or is empty.");

            // Check tables configuration
            if (!syncConfig.DatabaseTables.TryGetValue("PrimaryDatabase", out var primaryTables) || primaryTables.Length == 0)
                throw new InvalidOperationException("No tables configured for synchronization in Primary Database.");

            if (!syncConfig.DatabaseTables.TryGetValue("SecondaryDatabase", out var secondaryTables) || secondaryTables.Length == 0)
                throw new InvalidOperationException("No tables configured for synchronization in Secondary Database.");

            // Check sync options
            if (syncConfig.SyncOptions.BatchSize <= 0)
                throw new InvalidOperationException("BatchSize must be greater than 0.");

            if (syncConfig.SyncOptions.DbCommandTimeout <= 0)
                throw new InvalidOperationException("DbCommandTimeout must be greater than 0.");

            if (string.IsNullOrWhiteSpace(syncConfig.SyncOptions.ConflictResolutionPolicy) ||
                (syncConfig.SyncOptions.ConflictResolutionPolicy != "ClientWins" &&
                 syncConfig.SyncOptions.ConflictResolutionPolicy != "ServerWins"))
            {
                throw new InvalidOperationException("ConflictResolutionPolicy must be either 'ClientWins' or 'ServerWins'.");
            }
        }
        #endregion
    }
}


