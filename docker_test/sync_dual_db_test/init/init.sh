#!/usr/bin/env bash
set -e 
echo "SA_PASSWORD=$SA_PASSWORD"
# List of SQL Server instances (server + 3 clients)
servers=("central-db-TDS" "client1-db-TDS" "client2-db-TDS" "client3-db-TDS")

# Wait for each instance to be ready
for s in "${servers[@]}"; do
    echo "Waiting for $s to be ready..."
    until /opt/mssql-tools/bin/sqlcmd -S "$s" -U sa -P "$SA_PASSWORD" -Q "SELECT 1" >/dev/null 2>&1; do
        sleep 2
    done
done

# Restore both databases on each instance
for s in "${servers[@]}"; do
    echo "Restoring TERRADISIENA on $s..."
    /opt/mssql-tools/bin/sqlcmd -S "$s" -U sa -P "$SA_PASSWORD" -i /init/restore-db-TerraDiSiena.sql
    echo "Restoring ZEUSCFG_TERRADISIENA on $s..."
    /opt/mssql-tools/bin/sqlcmd -S "$s" -U sa -P "$SA_PASSWORD" -i /init/restore-db-zeuscfg_TerraDiSiena.sql
    echo "Databases restored on $s."
done

# Wait for both databases to be online on each instance
for s in "${servers[@]}"; do
    for db in TERRADISIENA ZEUSCFG_TERRADISIENA; do
        echo "Waiting for database $db to be online on $s..."
        until /opt/mssql-tools/bin/sqlcmd -S "$s" -U sa -P "$SA_PASSWORD" -d $db -Q "SELECT 1" >/dev/null 2>&1; do
            sleep 2
        done
        echo "Database $db is online on $s."
    done
done

echo "All databases have been restored successfully and are online."