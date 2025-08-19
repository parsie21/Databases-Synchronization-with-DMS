-- restore-dq.sql for AdeventureWorks

RESTORE DATABASE [AdventureWorks]
FROM DISK = "/var/opt/mssql/backup/AdventureWorks.bak"
WITH MOVE 'AdventureWorksLT2022_Data' TO '/var/opt/mssql/AdventureWorks.mdf',
     MOVE 'AdventureWorksLT2022_Log' TO '/var/opt/mssql/AdventureWorks_log.ldf',
     REPLACE,
     STATS = 5; -- stats means show progress every 5 percent
GO


/* ===========================================================
   Enable Change Tracking (DB + tables) after restore
   - Idempotent: safe if CT already ON
   - Enables CT on all user tables that have a PRIMARY KEY
   =========================================================== */

-- Assicurati che il DB sia online prima di proseguire
-- (opzionale) ALTER DATABASE [AdventureWorks] SET READ_WRITE WITH ROLLBACK IMMEDIATE;

-- 1) Abilita CT a livello di database (o riallinea retention/cleanup se già ON)
IF NOT EXISTS (
    SELECT 1
    FROM sys.change_tracking_databases
    WHERE database_id = DB_ID(N'AdventureWorks')
)
BEGIN
    ALTER DATABASE [AdventureWorks]
        SET CHANGE_TRACKING = ON
        (CHANGE_RETENTION = 7 DAYS, AUTO_CLEANUP = ON);
END
ELSE
BEGIN
    -- Se è già ON, riallinea comunque i parametri desiderati
    ALTER DATABASE [AdventureWorks]
        SET CHANGE_TRACKING = ON
        (CHANGE_RETENTION = 7 DAYS, AUTO_CLEANUP = ON);
END
GO

-- 2) Abilita CT su tutte le tabelle utente con PK che non lo hanno già
USE [AdventureWorks];
GO

DECLARE @sql NVARCHAR(MAX) = N'';

;WITH pk_tables AS (
    SELECT
        s.name  AS schema_name,
        t.name  AS table_name,
        t.object_id
    FROM sys.tables t
    JOIN sys.schemas s ON s.schema_id = t.schema_id
    JOIN sys.indexes i ON i.object_id = t.object_id AND i.is_primary_key = 1
    WHERE t.is_ms_shipped = 0
)
SELECT @sql = STRING_AGG(
    N'IF NOT EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N''' +
    QUOTENAME(schema_name) + N'.' + QUOTENAME(table_name) + N''')) ' +
    N'ALTER TABLE ' + QUOTENAME(schema_name) + N'.' + QUOTENAME(table_name) +
    N' ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = ON);'
, CHAR(10))
FROM pk_tables;

IF @sql IS NOT NULL AND LEN(@sql) > 0
    EXEC sp_executesql @sql;
GO

-- (facoltativo) Verifica rapido
-- SELECT DB_NAME(database_id) AS db, * FROM sys.change_tracking_databases WHERE database_id = DB_ID();
-- SELECT s.name AS [schema], t.name AS [table] FROM sys.change_tracking_tables ctt JOIN sys.tables t ON t.object_id=ctt.object_id JOIN sys.schemas s ON s.schema_id=t.schema_id ORDER BY s.name, t.name;
