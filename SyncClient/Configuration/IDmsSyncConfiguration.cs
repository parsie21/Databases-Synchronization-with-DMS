using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SyncClient.Console.Configuration
{
    public interface IDmsSyncConfiguration
    {
        string GetConnectionString(string name);
        string GetValue(string key);

        /*
        POSSIBILI METODI FUTURI DI VALIDAZIONE 
        ----------------------------
        bool isConnectionStringValid
        bool isValuePresent
        bool tryGetValue
        bool validateAll
        */
    }
}
