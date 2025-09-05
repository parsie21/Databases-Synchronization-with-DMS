# DMS Sync Solution

# Overview
A .NET 8 Database syncronization solution using Dotmim.Sync Framework for bidirectional data sync between SQL Server Databases 

**Framework Documentation**:  [Dotmim.Sync Framework](https://dotmimsync.readthedocs.io/) - refer to the official documentation for advanced configuration options and troubleshooting.
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\

# Quick Start
Step-by-step guide to get the solution running locally:
- Prerequisites (SQL Server, .NET 8)
- Building the solution
- Initial database setup
- Running the server
- Running the client

\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
# Architecture 
- **SyncServer**: ASP .NET Core Web API providing sync endpoints
- **SyncClient**: Console application fo client-side syncronization
- **Docker Support**: Container orchestration for testing environments

<p align="center">
  <img src="z_docs/images/SystemExample.png" alt="Architecture" width="450">
</p>


## appsettings.json Structure for the server side 
When configuring connection strings, it is absolutely essential that both the server and client applications use identical database assignments. Both `PrimaryDatabaseConnectionString` must point to the same database (either the CFG database containing tables like `cfg_Aziende`, `cfg_Utenti` or the main database containing tables like `ana_Clienti`, `ana_Fornitori`), and both `SecondaryDatabaseConnectionString` must point to the other database. You can choose either the CFG database as primary and the main database as secondary, or vice versa, but whatever pattern you choose must be applied consistently across all server and client configurations - mixing these assignments will cause synchronization failures.




```json
{
  "SyncConfiguration": {
    "RunProvisionOnStartup": true,
    "PrimaryDatabaseConnectionString": "Server=localhost,14330;Database=ZEUSCFG_TERRADISIENA;User ID=sa;Password=Terya12345!;Encrypt=False;TrustServerCertificate=True;MultipleActiveResultSets=True",
    "SecondaryDatabaseConnectionString": "Server=localhost,14330;Database=TERRADISIENA;User ID=sa;Password=Terya12345!;Encrypt=False;TrustServerCertificate=True;MultipleActiveResultSets=True",
    "SyncOptions": {
      "BatchSize": 800,
      "DbCommandTimeout": 300,
      "ConflictResolutionPolicy": "ClientWins"
    },
    "DatabaseTables": {
      "PrimaryDatabase": [
              "[cfg_Aziende]",
              "[cfg_Utenti]",
              "[...]"
      ],
      "SecondaryDatabase": [
        "[ana_CampiLiberiFornitori]",
        "[ana_Clienti]",
        "[...]"
      ]
    }
  }
}
```
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\

# Installation & Setup

## Prerequisites
- .NET 8 SDK installed on the host (for local runs)
- Docker + Docker Compose (for containerized environment)
- SQL Server 2022 instances (central + clients)
- Database backups (.bak)
## Database Preparation
1) Both the SyncServer and SyncClient rely on Dotmim.Sync’s SqlSyncChangeTrackingProvider, which requires SQL Server Change Tracking to be enabled.
If not enabled, synchronization will fail immediately.

Run the following command on each database to enable Change Tracking at the database level:
```SQL
ALTER DATABASE [Nome database]
SET CHANGE_TRACKING = ON (CHANGE_RETENTION = 7 DAYS, AUTO_CLEANUP = ON);
```
You can also abilitate the change tracking feature only on the specific tables you wish to synchronize. You can do so with the following query template
```SQL
ALTER TABLE dbo.cfg_Aziende ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = ON);
ALTER TABLE dbo.cfg_Utenti ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = ON);
ALTER TABLE dbo.ana_CampiLiberiFornitori ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = ON);
ALTER TABLE dbo.ana_Clienti ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = ON);
```

2) If not already done, restore the .bak files onto the server and client SQL Server instances.\
**Recommended**: restore the databases on all instances before running sync (faster, avoids huge initial transfers).\
**Alternative**: restore only on the server. In this case, the SyncServer will provision and push the schema + data to the clients on the first synchronization.
This option can be extremely slow for large databases and may lead to timeouts. 

3) Start the services.
    - Start the SyncServer first.
    - Then start one or more SyncClients.
    - You may start them simultaneously, but sequential startup is cleaner and avoids transient errors.

After startup, verify that the server is healthy: 
```bash 
curl http://localhost:5202/api/health
```
or browse to http://localhost:5202/api/info to confirm the server is up and running. You'll see some other information about the system. 

