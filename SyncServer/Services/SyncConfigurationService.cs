using Dotmim.Sync;
using Dotmim.Sync.SqlServer;
using Dotmim.Sync.Web.Server;
using Microsoft.Extensions.Options;
using SyncServer.Configurations;
using Microsoft.Data.SqlClient;  

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
        setupDb2.Tables.Add("[ven_Cassa]").Columns.AddRange(new string[] { "IDCassa",
            "Numero punto",
            "Nome punto",
            "Data",
            "IDCassetto",
            "Importo",
            "Importo resi",
            "Importo detraibile",
            "Tipo riga",
            "Tipo operazione",
            "Codice causale",
            "Descrizione operazione",
            "Note su operazione",
            "Utente",
            "Numero cassa",
            "Numero cassa trasferimento",
            "IDTessera",
            "Codice cliente",
            "Numero scontrino",
            //"Identificativo S.F.",
            "Numero righe scontrino",
            "Tipo soggetto",
            "Codice soggetto",
            "Documento di riferimento - tipo",
            "Documento di riferimento - numero",
            "Documento di riferimento - data",
            "Documento di riferimento - annotazioni",
            "Mezzo di pagamento",
            "Codice iva 1",
            "Codice iva 2",
            "Codice iva 3",
            "Codice iva 4",
            "Codice iva 5",
            "Codice iva 6",
            "Codice iva 7",
            "Codice iva 8",
            "Codice iva 9",
            "Codice iva 10",
            "Importo1",
            "Importo2",
            "Importo3",
            "Importo4",
            "Importo5",
            "Importo6",
            "Importo7",
            "Importo8",
            "Importo9",
            "Importo10",
            "Importo sconto buoni",
            "Importo sconto promozione a buoni",
            "IDPromozioneBuoni associati",
            "Forma di pagamento - contanti",
            "Forma di pagamento - bancomat",
            "Forma di pagamento - carte di credito",
            "Forma di pagamento - assegno",
            "Forma di pagamento - ticket",
            "Forma di pagamento - buoni",
            "Forma di pagamento - credito",
            "Forma di pagamento - credito promozione a buoni",
            "Forma di pagamento - altro",
            "Forma di pagamento - anticipato",
            "Forma di pagamento - buono celiachia",
            "Forma di pagamento - seguirà fattura",
            "Forma di pagamento - aggiuntiva 1",
            "Forma di pagamento - aggiuntiva 2",
            "Forma di pagamento - aggiuntiva 3",
            "Forma di pagamento - aggiuntiva 4",
            "Forma di pagamento - aggiuntiva 5",
            "Forma di pagamento - aggiuntiva 6",
            "Forma di pagamento - aggiuntiva 7",
            "Forma di pagamento - aggiuntiva 8",
            "Forma di pagamento - aggiuntiva 9",
            "Forma di pagamento - aggiuntiva 10",
            "Forma di pagamento - aggiuntiva 11",
            "Forma di pagamento - aggiuntiva 12",
            "Forma di pagamento - aggiuntiva 13",
            "Forma di pagamento - aggiuntiva 14",
            "Forma di pagamento - aggiuntiva 15",
            "Importo sconti cassa",
            "Importo sconti promozioni",
            "Vendita in emergenza",
            "Fatturato",
            "IDCDC",
            "Tipo scontrino",
            "Codice lotteria",
            "Non riscosso servizi",
            "DCR a SSN",
            "Tipo documento commerciale",
            "Pan",
            "PanCeliachia",
            "Stato",
            "Data aggiornamento",
            "Utente aggiornamento"});
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

            _logger.LogInformation("{DatabaseName}: Iniziando pulizia manuale completa...", databaseName);
            
            // PULIZIA MANUALE AGGRESSIVA - Rimuove tutte le strutture DMS incompatibili
            await PerformManualCleanupAsync(connectionString, databaseName);
            
            // PROVISIONING COMPLETO con ambiente pulito
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

    private async Task PerformManualCleanupAsync(string connectionString, string databaseName)
    {
        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            _logger.LogInformation("{DatabaseName}: Eseguendo pulizia aggressiva a chunks...", databaseName);

            // STRATEGIA: Pulizia in chunks per evitare limite STRING_AGG
            await PerformChunkedCleanupAsync(connection, databaseName);

            _logger.LogInformation("{DatabaseName}: Pulizia aggressiva completata", databaseName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{DatabaseName}: Errore durante pulizia aggressiva: {Error}", databaseName, ex.Message);
            throw;
        }
    }

    private async Task PerformChunkedCleanupAsync(SqlConnection connection, string databaseName)
    {
        // Step 1: Rimuovi TUTTE le stored procedures DMS in chunks
        await RemoveStoredProceduresInChunksAsync(connection, databaseName);

        // Step 2: Rimuovi TUTTI i tipi DMS in chunks
        await RemoveTypesInChunksAsync(connection, databaseName);

        // Step 3: Rimuovi triggers, tabelle tracking e scope
        await RemoveRemainingObjectsAsync(connection, databaseName);
    }

    private async Task RemoveStoredProceduresInChunksAsync(SqlConnection connection, string databaseName)
    {
        try
        {
            _logger.LogInformation("{DatabaseName}: Rimozione stored procedures DMS in chunks...", databaseName);

            var getProceduresQuery = @"
                SELECT QUOTENAME(SCHEMA_NAME(schema_id)) + '.' + QUOTENAME(name) as FullName
                FROM sys.procedures 
                WHERE name LIKE '%_selectchanges%' 
                   OR name LIKE '%_insertmetadata%' 
                   OR name LIKE '%_updatemetadata%'
                   OR name LIKE '%_deletemetadata%'
                   OR name LIKE '%_reset%'
                   OR name LIKE '%_bulkinsert%'
                   OR name LIKE '%_bulkupdate%'
                   OR name LIKE '%_bulkdelete%'
                   OR name LIKE '%bulkinsert%'
                   OR name LIKE '%bulkupdate%'
                   OR name LIKE '%bulkdelete%'
                ORDER BY name";

            var procedures = new List<string>();
            using var cmd = new SqlCommand(getProceduresQuery, connection);
            using var reader = await cmd.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
                procedures.Add(reader["FullName"].ToString());
            }

            _logger.LogInformation("{DatabaseName}: Trovate {Count} stored procedures DMS da rimuovere", databaseName, procedures.Count);

            // Rimuovi in chunks di 10 per volta
            const int chunkSize = 10;
            for (int i = 0; i < procedures.Count; i += chunkSize)
            {
                var chunk = procedures.Skip(i).Take(chunkSize);
                var dropSql = string.Join(";\n", chunk.Select(proc => $"DROP PROCEDURE {proc}"));
                
                if (!string.IsNullOrEmpty(dropSql))
                {
                    try
                    {
                        using var dropCmd = new SqlCommand(dropSql, connection);
                        dropCmd.CommandTimeout = 60;
                        await dropCmd.ExecuteNonQueryAsync();
                        
                        _logger.LogInformation("{DatabaseName}: Rimosso chunk {ChunkStart}-{ChunkEnd} di stored procedures", 
                            databaseName, i + 1, Math.Min(i + chunkSize, procedures.Count));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "{DatabaseName}: Errore rimozione chunk stored procedures: {Error}", databaseName, ex.Message);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{DatabaseName}: Errore durante rimozione stored procedures: {Error}", databaseName, ex.Message);
        }
    }

    private async Task RemoveTypesInChunksAsync(SqlConnection connection, string databaseName)
    {
        try
        {
            _logger.LogInformation("{DatabaseName}: Rimozione tipi DMS in chunks...", databaseName);

            var getTypesQuery = @"
                SELECT QUOTENAME(SCHEMA_NAME(schema_id)) + '.' + QUOTENAME(name) as FullName
                FROM sys.table_types 
                WHERE name LIKE '%_BulkType%' 
                   OR name LIKE '%BulkTableType%'
                   OR name LIKE '%_bulktype%'
                   OR name LIKE '%BulkType%'
                ORDER BY name";

            var types = new List<string>();
            using var cmd = new SqlCommand(getTypesQuery, connection);
            using var reader = await cmd.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
                types.Add(reader["FullName"].ToString());
            }

            _logger.LogInformation("{DatabaseName}: Trovati {Count} tipi DMS da rimuovere", databaseName, types.Count);

            // Rimuovi i tipi uno per volta per gestire meglio le dipendenze
            foreach (var type in types)
            {
                try
                {
                    var dropSql = $"DROP TYPE {type}";
                    using var dropCmd = new SqlCommand(dropSql, connection);
                    dropCmd.CommandTimeout = 30;
                    await dropCmd.ExecuteNonQueryAsync();
                    
                    _logger.LogDebug("{DatabaseName}: Rimosso tipo {Type}", databaseName, type);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "{DatabaseName}: Errore rimozione tipo {Type}: {Error}", databaseName, type, ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{DatabaseName}: Errore durante rimozione tipi: {Error}", databaseName, ex.Message);
        }
    }

    private async Task RemoveRemainingObjectsAsync(SqlConnection connection, string databaseName)
    {
        try
        {
            _logger.LogInformation("{DatabaseName}: Rimozione oggetti rimanenti...", databaseName);

            var cleanupSteps = new[]
            {
                // Triggers
                @"
                DECLARE @sql NVARCHAR(MAX) = '';
                DECLARE @triggers TABLE (DropSql NVARCHAR(MAX));
                
                INSERT INTO @triggers
                SELECT 'DROP TRIGGER ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + '.' + QUOTENAME(tr.name) + ';'
                FROM sys.triggers tr
                JOIN sys.tables t ON tr.parent_id = t.object_id
                WHERE tr.name LIKE '%_insert_trigger%' 
                   OR tr.name LIKE '%_update_trigger%'
                   OR tr.name LIKE '%_delete_trigger%'
                   OR tr.name LIKE '%insert_trigger%'
                   OR tr.name LIKE '%update_trigger%'
                   OR tr.name LIKE '%delete_trigger%';

                DECLARE trigger_cursor CURSOR FOR SELECT DropSql FROM @triggers;
                DECLARE @triggerSql NVARCHAR(MAX);
                
                OPEN trigger_cursor;
                FETCH NEXT FROM trigger_cursor INTO @triggerSql;
                
                WHILE @@FETCH_STATUS = 0
                BEGIN
                    BEGIN TRY
                        EXEC sp_executesql @triggerSql;
                    END TRY
                    BEGIN CATCH
                        -- Continua anche se un trigger fallisce
                    END CATCH
                    
                    FETCH NEXT FROM trigger_cursor INTO @triggerSql;
                END
                
                CLOSE trigger_cursor;
                DEALLOCATE trigger_cursor;
                ",
                
                // Tracking tables
                @"
                DECLARE @sql NVARCHAR(MAX) = '';
                DECLARE @trackingTables TABLE (DropSql NVARCHAR(MAX));
                
                INSERT INTO @trackingTables
                SELECT 'DROP TABLE ' + QUOTENAME(SCHEMA_NAME(schema_id)) + '.' + QUOTENAME(name) + ';'
                FROM sys.tables 
                WHERE name LIKE '%_tracking';

                DECLARE table_cursor CURSOR FOR SELECT DropSql FROM @trackingTables;
                DECLARE @tableSql NVARCHAR(MAX);
                
                OPEN table_cursor;
                FETCH NEXT FROM table_cursor INTO @tableSql;
                
                WHILE @@FETCH_STATUS = 0
                BEGIN
                    BEGIN TRY
                        EXEC sp_executesql @tableSql;
                    END TRY
                    BEGIN CATCH
                        -- Continua anche se una tabella fallisce
                    END CATCH
                    
                    FETCH NEXT FROM table_cursor INTO @tableSql;
                END
                
                CLOSE table_cursor;
                DEALLOCATE table_cursor;
                ",
                
                // Scope tables
                @"
                IF OBJECT_ID('dbo.scope_info', 'U') IS NOT NULL DROP TABLE [dbo].[scope_info];
                IF OBJECT_ID('dbo.scope_info_client', 'U') IS NOT NULL DROP TABLE [dbo].[scope_info_client];
                "
            };

            foreach (var (step, index) in cleanupSteps.Select((s, i) => (s, i + 1)))
            {
                try
                {
                    _logger.LogInformation("{DatabaseName}: Eseguendo cleanup step {Step}/3", databaseName, index);
                    
                    using var command = new SqlCommand(step, connection);
                    command.CommandTimeout = 120;
                    await command.ExecuteNonQueryAsync();
                    
                    _logger.LogInformation("{DatabaseName}: Cleanup step {Step} completato", databaseName, index);
                }
                catch (Exception stepEx)
                {
                    _logger.LogWarning(stepEx, "{DatabaseName}: Errore cleanup step {Step}: {Error}", databaseName, index, stepEx.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{DatabaseName}: Errore durante cleanup oggetti rimanenti: {Error}", databaseName, ex.Message);
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

