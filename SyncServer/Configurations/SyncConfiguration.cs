namespace SyncServer.Configurations
{
    public class SyncConfiguration
    {
        /// <summary>
        /// Connection string for the primary database.
        /// </summary>
        public required string PrimaryDatabaseConnectionString { get; set; }

        /// <summary>
        /// Connection string for the secondary database.
        /// </summary>
        public required string SecondaryDatabaseConnectionString { get; set; }

        /// <summary>
        /// Synchronization options (e.g., batch size, timeout, conflict resolution policy).
        /// </summary>
        public required SyncOptionsConfiguration SyncOptions { get; set; }

        /// <summary>
        /// Tables to synchronize for each database.
        /// </summary>
        public required Dictionary<string, string[]> DatabaseTables { get; set; }
    }
}