## Initial Synchronization
The **first synchronization** is critical:
- If the clients have already restored the databases, then the sync will only reconcile the deltas 
- if the clients have not restored the databases, then the server has to send a full provision, which may take a long time depending on the database size. 
- Both the clients and the server will make a deprovision of the DMS tables and a new provision at the start of the application, which will re-create the tables based on the setup we give. This was made to make sure that, if any change was made to the setup in the 


## Verification Steps

\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\

# Client Configuration
Mirror the server configuration section but for SyncClient:
- appsettings.json structure for client
- Connection string requirements
- Client-specific options

\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\

# Usage Examples
## Basic Synchronization
## Manual Sync Triggers
## Monitoring Sync Status
## Common Workflows

\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\

# Docker Deployment
Since you have `docker_test` folder:

```
docker_test/
├── sync_dual_db_test/    # Primary test environment (recommended)
├── test_staging/         # Legacy - deprecated
└── test_sync/            # Legacy - deprecated
```


**Recommended Environment**: Use `sync_dual_db_test` as it contains the most up-to-date configuration and testing scenario. The others do not probably work anyway and should be deleted. I only leave them as a reference. 

## Primary Test Environment: sync_dual_db_test

This environment provides a complete dual-database synchronization testing setup:

```
sync_dual_db_test/
├── init/
│   ├── 2_TERRADISIENA.bak                   # Database backup file for TERRADISIENA database
│   ├── init.sh                              # Shell script for container initialization
│   ├── restore-db-TerraDiSiena.sql          # SQL script to restore TERRADISIENA database
│   ├── restore-db-zeuscfg_TerraDiSiena.sql  # SQL script to restore ZEUSCFG_TERRADISIENA database
│   └── zeuscfg_terradisiena.bak             # Database backup file for ZEUSCFG_TERRADISIENA database
└── docker-compose.sync_dual_db_test.yml     # Docker Compose configuration file
```

**Important**: The `.bak` files are not included in the repository due to size constraints. You'll need to provide your own database backup files or use the SQL scripts to create sample databases.

Let's start from the `docker-compose.sync_dual_db_test.yml` description ... 
_____________________________________________
### Docker Compose Configuration

This Docker Compose configuration creates a **hub-and-spoke** synchronization architecture with one central server and three client nodes, each one maintaining their own databases instances.  

#### File overview 
```yaml
services: 
  # Database Infrastructure (4 SQL Server instances)
  central-dualdb-tds:     # Master database hub (port 14330)
  client1-dualdb-tds:     # Client 1 local database (port 14331)
  client2-dualdb-tds:     # Client 2 local database (port 14332)
  client3-dualdb-tds:     # Client 3 local database (port 14333)

  # Database Initialization
  init-dualdb-tds:        # One-time setup service (restores .bak files)

  # Synchronization Infrastructure
  syncserver-dualdb-tds:  # Central sync coordination API (port 5202)
  syncclient1-dualdb-tds: # Client 1 sync agent
  syncclient2-dualdb-tds: # Client 2 sync agent
  syncclient3-dualdb-tds: # Client 3 sync agent

# Network isolation for all sync services
networks:
  syncnet: {}

# Persistent storage for each database instance
volumes: 
  central_data_staging: {}   # Central hub data persistence
  client1_data_staging: {}   # Client 1 data persistence
  client2_data_staging: {}   # Client 2 data persistence
  client3_data_staging: {}   # Client 3 data persistence
```
**Service Count**: 9 total services (4 databases + 1 initializer + 1 server + 3 clients)  
**Network Architecture**: Hub-and-spoke with isolated `syncnet` for secure communication  
**Data Persistence**: 4 named volumes ensuring data survives container restarts  
**Port Mapping**: Each database exposed on unique host ports (14330-14333) for external access

_____________________________________________
### Database Services (Sql Server Instances)
All database services in this Docker Compose configuration follow the same structural pattern, with only specific values differing between the central hub and client instances.

```yaml
central-dualdb-tds:
    image: mcr.microsoft.com/mssql/server:2022-latest
    container_name: central-dualdb-tds
    environment:
      - ACCEPT_EULA=Y
      - SA_PASSWORD=Terya12345!
      - MSSQL_PID=Express
    ports:
      - "14330:1433"
    volumes:
      - central_data_staging:/var/opt/mssql
      - ./init:/var/opt/mssql/backup
    networks: [syncnet]
    restart: unless-stopped
    healthcheck:
      test: ["CMD-SHELL", "bash -c '</dev/tcp/127.0.0.1/1433'"]
      interval: 5s
      timeout: 3s
      retries: 30
```
**Field Descriptions (Common to All Database Services):**

