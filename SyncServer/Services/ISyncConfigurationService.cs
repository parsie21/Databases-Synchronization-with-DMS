namespace SyncServer.Services;

public interface ISyncConfigurationService
{
    Task ProvisionAllDatabasesAsync();
    void RegisterSyncServices(IServiceCollection services);
}
