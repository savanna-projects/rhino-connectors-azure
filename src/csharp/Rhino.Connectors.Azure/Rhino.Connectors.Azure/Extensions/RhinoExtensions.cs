/*
 * CHANGE LOG - keep only last 5 threads
 * 
 * RESOURCES
 */
using Microsoft.VisualStudio.Services.Common;

using Rhino.Api.Contracts.Configuration;
using Rhino.Connectors.Azure.Framework;

using System.Net;

namespace Rhino.Connectors.Azure.Extensions
{
    /// <summary>
    /// Extension package for Rhino objects.
    /// </summary>
    internal static class RhinoExtensions
    {
        /// <summary>
        /// Gets <see cref="VssCredentials"/> by RhinoConfiguration.ConnectorConfiguration.
        /// </summary>
        /// <param name="configuration">RhinoConfiguration by which to get <see cref="VssCredentials"/>.</param>
        /// <returns><see cref="VssCredentials"/> object for interacting with Azure DevOps or Team Foundation Server.</returns>
        public static VssCredentials GetVssCredentials(this RhinoConfiguration configuration)
        {
            // setup
            var isOs = configuration.ConnectorConfiguration.AsOsUser;
            var userName = configuration.ConnectorConfiguration.UserName;
            var password = configuration.ConnectorConfiguration.Password;

            // by token
            if (string.IsNullOrEmpty(password) && !string.IsNullOrEmpty(userName))
            {
                return CredentialsFactory.GetVssCredentials(personalAccessToken: userName);
            }

            // get
            return isOs
                ? CredentialsFactory.GetVssCredentials(new NetworkCredential(userName, password))
                : CredentialsFactory.GetVssCredentials(userName, password);
        }
    }
}