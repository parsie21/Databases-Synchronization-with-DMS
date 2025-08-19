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

        /// <summary>
        /// Oggetto di configurazione che fornisce accesso alle impostazioni dell'applicazione,
        /// come stringhe di connessione e parametri definiti in appsettings.json.
        /// </summary>
        public IConfiguration Configuration { get; }

        /// <summary>
        /// Costruttore della classe Startup.
        /// Inizializza la configurazione dell'applicazione.
        /// </summary>
        /// <param name="configuration">Configurazione fornita dal sistema di hosting ASP.NET Core.</param>
        public Startup(IConfiguration configuration)
        {
            this.Configuration = configuration;
        }

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


            var tables = new string[] { "SalesLT.ProductCategory", "SalesLT.Product", "SalesLT.Address", "SalesLT.Customer", "SalesLT.CustomerAddress" };
            var setup = new SyncSetup(tables);
            var options = new SyncOptions { BatchSize = 2000, DbCommandTimeout = 300 };
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
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
                app.UseDeveloperExceptionPage();

            app.UseSession();
            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapGet("/", context =>
                {
                    context.Response.ContentType = "text/plain";
                    return context.Response.WriteAsync("Dotmim.Sync Server up");
                });
            });
        }


    }
}
