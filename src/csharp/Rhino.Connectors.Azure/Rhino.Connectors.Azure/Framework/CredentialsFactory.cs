/*
 * CHANGE LOG - keep only last 5 threads
 * 
 * RESOURCES
 */
using Microsoft.VisualStudio.Services.Common;

using System.Net;

namespace Rhino.Connectors.Azure.Framework
{
    /// <summary>
    /// Utility class for creating various <see cref="VssCredentials"/> object.
    /// </summary>
    public static class CredentialsFactory
    {
        /// <summary>
        /// Creates <see cref="VssCredentials"/>.
        /// </summary>
        /// <param name="userName">Azure DevOps or Team Foundation Server (TFS) user name.</param>
        /// <param name="password">Azure DevOps or Team Foundation Server (TFS) password.</param>
        /// <returns><see cref="VssCredentials"/>.</returns>
        public static VssCredentials GetVssCredentials(string userName, string password)
        {
            // setup
            var basicCredential = new VssBasicCredential(userName, password);

            // get
            return new VssCredentials(basicCredential);
        }

        /// <summary>
        /// Creates <see cref="VssCredentials"/>.
        /// </summary>
        /// <param name="personalAccessToken">Azure DevOps or Team Foundation Server (TFS) personal access token (PAT).</param>
        /// <returns><see cref="VssCredentials"/>.</returns>
        public static VssCredentials GetVssCredentials(string personalAccessToken)
        {
            // setup
            var basicCredential = new VssBasicCredential("", personalAccessToken);

            // get
            return new VssCredentials(basicCredential);
        }

        /// <summary>
        /// Creates <see cref="VssCredentials"/>.
        /// </summary>
        /// <param name="credentials"><see cref="NetworkCredential"/> by which to factor <see cref="VssBasicCredential"/>.</param>
        /// <returns><see cref="VssCredentials"/>.</returns>
        /// <remarks><see cref="VssCredentials"/> will factored by <see cref="WindowsCredential"/>.</remarks>
        public static VssCredentials GetVssCredentials(NetworkCredential credentials)
        {
            // setup
            var windowsCredentials = new WindowsCredential(credentials);

            // get
            return new VssCredentials(windowsCredentials);
        }
    }
}