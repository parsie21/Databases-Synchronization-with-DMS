namespace SyncServer.Configurations;

public class SyncOptionsConfiguration
{
    public int BatchSize { get; set; }
    public int DbCommandTimeout { get; set; }
    public required string ConflictResolutionPolicy { get; set; }
}