| Field | Value/Configuration | Function | Purpose |
|-------|-------------------|----------|---------|
| `image` | `mcr.microsoft.com/mssql/server:2022-latest` | Official Microsoft SQL Server 2022 Express container image | Provides consistent database engine across all instances |
| `container_name` | Service-specific name | Sets unique container identifier for each database instance | Enables clear identification and inter-service communication |
| `environment` | **SQL Server configuration** | | |
| └ `ACCEPT_EULA` | `Y` | Accepts Microsoft's End User License Agreement | Mandatory for SQL Server containers |
| └ `SA_PASSWORD` | `Terya12345!` | Sets consistent SA password across all instances | Authentication for database access |
| └ `MSSQL_PID` | `Express` | Uses free Express edition for all database instances | Cost-effective SQL Server edition |
| `ports` | `"[unique-port]:1433"` | Maps unique host ports to container's internal port 1433 | Allows external access to each database instance via different ports |
| `volumes` | **Storage configuration** | | |
| └ Named Volume | `[service-specific]:/var/opt/mssql` | Persistent database files | Data survives container restarts |
| └ Backup Mount | `./init:/var/opt/mssql/backup` | Shared access to initialization files | Access to .bak files and SQL scripts |
| `networks` | `[syncnet]` | Connects all database services to isolated network | Secure inter-container communication within sync ecosystem |
| `restart` | `unless-stopped` | Automatic restart on failure, manual stop prevention | High availability and fault tolerance |
| `healthcheck` | **Health monitoring (identical across all instances)** | | |
| └ Test | TCP connection verification to port 1433 | Verifies database availability | Ensures database is ready for connections |
| └ Frequency | Every 5 seconds with 3-second timeout | Regular health status checks | Early detection of service issues |
| └ Failure Threshold | 30 consecutive failures mark container as unhealthy | Failure detection threshold | Prevents false positives in health status |

\
**Service Variations:**

| Service | Container Name | Host Port | Volume Name | Role |
|---------|---------------|-----------|-------------|------|
| `central-dualdb-tds` | `central-dualdb-tds` | `14330` | `central_data_staging` | Master hub database |
| `client1-dualdb-tds` | `client1-dualdb-tds` | `14331` | `client1_data_staging` | Client 1 local database |
| `client2-dualdb-tds` | `client2-dualdb-tds` | `14332` | `client2_data_staging` | Client 2 local database |
| `client3-dualdb-tds` | `client3-dualdb-tds` | `14333` | `client3_data_staging` | Client 3 local database |

**Database Content (All Instances):**
- **`ZEUSCFG_TERRADISIENA`**: Configuration database with tables like `cfg_Aziende`, `cfg_Utenti`
- **`TERRADISIENA`**: Business data database with tables like `ana_Clienti`, `ana_Fornitori`

**External Access:**
- **Connection String Pattern**: `Server=localhost,[PORT];Database=[DB_NAME];User ID=sa;Password=Terya12345!;`
- **SSMS Access**: Connect using `localhost,14330` (central), `localhost,14331` (client1), etc.
_____
### Init-dualdb-tds service


```yaml
init-dualdb-tds:
    image: mcr.microsoft.com/mssql-tools
    container_name: init-dualdb-tds
    depends_on:
      central-dualdb-tds:
        condition: service_healthy
      client1-dualdb-tds:
        condition: service_healthy
      client2-dualdb-tds:
        condition: service_healthy
      client3-dualdb-tds:
        condition: service_healthy
    environment:
      - SA_PASSWORD=Terya12345!
    volumes:
      - ./init:/init:ro
    entrypoint: ["bash","-lc","tr -d '\r' </init/init.sh >/tmp/init.sh && bash /tmp/init.sh"]
    networks: [syncnet]
    restart: "no"
```

**Field Descriptions:**

