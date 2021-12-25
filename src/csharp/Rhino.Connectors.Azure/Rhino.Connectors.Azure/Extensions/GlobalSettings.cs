/*
 * CHANGE LOG - keep only last 5 threads
 * 
 * RESOURCES
 */
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Rhino.Connectors.Azure.Extensions
{
    // TODO: expose as application settings
    internal static class GlobalSettings
    {
        /// <summary>
        /// The numbers of attempts an HTTP client tries to send a request before throwing an exception.
        /// </summary>
        public const int ClientNumberOfAttempts = 15;

        /// <summary>
        /// Get the default Newtonsoft.Json.Serialization.JsonSerializerSettings
        /// </summary>
        public static JsonSerializerSettings NewtonsoftSerializerSettings = new()
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };
    }
}
