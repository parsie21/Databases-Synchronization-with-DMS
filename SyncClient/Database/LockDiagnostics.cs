using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SyncClient.Database
{
    /// <summary>
    /// Gestisce la diagnostica di lock, deadlock e transazioni bloccanti
    /// </summary>
    public class LockDiagnostics
    {
        private readonly ILogger _logger;

        public LockDiagnostics(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Verifica processi bloccati e bloccanti
        /// </summary>
        public async Task CheckBlockedProcessesAsync(SqlConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandTimeout = 30;
            cmd.CommandText = @"
                SELECT 
                    r.session_id AS blocked_session_id,
                    r.blocking_session_id,
                    r.wait_type,
                    r.wait_time,
                    r.command,
                    r.wait_resource,
                    s.program_name,
                    s.host_name,
                    s.login_name,
                    DB_NAME(r.database_id) as database_name
                FROM sys.dm_exec_requests r
                INNER JOIN sys.dm_exec_sessions s ON r.session_id = s.session_id
                WHERE r.blocking_session_id > 0
                   OR r.session_id IN (
                       SELECT DISTINCT blocking_session_id 
                       FROM sys.dm_exec_requests 
                       WHERE blocking_session_id > 0
                   )
                ORDER BY r.blocking_session_id, r.session_id";

            using var reader = await cmd.ExecuteReaderAsync();
            bool hasBlocks = false;

            while (await reader.ReadAsync())
            {
                hasBlocks = true;

                var blockedSessionId = (int)reader["blocked_session_id"];
                var blockingSessionId = reader["blocking_session_id"] != DBNull.Value
                    ? (int)reader["blocking_session_id"]
                    : 0;
                var waitType = reader["wait_type"]?.ToString() ?? "NULL";
                var waitTime = reader["wait_time"] != DBNull.Value ? (int)reader["wait_time"] : 0;
                var command = reader["command"]?.ToString() ?? "NULL";
                var waitResource = reader["wait_resource"]?.ToString() ?? "NULL";
                var programName = reader["program_name"]?.ToString() ?? "NULL";
                var hostName = reader["host_name"]?.ToString() ?? "NULL";
                var loginName = reader["login_name"]?.ToString() ?? "NULL";
                var databaseName = reader["database_name"]?.ToString() ?? "NULL";

                if (blockingSessionId > 0)
                {
                    _logger.LogWarning("BLOCKED PROCESS: Session {BlockedSession} blocked by {BlockingSession} | " +
                                     "WaitType: {WaitType} | WaitTime: {WaitTime}ms | Command: {Command} | " +
                                     "Resource: {Resource} | Program: {Program} | Host: {Host} | " +
                                     "Login: {Login} | DB: {Database}",
                        blockedSessionId, blockingSessionId, waitType, waitTime, command,
                        waitResource, programName, hostName, loginName, databaseName);
                }
                else
                {
                    _logger.LogWarning("BLOCKING PROCESS: Session {SessionId} is blocking others | " +
                                     "Program: {Program} | Host: {Host} | Login: {Login} | DB: {Database}",
                        blockedSessionId, programName, hostName, loginName, databaseName);
                }
            }

            if (!hasBlocks)
            {
                _logger.LogInformation("No blocked processes detected");
            }
            else
            {
                _logger.LogWarning("BLOCKING DETECTED: Consider investigating these blocked processes");
            }
        }

        /// <summary>
        /// Esegue analisi completa dei lock
        /// </summary>
        public async Task PerformLockAnalysisAsync(SqlConnection connection)
        {
            _logger.LogInformation("=== ADVANCED LOCK ANALYSIS ===");
            
            await CheckActiveLocks(connection);
            await CheckRecentDeadlocks(connection);
            await CheckLockEscalation(connection);
            await CheckLongRunningTransactions(connection);
            
            _logger.LogInformation("=== END LOCK ANALYSIS ===");
        }

        #region Private Methods

        /// <summary>
        /// Analizza i lock attualmente attivi nel database
        /// </summary>
        private async Task CheckActiveLocks(SqlConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandTimeout = 30;
            cmd.CommandText = @"
                SELECT 
                    tl.resource_type,
                    tl.request_mode,
                    tl.request_status,
                    tl.request_session_id,
                    COALESCE(OBJECT_NAME(p.object_id), 'N/A') as table_name,
                    tl.resource_description,
                    s.program_name,
                    s.host_name,
                    s.login_name,
                    COALESCE(r.wait_time, 0) as wait_time_ms,
                    r.wait_type
                FROM sys.dm_tran_locks tl
                LEFT JOIN sys.partitions p ON tl.resource_associated_entity_id = p.hobt_id
                LEFT JOIN sys.dm_exec_sessions s ON tl.request_session_id = s.session_id
                LEFT JOIN sys.dm_exec_requests r ON tl.request_session_id = r.session_id
                WHERE tl.resource_database_id = DB_ID()
                AND (tl.request_status = 'WAIT' OR tl.resource_type IN ('OBJECT', 'PAGE', 'KEY', 'RID'))
                ORDER BY tl.request_status DESC, wait_time_ms DESC, tl.request_session_id";

            using var reader = await cmd.ExecuteReaderAsync();
            var lockCount = 0;
            var waitingLocks = 0;
            var highPriorityLocks = 0;

            _logger.LogInformation("--- ACTIVE LOCKS ANALYSIS ---");

            while (await reader.ReadAsync())
            {
                lockCount++;
                var resourceType = reader["resource_type"]?.ToString() ?? "UNKNOWN";
                var requestMode = reader["request_mode"]?.ToString() ?? "UNKNOWN";
                var requestStatus = reader["request_status"]?.ToString() ?? "UNKNOWN";
                var sessionId = reader["request_session_id"] != DBNull.Value ? (int)reader["request_session_id"] : -1;
                var tableName = reader["table_name"]?.ToString() ?? "UNKNOWN";
                var resourceDesc = reader["resource_description"]?.ToString() ?? "UNKNOWN";
                var programName = reader["program_name"]?.ToString() ?? "UNKNOWN";
                var waitTimeMs = reader["wait_time_ms"] != DBNull.Value ? (int)reader["wait_time_ms"] : 0;
                var waitType = reader["wait_type"]?.ToString() ?? "NONE";

                if (requestStatus == "WAIT")
                {
                    waitingLocks++;
                    _logger.LogWarning("WAITING LOCK: Session {SessionId} | Type: {Type} | Mode: {Mode} | Table: {Table} | " +
                                     "WaitTime: {WaitTime}ms | WaitType: {WaitType} | Program: {Program}",
                        sessionId, resourceType, requestMode, tableName, waitTimeMs, waitType, programName);

                    if (waitTimeMs > 30000) // > 30 secondi
                    {
                        _logger.LogError("CRITICAL WAIT: Session {SessionId} waiting {WaitTime}ms for {Type} lock on {Table}",
                            sessionId, waitTimeMs, resourceType, tableName);
                    }
                }
                else if (resourceType == "OBJECT" && (requestMode == "X" || requestMode == "IX"))
                {
                    highPriorityLocks++;
                    _logger.LogInformation("EXCLUSIVE LOCK: Session {SessionId} | Type: {Type} | Mode: {Mode} | Table: {Table} | Program: {Program}",
                        sessionId, resourceType, requestMode, tableName, programName);
                }
            }

            _logger.LogInformation("Lock Summary: {Total} locks analyzed, {Waiting} waiting, {HighPriority} exclusive",
                lockCount, waitingLocks, highPriorityLocks);

            if (waitingLocks > 5)
            {
                _logger.LogWarning("HIGH WAITING LOCKS DETECTED: {Count} locks waiting - potential performance impact", waitingLocks);
            }

            if (highPriorityLocks > 10)
            {
                _logger.LogWarning("HIGH EXCLUSIVE LOCKS: {Count} exclusive locks - potential contention", highPriorityLocks);
            }
        }

        /// <summary>
        /// Verifica deadlock recenti utilizzando system_health Extended Events
        /// </summary>
        /// <summary>
        /// Verifica deadlock recenti utilizzando system_health Extended Events
        /// </summary>
        private async Task CheckRecentDeadlocks(SqlConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandTimeout = 30;
            cmd.CommandText = @"
                WITH DeadlockEvents AS (
                    SELECT 
                        -- il nodo corrente di .nodes(...) è <event>, quindi @timestamp funziona senza 'event/'
                        ev.value('(@timestamp)[1]', 'datetime2') AS deadlock_time,
                        -- prendi direttamente il nodo <deadlock> come XML (più utile del nvarchar)
                        ev.query('(data[@name=""xml_report""]/value/*)[1]') AS deadlock_report
                    FROM (
                        SELECT CAST(st.target_data AS XML) AS target_xml
                        FROM sys.dm_xe_session_targets AS st
                        JOIN sys.dm_xe_sessions AS s
                          ON s.address = st.event_session_address
                        WHERE s.name = 'system_health'
                          AND st.target_name = 'ring_buffer'
                    ) AS data
                    CROSS APPLY data.target_xml.nodes('RingBufferTarget/event[@name=""xml_deadlock_report""]') AS XEventData(ev)
                    -- i timestamp Extended Events sono in UTC
                    WHERE ev.value('(@timestamp)[1]', 'datetime2') > DATEADD(hour, -2, SYSUTCDATETIME())
                )
                SELECT TOP (10) *
                FROM DeadlockEvents
                ORDER BY deadlock_time DESC;
                ";

            try
            {
                using var reader = await cmd.ExecuteReaderAsync();
                var deadlockCount = 0;

                _logger.LogInformation("--- RECENT DEADLOCKS (Last 2 Hours) ---");

                while (await reader.ReadAsync())
                {
                    deadlockCount++;
                    var deadlockTime = reader["deadlock_time"] != DBNull.Value ? (DateTime)reader["deadlock_time"] : DateTime.MinValue;
                    var deadlockReport = reader["deadlock_report"]?.ToString() ?? "NO REPORT";

                    _logger.LogWarning("DEADLOCK #{Count} detected at {Time}", deadlockCount, deadlockTime);

                    // Analisi semplificata del report XML
                    if (deadlockReport.Contains("Change Tracking") || deadlockReport.Contains("scope_info") || deadlockReport.Contains("_tracking"))
                    {
                        _logger.LogError("SYNC-RELATED DEADLOCK: Deadlock involves sync infrastructure tables");
                    }

                    if (deadlockReport.Contains("AdventureWorks"))
                    {
                        _logger.LogWarning("ADVENTUREWORKS DEADLOCK: Deadlock involves AdventureWorks tables");
                    }

                    // Verifica se coinvolge tabelle DMS specifiche
                    if (deadlockReport.Contains("SqlSyncChangeTrackingProvider") || deadlockReport.Contains("dotmim"))
                    {
                        _logger.LogError("DMS SYNC DEADLOCK: Deadlock involves DMS synchronization components");
                    }

                }

                if (deadlockCount == 0)
                {
                    _logger.LogInformation("No deadlocks detected in the last 2 hours");
                }
                else
                {
                    _logger.LogError("DEADLOCK ALERT: {Count} deadlocks detected in the last 2 hours - CRITICAL ISSUE", deadlockCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not check deadlocks: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Verifica situazioni di escalation dei lock
        /// </summary>
        private async Task CheckLockEscalation(SqlConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandTimeout = 30;
            cmd.CommandText = @"
                SELECT 
                OBJECT_NAME(t.object_id) AS table_name,
                t.lock_escalation_desc,
                (
                    SELECT COUNT(*) 
                    FROM sys.dm_tran_locks tl 
                    WHERE tl.resource_associated_entity_id = p.hobt_id
                      AND tl.resource_database_id = DB_ID()
                ) AS current_locks
                FROM sys.partitions AS p
                JOIN sys.tables AS t 
                  ON p.object_id = t.object_id
                WHERE p.index_id IN (0, 1)  -- Heap o clustered index
                  AND EXISTS (
                        SELECT 1 
                        FROM sys.dm_tran_locks tl 
                        WHERE tl.resource_associated_entity_id = p.hobt_id
                    )
                ORDER BY current_locks DESC;
                ";

            using var reader = await cmd.ExecuteReaderAsync();
            
            _logger.LogInformation("--- LOCK ESCALATION ANALYSIS ---");

            var tablesChecked = 0;
            while (await reader.ReadAsync())
            {
                tablesChecked++;
                var tableName = reader["table_name"]?.ToString() ?? "UNKNOWN";
                var escalationDesc = reader["lock_escalation_desc"]?.ToString() ?? "UNKNOWN";
                var currentLocks = reader["current_locks"] != DBNull.Value ? (int)reader["current_locks"] : 0;

                if (currentLocks > 5000) // SQL Server default escalation threshold
                {
                    _logger.LogError("LOCK ESCALATION IMMINENT: Table {Table} has {Locks} locks (Threshold: ~5000, Escalation: {Escalation})",
                        tableName, currentLocks, escalationDesc);
                }
                else if (currentLocks > 1000)
                {
                    _logger.LogWarning("HIGH LOCK COUNT: Table {Table} has {Locks} locks (Escalation: {Escalation})",
                        tableName, currentLocks, escalationDesc);
                }
                else if (currentLocks > 100)
                {
                    _logger.LogInformation("MODERATE LOCKS: Table {Table} has {Locks} locks (Escalation: {Escalation})",
                        tableName, currentLocks, escalationDesc);
                }

                // Identifica tabelle critiche per la sincronizzazione
                if (tableName.Contains("scope_info") || tableName.Contains("_tracking") || tableName.EndsWith("_CT"))
                {
                    _logger.LogWarning("SYNC TABLE LOCK ACTIVITY: {Table} has {Locks} locks - monitor sync performance",
                        tableName, currentLocks);
                }
            }

            if (tablesChecked == 0)
            {
                _logger.LogInformation("No tables with significant lock activity detected");
            }
            else
            {
                _logger.LogInformation("Analyzed {Count} tables with active locks", tablesChecked);
            }
        }

        /// <summary>
        /// Identifica transazioni che durano troppo a lungo
        /// </summary>
        private async Task CheckLongRunningTransactions(SqlConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandTimeout = 30;
            cmd.CommandText = @"
                SELECT 
                    s.session_id,
                    s.status,
                    s.program_name,
                    s.host_name,
                    s.login_name,
                    t.transaction_begin_time,
                    DATEDIFF(second, t.transaction_begin_time, GETDATE()) as duration_seconds,
                    t.transaction_type,
                    t.transaction_state,
                    (SELECT COUNT(*) FROM sys.dm_tran_locks WHERE request_session_id = s.session_id) as lock_count,
                    r.command,
                    r.wait_type,
                    r.wait_time as current_wait_time_ms,
                    r.cpu_time,
                    r.logical_reads,
                    r.writes
                FROM sys.dm_exec_sessions s
                JOIN sys.dm_tran_session_transactions st ON s.session_id = st.session_id
                JOIN sys.dm_tran_active_transactions t ON st.transaction_id = t.transaction_id
                LEFT JOIN sys.dm_exec_requests r ON s.session_id = r.session_id
                WHERE s.database_id = DB_ID()
                AND DATEDIFF(second, t.transaction_begin_time, GETDATE()) > 15  -- Transazioni > 15 secondi
                ORDER BY duration_seconds DESC";

            using var reader = await cmd.ExecuteReaderAsync();
            var longTransactionCount = 0;

            _logger.LogInformation("--- LONG RUNNING TRANSACTIONS ---");

            while (await reader.ReadAsync())
            {
                longTransactionCount++;
                var sessionId = (int)reader["session_id"];
                var status = reader["status"]?.ToString() ?? "UNKNOWN";
                var programName = reader["program_name"]?.ToString() ?? "UNKNOWN";
                var hostName = reader["host_name"]?.ToString() ?? "UNKNOWN";
                var beginTime = reader["transaction_begin_time"] != DBNull.Value ? (DateTime)reader["transaction_begin_time"] : DateTime.MinValue;
                var durationSeconds = reader["duration_seconds"] != DBNull.Value ? (int)reader["duration_seconds"] : 0;
                var lockCount = reader["lock_count"] != DBNull.Value ? (int)reader["lock_count"] : 0;
                var command = reader["command"]?.ToString() ?? "NONE";
                var waitType = reader["wait_type"]?.ToString() ?? "NONE";
                var currentWaitMs = reader["current_wait_time_ms"] != DBNull.Value ? (int)reader["current_wait_time_ms"] : 0;
                var cpuTime = reader["cpu_time"] != DBNull.Value ? (int)reader["cpu_time"] : 0;
                var logicalReads = reader["logical_reads"] != DBNull.Value ? Convert.ToInt64(reader["logical_reads"]) : 0;
                var writes = reader["writes"] != DBNull.Value ? (int)reader["writes"] : 0;

                if (durationSeconds > 300) // 5 minuti
                {
                    _logger.LogError("CRITICAL LONG TRANSACTION: Session {SessionId} | Duration: {Duration}s | Locks: {Locks} | " +
                                   "Status: {Status} | Command: {Command} | WaitType: {WaitType} | CPU: {CPU}ms | " +
                                   "Reads: {Reads} | Writes: {Writes} | Program: {Program}",
                        sessionId, durationSeconds, lockCount, status, command, waitType, cpuTime, logicalReads, writes, programName);
                }
                else if (durationSeconds > 60) // 1 minuto
                {
                    _logger.LogWarning("LONG TRANSACTION: Session {SessionId} | Duration: {Duration}s | Locks: {Locks} | " +
                                     "Status: {Status} | Command: {Command} | CPU: {CPU}ms | Program: {Program}",
                        sessionId, durationSeconds, lockCount, status, command, cpuTime, programName);
                }
                else
                {
                    _logger.LogInformation("MODERATE TRANSACTION: Session {SessionId} | Duration: {Duration}s | Locks: {Locks} | Program: {Program}",
                        sessionId, durationSeconds, lockCount, programName);
                }

                // Identifica transazioni sync specifiche
                if (programName.Contains(".Net SqlClient") && (command.Contains("SELECT") || command.Contains("INSERT") || command.Contains("UPDATE")))
                {
                    _logger.LogInformation("SYNC TRANSACTION DETECTED: Session {SessionId} | Duration: {Duration}s | Command: {Command}",
                        sessionId, durationSeconds, command);
                }

                // Identifica transazioni problematiche
                if (lockCount > 100 && durationSeconds > 30)
                {
                    _logger.LogWarning("HIGH LOCK TRANSACTION: Session {SessionId} holding {Locks} locks for {Duration}s",
                        sessionId, lockCount, durationSeconds);
                }

                // Identifica transazioni con alto I/O
                if (logicalReads > 1000000 || writes > 10000) // 1M reads o 10K writes
                {
                    _logger.LogWarning("HIGH I/O TRANSACTION: Session {SessionId} | Reads: {Reads} | Writes: {Writes} | Duration: {Duration}s",
                        sessionId, logicalReads, writes, durationSeconds);
                }
            }

            if (longTransactionCount == 0)
            {
                _logger.LogInformation("No long-running transactions detected");
            }
            else
            {
                var severity = longTransactionCount > 5 ? LogLevel.Error : LogLevel.Warning;
                _logger.Log(severity, "LONG TRANSACTION ALERT: {Count} long-running transactions detected", longTransactionCount);
            }
        }

        #endregion
    }
}