| Field | Value/Configuration | Function | Purpose |
|-------|-------------------|----------|---------|
| `image` | `mcr.microsoft.com/mssql-tools` | Official Microsoft SQL Server command-line tools container | Provides `sqlcmd` utility for executing SQL scripts and database operations |
| `container_name` | `init-dualdb-tds` | Sets a specific container name for the initialization service | Easy identification during startup sequence and logging |
| `depends_on` | **Service dependency configuration with health checks** | | |
| └ `central-dualdb-tds` | `condition: service_healthy` | Central database must be ready | Prevents initialization from running before all databases are accessible |
| └ `client1-dualdb-tds` | `condition: service_healthy` | Client 1 database must be ready | |
| └ `client2-dualdb-tds` | `condition: service_healthy` | Client 2 database must be ready | |
| └ `client3-dualdb-tds` | `condition: service_healthy` | Client 3 database must be ready | |
| `environment` | `SA_PASSWORD=Terya12345!` | Provides authentication credentials for database connections | Allows the initialization script to connect to all SQL Server instances |
| `volumes` | `./init:/init:ro` | Mounts local `init` directory to container's `/init` path in read-only mode | Provides access to initialization scripts and database backup files |
| └ Contents | `init.sh` - Main initialization shell script | | |
| └ Contents | `*.bak` - Database backup files for restoration | | |
| └ Contents | `*.sql` - SQL scripts for database setup | | |
| `entrypoint` | **Custom startup command** | | |
| └ Step 1 | `tr -d '\r'` | Removes Windows carriage return characters from the script | Handles cross-platform line ending compatibility |
| └ Step 2 | `</init/init.sh >/tmp/init.sh` | Copies cleaned script to temp location | |
| └ Step 3 | `bash /tmp/init.sh` | Executes the initialization script | |
| `networks` | `[syncnet]` | Connects to the isolated sync network | Enables communication with all SQL Server instances for database operations |
| `restart` | `"no"` | Prevents automatic container restart | One-time execution service that should not restart after completion |

#### Service Role

- **Primary Function**: One-time database initialization and setup across all SQL Server instances
- **Execution Pattern**: Runs once during initial startup, then stops
- **Key Operations**:
  - Waits for all SQL Server containers to be healthy and accessible
  - Restores database backup files (`*.bak`) to each SQL Server instance
  - Creates required database structures on all instances
  - Enables Change Tracking for synchronization capabilities
  - Sets up initial sync metadata and configuration

#### Initialization Sequence
1. **Dependency Check**: Waits for all 4 SQL Server containers to pass health checks
2. **Script Preparation**: Cleans and prepares the initialization script for execution
3. **Database Restoration**: Restores `ZEUSCFG_TERRADISIENA` and `TERRADISIENA` databases on all instances
4. **Configuration Setup**: Enables Change Tracking and creates sync-required objects
5. **Completion**: Container stops after successful initialization

#### Critical Notes
- **Single Execution**: This service runs only once per environment setup
- **Dependency Critical**: Will not start until all databases are fully operational
- **Cross-Platform**: Handles Windows/Linux line ending differences automatically
- **Read-Only Access**: Cannot modify the source initialization files, ensuring consistency
_____________________________________________


### Clients and Server Services 
The synchronization infrastructure consists of one central coordination server and three client agents that handle bidirectional data synchronization between the central hub and individual client databases. These services form the operational core of the DMS Sync Solution, orchestrating data flow and maintaining consistency across the distributed database environment.
- **Central Sync Server** (`syncserver-dualdb-tds`) acts as the coordination hub
- **Client Sync Agents** (`syncclient1/2/3-dualdb-tds`) operate as distributed spokes

| Service Type | Count | Primary Function | Key Features |
|-------------|-------|------------------|--------------|
| **Sync Server** | 1 | Central coordination hub | REST API endpoints, conflict resolution, metadata management |
| **Sync Clients** | 3 | Distributed sync agents | Local database sync, scheduled operations, HTTP API consumers |

Server service
```yaml
  syncserver-dualdb-tds:
    build: ../../SyncServer
    image: dmssyncserver-dualdb:latest
    container_name: syncserver-dualdb-tds
    depends_on:
      central-dualdb-tds:
        condition: service_healthy
    environment:
      - PrimaryDatabaseConnectionString=Server=central-dualdb-tds,1433;Database=ZEUSCFG_TERRADISIENA;User ID=sa;Password=Terya12345!;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True      
      - SecondaryDatabaseConnectionString=Server=central-dualdb-tds,1433;Database=TERRADISIENA;User ID=sa;Password=Terya12345!;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True
      - ASPNETCORE_ENVIRONMENT=Development
      - ASPNETCORE_URLS=http://0.0.0.0:8080
    ports:
      - "5202:8080"   #"HOST_PORT:CONTAINER_PORT"
    networks: [syncnet]
    restart: unless-stopped
    healthcheck:
      test: ["CMD-SHELL", "bash -c '</dev/tcp/127.0.0.1/8080'"]
      interval: 5s
      timeout: 3s
      retries: 60
```
Fields Description: 

