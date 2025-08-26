namespace SyncServer.Configurations;

public class SyncOptionsConfiguration
{
    public int BatchSize { get; set; }
    public int DbCommandTimeout { get; set; }
    public string ConflictResolutionPolicy { get; set; }
}
