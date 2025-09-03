using Dotmim.Sync;
using Dotmim.Sync.SqlServer;
using Dotmim.Sync.Web.Server;
using Microsoft.Extensions.Options;
using SyncServer.Configurations;

namespace SyncServer.Services;

public class SyncConfigurationService : ISyncConfigurationService
{
    private readonly SyncConfiguration _config;
    private readonly ILogger<SyncConfigurationService> _logger;

    public SyncConfigurationService(IOptions<SyncConfiguration> config, ILogger<SyncConfigurationService> logger)
    {
        _config = config.Value;
        _logger = logger;
        ValidateConfiguration();
    }

    public async Task ProvisionAllDatabasesAsync()
    {
        var connectionStringDb1 = GetConnectionString("PrimaryDatabaseConnectionString", _config.PrimaryDatabaseConnectionString);
        var connectionStringDb2 = GetConnectionString("SecondaryDatabaseConnectionString", _config.SecondaryDatabaseConnectionString);

        // Provisioning sequenziale per evitare race conditions
        await ProvisionDatabaseAsync(
            connectionStringDb1,
            _config.DatabaseTables["PrimaryDatabase"],
            "PrimaryDatabaseScope",
            "Primary Database");

        await ProvisionDatabaseAsync(
            connectionStringDb2,
            _config.DatabaseTables["SecondaryDatabase"],
            "SecondaryDatabaseScope",
            "Secondary Database");
    }

    public void RegisterSyncServices(IServiceCollection services)
    {
        var syncOptions = CreateSyncOptions();

        var connectionStringDb1 = GetConnectionString("PrimaryDatabaseConnectionString", _config.PrimaryDatabaseConnectionString);
        var providerDb1 = new SqlSyncChangeTrackingProvider(connectionStringDb1);
        var setupDb1 = new SyncSetup(_config.DatabaseTables["PrimaryDatabase"]);
        // Registrazione servizi sync per Primary Database
        services.AddSyncServer(
            providerDb1,
            setupDb1,
            syncOptions,
            null,
            "PrimaryDatabaseScope");
        
        var connectionStringDb2 = GetConnectionString("SecondaryDatabaseConnectionString", _config.SecondaryDatabaseConnectionString);
        var providerDb2 = new SqlSyncChangeTrackingProvider(connectionStringDb2);
        var setupDb2 = new SyncSetup(_config.DatabaseTables["SecondaryDatabase"]);
        // Registrazione servizi sync per Secondary Database  
        services.AddSyncServer(
            providerDb2,
            setupDb2,
            syncOptions,
            null,
            "SecondaryDatabaseScope");

        _logger.LogInformation("Sync services registered successfully");
    }

    private async Task ProvisionDatabaseAsync(string connectionString, string[] tables, string scopeName, string databaseName)
    {
        try
        {
            var setup = new SyncSetup(tables);
            var provider = new SqlSyncChangeTrackingProvider(connectionString);
            var orchestrator = new RemoteOrchestrator(provider);

            // Verifica se già provisionato usando GetScopeInfoAsync
            ScopeInfo scopeInfo = null;
            try
            {
                scopeInfo = await orchestrator.GetScopeInfoAsync(scopeName);
            }
            catch
            {
                // Se GetScopeInfoAsync lancia eccezione, significa che lo scope non esiste
                scopeInfo = null;
            }

            if (scopeInfo != null)
            {
                _logger.LogInformation("{DatabaseName}: Scope {ScopeName} già provisionato", databaseName, scopeName);
                
                // Deprovision selettivo (solo stored procedures e scope info per il server)
                var deprovFlags = Dotmim.Sync.Enumerations.SyncProvision.StoredProcedures | Dotmim.Sync.Enumerations.SyncProvision.ScopeInfo;
                await orchestrator.DeprovisionAsync(deprovFlags);
                _logger.LogInformation("{DatabaseName}: Deprovision completato per scope {ScopeName}", databaseName, scopeName);
            }

            // Provisioning completo
            await orchestrator.ProvisionAsync(setup, Dotmim.Sync.Enumerations.SyncProvision.NotSet, overwrite: true);
            _logger.LogInformation("{DatabaseName}: Provisioning completato per scope {ScopeName} con {TableCount} tabelle", 
                databaseName, scopeName, tables.Length);

            // Log delle tabelle configurate
            _logger.LogInformation("{DatabaseName} - Tabelle configurate: {Tables}", 
                databaseName, string.Join(", ", tables));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Errore durante il provisioning di {DatabaseName} (scope: {ScopeName})", databaseName, scopeName);
            throw;
        }
    }

    private string GetConnectionString(string envVarName, string fallbackValue)
    {
        var fromEnv = Environment.GetEnvironmentVariable(envVarName);
        var connectionString = fromEnv ?? fallbackValue;
        
        _logger.LogInformation("Connection string per {EnvVar} da: {Source}", 
            envVarName, 
            fromEnv != null ? "Environment Variable" : "Configuration File");
            
        return connectionString;
    }

    private SyncOptions CreateSyncOptions()
    {
        return new SyncOptions
        {
            BatchSize = _config.SyncOptions.BatchSize,
            DbCommandTimeout = _config.SyncOptions.DbCommandTimeout,
            ConflictResolutionPolicy = _config.SyncOptions.ConflictResolutionPolicy == "ServerWins"
                ? Dotmim.Sync.Enumerations.ConflictResolutionPolicy.ServerWins
                : Dotmim.Sync.Enumerations.ConflictResolutionPolicy.ClientWins,
            DisableConstraintsOnApplyChanges = true
        };
    }

    private void ValidateConfiguration()
    {
        // Validazione connection strings
        if (string.IsNullOrWhiteSpace(_config.PrimaryDatabaseConnectionString))
            throw new InvalidOperationException("PrimaryDatabaseConnectionString non configurata o vuota.");

        if (string.IsNullOrWhiteSpace(_config.SecondaryDatabaseConnectionString))
            throw new InvalidOperationException("SecondaryDatabaseConnectionString non configurata o vuota.");

        // Validazione tabelle
        if (!_config.DatabaseTables.TryGetValue("PrimaryDatabase", out var primaryTables) || primaryTables.Length == 0)
            throw new InvalidOperationException("Nessuna tabella configurata per il Primary Database.");

        if (!_config.DatabaseTables.TryGetValue("SecondaryDatabase", out var secondaryTables) || secondaryTables.Length == 0)
            throw new InvalidOperationException("Nessuna tabella configurata per il Secondary Database.");

        // Validazione sync options
        if (_config.SyncOptions.BatchSize <= 0)
            throw new InvalidOperationException("BatchSize deve essere maggiore di 0.");

        if (_config.SyncOptions.DbCommandTimeout <= 0)
            throw new InvalidOperationException("DbCommandTimeout deve essere maggiore di 0.");

        var validPolicies = new[] { "ClientWins", "ServerWins" };
        if (string.IsNullOrWhiteSpace(_config.SyncOptions.ConflictResolutionPolicy) ||
            !validPolicies.Contains(_config.SyncOptions.ConflictResolutionPolicy))
        {
            throw new InvalidOperationException("ConflictResolutionPolicy deve essere 'ClientWins' o 'ServerWins'.");
        }

        _logger.LogInformation("Configurazione validata con successo");
    }
}