- **`build`**: 
  - **Value**: `../../SyncServer`
  - **Function**: Specifies the build context relative to the Docker Compose file location
  - **Purpose**: Builds the container image from the SyncServer project source code using its Dockerfile. Inside the SyncServer project you can find the DockerFile with the following lines of code: 

```Dockerfile
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish SyncServer.csproj -c Release -o /app

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "SyncServer.dll"]
```
- **`image`**: 
  - **Value**: `dmssyncserver-dualdb:latest`
  - **Function**: Tags the built image with a specific name and version
  - **Purpose**: Allows image reuse and identification in Docker registry or local cache

- **`container_name`**: 
  - **Value**: `syncserver-dualdb-tds`
  - **Function**: Sets a fixed container name for easy reference
  - **Purpose**: Enables predictable container identification and inter-service communication

- **`depends_on`**: Service dependency with health check
  - **Dependency**: `central-dualdb-tds: condition: service_healthy`
  - **Function**: Ensures the central database is fully operational before starting the sync server
  - **Purpose**: Prevents startup failures due to unavailable database connections

- **`environment`**: Application configuration via environment variables
  - **`PrimaryDatabaseConnectionString`**: Connection to `ZEUSCFG_TERRADISIENA` database on central server
  - **`SecondaryDatabaseConnectionString`**: Connection to `TERRADISIENA` database on central server
  - **`ASPNETCORE_ENVIRONMENT=Development`**: Sets ASP.NET Core to development mode (detailed logging, developer exception pages)
  - **`ASPNETCORE_URLS=http://0.0.0.0:8080`**: Binds the web server to all network interfaces on port 8080

- **`ports`**: 
  - **Value**: `"5202:8080"`
  - **Function**: Maps host port 5202 to container's internal port 8080
  - **Purpose**: Exposes the sync server's REST API endpoints to external clients
  - **Access**: API available at `http://localhost:5202`

- **`networks`**: 
  - **Value**: `[syncnet]`
  - **Function**: Connects to the isolated Docker network
  - **Purpose**: Enables secure communication with database services and client agents

- **`restart`**: 
  - **Value**: `unless-stopped`
  - **Function**: Automatically restarts container on failure, except when manually stopped
  - **Purpose**: Ensures high availability and continuous operation of the sync coordination hub

- **`healthcheck`**: Service health monitoring
  - **Test**: `["CMD-SHELL", "bash -c '</dev/tcp/127.0.0.1/8080'"]` - TCP connection test to port 8080
  - **Interval**: `5s` - Health check runs every 5 seconds
  - **Timeout**: `3s` - Maximum 3 seconds wait for response
  - **Retries**: `60` - Considers service unhealthy after 60 consecutive failures (5 minutes)




Client service 
```yaml
  syncclient1-dualdb-tds:
    build: ../../SyncClient
    image: dmssyncclient1-dualdb:latest
    container_name: syncclient1-dualdb-tds
    depends_on:
      client1-dualdb-tds:
        condition: service_healthy
      syncserver-dualdb-tds:
        condition: service_healthy
    environment:
      - PrimaryDatabaseConnectionString=Server=client1-dualdb-tds,1433;Database=ZEUSCFG_TERRADISIENA;User ID=sa;Password=Terya12345!;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True
      - SecondaryDatabaseConnectionString=Server=client1-dualdb-tds,1433;Database=TERRADISIENA;User ID=sa;Password=Terya12345!;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True
      - PrimarySyncEndpoint=http://syncserver-dualdb-tds:8080/api/sync/db1
      - SecondarySyncEndpoint=http://syncserver-dualdb-tds:8080/api/sync/db2
    networks: [syncnet]
    restart: unless-stopped
```

**Field Descriptions:**

