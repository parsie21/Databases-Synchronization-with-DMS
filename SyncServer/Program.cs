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
        .ConfigureWebHostDefaults(webBuilder =>
        {
            webBuilder.UseStartup<SyncServer.Startup>();
        });



}































/*
 public static void Main(string[] args)
    {


        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddSession();
        

        //Define the sync scope 
        var setup = new SyncSetup(new string[] { "SalesLT.ProductCategory", "SalesLT.Product",
                                        "SalesLT.Address", "SalesLT.Customer", "SalesLT.CustomerAddress"});
        var options = new SyncOptions()
        {
            BatchSize = 2000
        };
        var provider = new SqlSyncChangeTrackingProvider(builder.Configuration.GetConnectionString("ServerDb"));
        builder.Services.AddSyncServer(provider, setup, options);

        var app = builder.Build();

        app.UseSession();
        // Health
        app.MapGet("/", () => Results.Ok("Dotmim.Sync Server up"));

        // Sync endpoint
        app.MapPost("/api/sync", async (HttpContext ctx, WebServerAgent agent) =>
        {
            await agent.HandleRequestAsync(ctx);
        });

        app.Run();

    }
 */