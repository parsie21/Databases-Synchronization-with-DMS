using Dotmim.Sync;
using Dotmim.Sync.SqlServer;
using Microsoft.Extensions.DependencyInjection;
using SyncServer.Configurations;

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
            // Add distributed memory cache and session management
            services.AddDistributedMemoryCache();
            services.AddSession();
            services.AddControllers();

            // Bind SyncConfiguration from appsettings.json
            services.Configure<SyncConfiguration>(Configuration.GetSection("SyncConfiguration"));

            // Retrieve configuration
            var syncConfig = Configuration.GetSection("SyncConfiguration").Get<SyncConfiguration>();

            // Perform validation checks
            ValidateConfiguration(syncConfig);

            // Try to read connection strings from environment variables first, fallback to appsettings.json
            var connectionStringDb1 = Environment.GetEnvironmentVariable("PrimaryDatabaseConnectionString") 
                ?? syncConfig.PrimaryDatabaseConnectionString;
            var connectionStringDb2 = Environment.GetEnvironmentVariable("SecondaryDatabaseConnectionString") 
                ?? syncConfig.SecondaryDatabaseConnectionString;

            // Log source of connection strings
            var logger = services.BuildServiceProvider().GetRequiredService<ILogger<Startup>>();
            logger.LogInformation("Primary database connection string from: {Source}", 
                Environment.GetEnvironmentVariable("PrimaryDatabaseConnectionString") != null ? "Environment Variable" : "Configuration File");
            logger.LogInformation("Secondary database connection string from: {Source}", 
                Environment.GetEnvironmentVariable("SecondaryDatabaseConnectionString") != null ? "Environment Variable" : "Configuration File");

            // Configure synchronization for the primary database
            var tablesDb1 = syncConfig.DatabaseTables["PrimaryDatabase"];
            var optionsDb1 = new SyncOptions
            {
                BatchSize = syncConfig.SyncOptions.BatchSize,
                DbCommandTimeout = syncConfig.SyncOptions.DbCommandTimeout,
                ConflictResolutionPolicy = syncConfig.SyncOptions.ConflictResolutionPolicy == "ServerWins"
                    ? Dotmim.Sync.Enumerations.ConflictResolutionPolicy.ServerWins
                    : Dotmim.Sync.Enumerations.ConflictResolutionPolicy.ClientWins
            };
            var providerDb1 = new SqlSyncChangeTrackingProvider(connectionStringDb1);
            services.AddSyncServer(providerDb1, new SyncSetup(tablesDb1), optionsDb1, null, "PrimaryDatabaseScope");

            // Configure synchronization for the secondary database
            var tablesDb2 = syncConfig.DatabaseTables["SecondaryDatabase"];
            var optionsDb2 = new SyncOptions
            {
                BatchSize = syncConfig.SyncOptions.BatchSize,
                DbCommandTimeout = syncConfig.SyncOptions.DbCommandTimeout,
                ConflictResolutionPolicy = syncConfig.SyncOptions.ConflictResolutionPolicy == "ServerWins"
                    ? Dotmim.Sync.Enumerations.ConflictResolutionPolicy.ServerWins
                    : Dotmim.Sync.Enumerations.ConflictResolutionPolicy.ClientWins
            };
            var providerDb2 = new SqlSyncChangeTrackingProvider(connectionStringDb2);
            services.AddSyncServer(providerDb2, new SyncSetup(tablesDb2), optionsDb2, null, "SecondaryDatabaseScope");

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




/*
 using Dotmim.Sync;
using Dotmim.Sync.SqlServer;
using Microsoft.Extensions.DependencyInjection;

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
            // Add distributed memory cache and session management
            services.AddDistributedMemoryCache();
            services.AddSession();
            services.AddControllers();

            // Retrieve sync options from appsettings.json
            #region Sync Options Configuration
            var syncOptionsSection = Configuration.GetSection("SyncOptions");
            var batchSize = syncOptionsSection.GetValue<int>("BatchSize", 800);
            var dbCommandTimeout = syncOptionsSection.GetValue<int>("DbCommandTimeout", 300);
            var conflictPolicyStr = syncOptionsSection.GetValue<string>("ConflictResolutionPolicy", "ClientWins");
            var conflictPolicy = conflictPolicyStr == "ServerWins"
                ? Dotmim.Sync.Enumerations.ConflictResolutionPolicy.ServerWins
                : Dotmim.Sync.Enumerations.ConflictResolutionPolicy.ClientWins;
            #endregion

            // Configure synchronization for the first database (ServerDbZeusCfgTerraDiSiena)
            #region DB1 Configuration
            var connectionStringDb1 = Configuration.GetConnectionString("ServerDbZeusCfgTerraDiSiena");
            var tablesDb1 = Configuration.GetSection("Sync:ServerDbZeusCfgTerraDiSiena").Get<string[]>();
            if (tablesDb1 == null || tablesDb1.Length == 0)
                throw new InvalidOperationException("No tables configured for synchronization in ServerDbZeusCfgTerraDiSiena.");

            var setupDb1 = new SyncSetup(tablesDb1);
            var optionsDb1 = new SyncOptions
            {
                BatchSize = batchSize,
                DbCommandTimeout = dbCommandTimeout,
                ConflictResolutionPolicy = conflictPolicy
            };
            var providerDb1 = new SqlSyncChangeTrackingProvider(connectionStringDb1);
            services.AddSyncServer(providerDb1, setupDb1, optionsDb1, null, "ZeusCfgTerraDiSienaScope", "DB_CFG_TDS");
            #endregion

            // Configure synchronization for the second database (ServerDbTERRADISIENA)
            #region DB2 Configuration
            var connectionStringDb2 = Configuration.GetConnectionString("ServerDbTERRADISIENA");
            var tablesDb2 = Configuration.GetSection("Sync:Tables_Negozio").Get<string[]>();
            if (tablesDb2 == null || tablesDb2.Length == 0)
                throw new InvalidOperationException("No tables configured for synchronization in ServerDbTERRADISIENA.");

            var setupDb2 = new SyncSetup(tablesDb2);
            var optionsDb2 = new SyncOptions
            {
                BatchSize = batchSize,
                DbCommandTimeout = dbCommandTimeout,
                ConflictResolutionPolicy = conflictPolicy
            };
            var providerDb2 = new SqlSyncChangeTrackingProvider(connectionStringDb2);
            services.AddSyncServer(providerDb2, setupDb2, optionsDb2, null, "TerraDiSienaScope", "DB_TDS");
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

            // Log tables configured for synchronization for DB1
            var tablesDb1 = Configuration.GetSection("Sync:ServerDbZeusCfgTerraDiSiena").Get<string[]>();
            if (tablesDb1 != null && tablesDb1.Length > 0)
                logger.LogInformation("Tables configured for synchronization in DB1 (ServerDbZeusCfgTerraDiSiena): {Tables}", string.Join(", ", tablesDb1));
            else
                logger.LogWarning("No tables configured for synchronization in DB1 (ServerDbZeusCfgTerraDiSiena).");

            // Log tables configured for synchronization for DB2
            var tablesDb2 = Configuration.GetSection("Sync:Tables_Negozio").Get<string[]>();
            if (tablesDb2 != null && tablesDb2.Length > 0)
                logger.LogInformation("Tables configured for synchronization in DB2 (ServerDbTERRADISIENA): {Tables}", string.Join(", ", tablesDb2));
            else
                logger.LogWarning("No tables configured for synchronization in DB2 (ServerDbTERRADISIENA).");

            // Add session and routing middleware
            app.UseSession();
            app.UseRouting();

            // Map controller endpoints
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
        #endregion
    }
}

 */