using Dotmim.Sync;
using Dotmim.Sync.SqlServer;
using Dotmim.Sync.Web.Server;
using SyncServer.Configurations;
using SyncServer.Services;

var builder = WebApplication.CreateBuilder(args);

// Configurazione logging
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = false;
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});

// Bind configuration
builder.Services.Configure<SyncConfiguration>(builder.Configuration.GetSection("SyncConfiguration"));

// Registra servizi base
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession();
builder.Services.AddControllers();

// Registra il servizio di configurazione sync
builder.Services.AddSingleton<ISyncConfigurationService, SyncConfigurationService>();

// ===== REGISTRAZIONE SERVIZI SYNC =====
using (var tempServiceProvider = builder.Services.BuildServiceProvider())
{
    var syncConfigService = tempServiceProvider.GetRequiredService<ISyncConfigurationService>();
    var logger = tempServiceProvider.GetRequiredService<ILogger<Program>>();
    
    logger.LogInformation("Registrazione servizi sync...");
    syncConfigService.RegisterSyncServices(builder.Services);
    logger.LogInformation("Servizi sync registrati con successo");
}

var app = builder.Build();

// ===== PROVISIONING ASINCRONO =====
using (var scope = app.Services.CreateScope())
{
    var syncConfigService = scope.ServiceProvider.GetRequiredService<ISyncConfigurationService>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    try
    {
        logger.LogInformation("Avvio provisioning databases...");
        await syncConfigService.ProvisionAllDatabasesAsync();
        logger.LogInformation("Provisioning completato con successo");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Provisioning fallito: {Message}", ex.Message);
        throw;
    }
}

// ===== CONFIGURAZIONE PIPELINE =====
if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();

app.UseSession();
app.UseRouting();
app.MapControllers();

// ===== CONFIGURAZIONE URL =====
var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
var aspnetCoreUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");

if (environment != "Development" || string.IsNullOrEmpty(aspnetCoreUrls))
{
    // app.Urls.Add("http://localhost:5202");
    app.Urls.Add("http://0.0.0.0:5202"); 
}



// Log finale
var finalLogger = app.Services.GetRequiredService<ILogger<Program>>();
finalLogger.LogInformation("Applicazione avviata correttamente su ambiente: {Environment}", environment);

app.Run();
