using Dotmim.Sync;
using Dotmim.Sync.SqlServer;

namespace SyncServer
{
    /// <summary>
    /// Classe di avvio dell'applicazione ASP.NET Core.
    /// Gestisce la configurazione dei servizi e della pipeline HTTP.
    /// </summary>
    public class Startup
    {
        #region Campi
        /// <summary>
        /// Oggetto di configurazione che fornisce accesso alle impostazioni dell'applicazione,
        /// come stringhe di connessione e parametri definiti in appsettings.json.
        /// </summary>
        public IConfiguration Configuration { get; }
        #endregion

        #region Costruttore
        /// <summary>
        /// Costruttore della classe Startup.
        /// Inizializza la configurazione dell'applicazione.
        /// </summary>
        /// <param name="configuration">Configurazione fornita dal sistema di hosting ASP.NET Core.</param>
        public Startup(IConfiguration configuration)
        {
            this.Configuration = configuration;
        }
        #endregion

        #region Metodi
        /// <summary>
        /// Metodo chiamato all'avvio per registrare i servizi necessari nell'IoC container.
        /// Qui si aggiungono servizi come Dotmim.Sync, sessioni, controller, cache, ecc.
        /// </summary>
        /// <param name="services">Collezione di servizi da configurare per l'applicazione.</param>
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDistributedMemoryCache();
            services.AddSession();
            services.AddControllers();
            // aggiungere autenticazione
            // aggiungere autorizzazione

            var tables = Configuration.GetSection("Sync:Tables_Negozio").Get<string[]>();
            var setup = new SyncSetup(tables);

            // parametri di configurazione di syncOptions da appsetting
            var syncOptionsSection = Configuration.GetSection("SyncOptions");
            var batchSize = syncOptionsSection.GetValue<int>("BatchSize", 800);
            var dbCommandTimeout = syncOptionsSection.GetValue<int>("DbCommandTimeout", 300);
            var conflictPolicyStr = syncOptionsSection.GetValue<string>("ConflictResolutionPolicy", "ClientWins");
            var conflictPolicy = conflictPolicyStr == "ServerWins"
                ? Dotmim.Sync.Enumerations.ConflictResolutionPolicy.ServerWins
                : Dotmim.Sync.Enumerations.ConflictResolutionPolicy.ClientWins;


            var options = new SyncOptions
            {
                BatchSize = batchSize,
                DbCommandTimeout = dbCommandTimeout,
                ConflictResolutionPolicy = conflictPolicy
            };
            var provider = new SqlSyncChangeTrackingProvider(Configuration.GetConnectionString("ServerDb"));


            services.AddSyncServer(provider, setup, options);
        }

        /// <summary>
        /// Configura la pipeline di gestione delle richieste HTTP dell'applicazione.
        /// Qui vengono definiti i middleware utilizzati (es. gestione errori, sessioni, routing)
        /// e vengono mappati gli endpoint, inclusi i controller e l'endpoint di health check.
        /// Questo metodo viene chiamato all'avvio dall'ambiente di hosting ASP.NET Core.
        /// </summary>
        /// <param name="app">Oggetto che consente di configurare la pipeline delle richieste.</param>
        /// <param name="env">Informazioni sull'ambiente di hosting (sviluppo, produzione, ecc.).</param>
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger)
        {
            if (env.IsDevelopment())
                app.UseDeveloperExceptionPage();

            // Log delle tabelle da sincronizzare
            var tables = Configuration.GetSection("Sync:Tables_Negozio").Get<string[]>();
            if (tables != null && tables.Length > 0)
                logger.LogInformation("Tabelle da sincronizzare: {Tables}\n", string.Join(",\t\n ", tables));
            else
                logger.LogWarning("Nessuna tabella configurata per la sincronizzazione!");

            app.UseSession();
            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
        #endregion
    }
}
