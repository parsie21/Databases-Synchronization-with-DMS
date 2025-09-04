# DMS Sync Solution

# Overview
A .NET 8 Database syncronization solution using Dotmim.Sync Framework for bidirectional data sync between SQL Server Databases 

**Framework Documentation**:  [Dotmim.Sync Framework](https://dotmimsync.readthedocs.io/) - refer to the official documentation for advanced configuration options and troubleshooting.

## Quick Start
Step-by-step guide to get the solution running locally:
- Prerequisites (SQL Server, .NET 8)
- Building the solution
- Initial database setup
- Running the server
- Running the client


## Architecture 
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


## Installation & Setup
### Prerequisites
### Database Preparation
### Initial Synchronization
### Verification Steps


## Client Configuration
Mirror the server configuration section but for SyncClient:
- appsettings.json structure for client
- Connection string requirements
- Client-specific options

## Usage Examples
### Basic Synchronization
### Manual Sync Triggers
### Monitoring Sync Status
### Common Workflows



## Docker Deployment
Since you have docker_test folder:
- Docker Compose setup
- Testing environments
- Production deployment considerations



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