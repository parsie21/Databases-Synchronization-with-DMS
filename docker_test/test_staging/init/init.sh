#!/usr/bin/env bash
set -e 
echo "SA_PASSWORD=$SA_PASSWORD"
# Elenco delle istanze SQL (server + client)
servers=("central-db-TDS" "client1-db-TDS" "client2-db-TDS" "client3-db-TDS")

# aspetta che ogni istanza risponda
for s in "${servers[@]}"; do
    echo "Waiting for $s to be ready..."
    until /opt/mssql-tools/bin/sqlcmd -S "$s" -U sa -P "$SA_PASSWORD" -Q "SELECT 1" >/dev/null 2>&1; do
        sleep 2
    done
done

# Ripristina il database in ogni istanza
for s in "${servers[@]}"; do
    echo "Restoring database on $s..."
    /opt/mssql-tools/bin/sqlcmd -S "$s" -U sa -P "$SA_PASSWORD" -i /init/restore-db-TerraDiSiena.sql
    echo "Database restored on $s."
done

echo "All databases restored successfully."