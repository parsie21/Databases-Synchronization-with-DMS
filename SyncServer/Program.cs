using Dotmim.Sync;
using Dotmim.Sync.SqlServer;
using Dotmim.Sync.SqlServer.ChangeTracking;
using Dotmim.Sync.Web.Server;
using SyncServer;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;


public class Program
{


    /// <summary>
    /// Punto di ingresso dell'applicazione. Avvia il web server ASP.NET Core usando la classe Startup.
    /// </summary>
    /// <param name="args">Argomenti della riga di comando.</param>
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    /// <summary>
    /// Crea e configura l'host web ASP.NET Core, specificando la classe Startup.
    /// </summary>
    /// <param name="args">Argomenti della riga di comando.</param>
    /// <returns>IHostBuilder configurato.</returns>
    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
        .ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddSimpleConsole(options =>
            {
                options.IncludeScopes = false;
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:SS ";
            });
        })
        .ConfigureWebHostDefaults(webBuilder =>
        {
            webBuilder.UseStartup<SyncServer.Startup>();
            
            // Controlla l'ambiente
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
            var aspnetCoreUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
            
            // Usa URL hardcoded solo se NON siamo in Development O se non è impostata ASPNETCORE_URLS
            if (environment != "Development" || string.IsNullOrEmpty(aspnetCoreUrls))
            {
                webBuilder.UseUrls("http://localhost:5202"); // URL di riferimento dell'API
            }
            // Se siamo in Development e ASPNETCORE_URLS è impostata (Docker), usa quella
        });
}
