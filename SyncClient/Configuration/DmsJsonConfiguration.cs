using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SyncClient.Console.Configuration
{
    /// <summary>
    /// Implementazione di <see cref="IDmsSyncConfiguration"/> che carica la configurazione
    /// da un file JSON e dalle variabili d'ambiente, fornendo metodi sicuri per accedere ai valori.
    /// Utilizza il logging per segnalare configurazioni mancanti o errate.
    /// </summary>
    public class DmsJsonConfiguration : IDmsSyncConfiguration
    {
        #region fields
        private readonly IConfiguration _config;
        private readonly ILogger<DmsJsonConfiguration> _logger;
        #endregion

        #region constructor
        /// <summary>
        /// Costruisce una nuova istanza di <see cref="DmsJsonConfiguration"/>.
        /// Carica la configurazione dal file JSON specificato e dalle variabili d'ambiente.
        /// </summary>
        /// <param name="jsonFile">Percorso del file di configurazione JSON.</param>
        /// <param name="logger">Istanza di logger per la registrazione di warning ed errori.</param>
        public DmsJsonConfiguration(string jsonFile, ILogger<DmsJsonConfiguration> logger)
        {
            /*
            FUTURE: Si può aggiungere una validazione esplicita del file JSON di configurazione
            per intercettare errori di sintassi o struttura prima di caricare i valori.
            Ad esempio, si può implementare un controllo con try-catch o usare una libreria di validazione JSON.
            */
            _config = new ConfigurationBuilder()
                .AddJsonFile(jsonFile)
                .AddEnvironmentVariables()
                .Build();
            _logger = logger;
        }
        #endregion

        #region methods
        /// <summary>
        /// Restituisce la stringa di connessione associata al nome specificato.
        /// Lancia un'eccezione e registra un warning se la stringa non è presente o è vuota.
        /// </summary>
        /// <param name="name">Nome della stringa di connessione da recuperare.</param>
        /// <returns>La stringa di connessione richiesta.</returns>
        /// <exception cref="InvalidOperationException">Se la stringa di connessione non è configurata o è vuota.</exception>
        public string GetConnectionString(string name)
        {
            var value = _config.GetConnectionString(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                _logger.LogWarning($"[WARNING] la stringa di connessione '{name}' non è configurata o è vuota");
                throw new InvalidOperationException($"La stringa di connessione '{name}' non è configurata o è vuota.");
            }

            return value;
        }
       

        /// <summary>
        /// Restituisce il valore di configurazione associato alla chiave specificata.
        /// Lancia un'eccezione e registra un warning se il valore non è presente o è vuoto.
        /// </summary>
        /// <param name="key">Chiave della configurazione da recuperare.</param>
        /// <returns>Il valore di configurazione richiesto.</returns>
        /// <exception cref="InvalidOperationException">Se la chiave non è presente o il valore è vuoto.</exception>
        public string GetValue(string key)
        {
            var value = _config[key];
            if (string.IsNullOrEmpty(value))
            {
                _logger.LogWarning($"[WARNING] la chiave di configurazione '{key}' non è presente o è vuota");
                throw new InvalidOperationException($"La chiave di configurazione '{key}' non è presente o è vuota.");
            }
            return value;
        }
        #endregion
    }
}