- **`build`**: 
  - **Value**: `../../SyncClient`
  - **Function**: Specifies the build context relative to the Docker Compose file location
  - **Purpose**: Builds the container image from the SyncClient project source code using its Dockerfile. The following are the lines of code that you should find:
  ```Dockerfile
    # Stage 1: Build
  FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
  WORKDIR /src
  COPY . .
  RUN dotnet publish SyncClient.csproj -c Release -o /app

  # Stage 2: Runtime
  FROM mcr.microsoft.com/dotnet/aspnet:8.0
  WORKDIR /app
  COPY --from=build /app .

  # Install SQL Server tools (sqlcmd and bcp)
  RUN apt-get update && \
      apt-get install -y curl gnupg && \
      curl https://packages.microsoft.com/keys/microsoft.asc | apt-key add - && \
      curl https://packages.microsoft.com/config/ubuntu/22.04/prod.list > /etc/apt/sources.list.d/mssql-release.list && \
      apt-get update && \
      ACCEPT_EULA=Y apt-get install -y msodbcsql18 mssql-tools18 && \
      echo 'export PATH="$PATH:/opt/mssql-tools18/bin"' >> ~/.bashrc

  ENV PATH="$PATH:/opt/mssql-tools18/bin"

  ENTRYPOINT ["dotnet", "SyncClient.dll"]
  ```

- **`image`**: 
  - **Value**: `dmssyncclient1-dualdb:latest`
  - **Function**: Tags the built image with a specific name and version
  - **Purpose**: Allows image reuse and identification in Docker registry or local cache

- **`container_name`**: 
  - **Value**: `syncclient1-dualdb-tds`
  - **Function**: Sets a fixed container name for easy reference
  - **Purpose**: Enables predictable container identification and inter-service communication

- **`depends_on`**: Service dependency with health checks
  - **`client1-dualdb-tds: condition: service_healthy`**: Ensures Client 1 database is fully operational before starting
  - **`syncserver-dualdb-tds: condition: service_healthy`**: Ensures central sync server is running and accessible
  - **Purpose**: Prevents sync failures due to unavailable dependencies

- **`environment`**: Application configuration via environment variables
  - **`PrimaryDatabaseConnectionString`**: `Server=client1-dualdb-tds,1433;Database=ZEUSCFG_TERRADISIENA;...`
    - Establishes connection to client's local configuration database
  - **`SecondaryDatabaseConnectionString`**: `Server=client1-dualdb-tds,1433;Database=TERRADISIENA;...`
    - Establishes connection to client's local business data database
  - **`PrimarySyncEndpoint`**: `http://syncserver-dualdb-tds:8080/api/sync/db1`
    - Defines API endpoint for ZEUSCFG_TERRADISIENA sync operations
  - **`SecondarySyncEndpoint`**: `http://syncserver-dualdb-tds:8080/api/sync/db2`
    - Defines API endpoint for TERRADISIENA sync operations

- **`networks`**: 
  - **Value**: `[syncnet]`
  - **Function**: Connects to the isolated Docker network
  - **Purpose**: Enables secure communication with database services and sync server

- **`restart`**: 
  - **Value**: `unless-stopped`
  - **Function**: Automatically restarts container on failure, except when manually stopped
  - **Purpose**: Ensures continuous sync operations and high



**Communication Flow:**
1. **Client-to-Server**: Each client agent initiates sync requests to the central server via HTTP/HTTPS
2. **Server-to-Database**: The server coordinates with the central database to manage sync operations
3. **Bidirectional Sync**: Changes flow both ways - from clients to central and central to clients
4. **Conflict Resolution**: The central server handles data conflicts using configurable policies

**Key Technologies:**
- **Dotmim.Sync Framework**: Core synchronization engine providing change tracking and conflict resolution
- **ASP.NET Core Web API**: RESTful endpoint exposure for sync operations
- **.NET 8 Console Applications**: Lightweight client agents for continuous sync operations
- **SQL Server Change Tracking**: Database-level change detection for efficient synchronization

**Operational Characteristics:**
- **Continuous Operation**: Sync services run 24/7 with automatic restart capabilities
- **Health Monitoring**: Built-in health checks ensure service availability
- **Dependency Management**: Services start in proper sequence based on database availability
- **Network Isolation**: All sync traffic flows through the dedicated `syncnet` Docker network

**Service Startup Sequence:**
1. Database services become healthy
2. Database initialization completes
3. Central sync server starts and connects to central database
4. Client sync agents start and register with the central server
5. Synchronization operations begin on configured schedules







\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\
\













## API Reference
Document the SyncServer endpoints:
- Available endpoints
- Request/Response formats
- Authentication (if any)
- Error responses


## Troubleshooting
### Common Issues
### Error Messages and Solutions
### Performance Tuning
### Logging and Diagnostics



## Development
### Project Structure
### Building from Source
### Running Tests
### Contributing Guidelines