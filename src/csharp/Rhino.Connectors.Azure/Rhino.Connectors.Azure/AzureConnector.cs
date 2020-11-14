/*
 * CHANGE LOG - keep only last 5 threads
 * 
 * RESOURCES
 */
using Gravity.Abstraction.Logging;

using Rhino.Api;
using Rhino.Api.Contracts.Attributes;
using Rhino.Api.Contracts.Configuration;
using Rhino.Api.Extensions;

using System;
using System.Collections.Generic;

namespace Rhino.Connectors.Azure
{
    /// <summary>
    /// Azure connector for running Test Manager tests as Rhino Automation Specs.
    /// </summary>
    [Connector(
        value: Connector.AzureTestManager,
        Name = "Connector - Azure DevOps & Team Foundation Server (TFS).",
        Description = "Allows to execute Rhino Specs from Azure DevOps or Team Foundation Server Test Case work items and report back as Test Runs.")]
    public class AzureConnector : RhinoConnector
    {
        #region *** Constructors ***
        /// <summary>
        /// Creates a new instance of this Rhino.Api.Components.RhinoConnector.
        /// </summary>
        /// <param name="configuration">Rhino.Api.Contracts.Configuration.RhinoConfiguration to use with this connector.</param>
        public AzureConnector(RhinoConfiguration configuration)
            : this(configuration, Utilities.Types)
        { }

        /// <summary>
        /// Creates a new instance of this Rhino.Api.Components.RhinoConnector.
        /// </summary>
        /// <param name="configuration">Rhino.Api.Contracts.Configuration.RhinoConfiguration to use with this connector.</param>
        /// <param name="types">A collection of <see cref="Type"/> to load for this repository.</param>
        public AzureConnector(RhinoConfiguration configuration, IEnumerable<Type> types)
            : this(configuration, types, Utilities.CreateDefaultLogger(configuration))
        { }

        /// <summary>
        /// Creates a new instance of this Rhino.Api.Components.RhinoConnector.
        /// </summary>
        /// <param name="configuration">Rhino.Api.Contracts.Configuration.RhinoConfiguration to use with this connector.</param>
        /// <param name="types">A collection of <see cref="Type"/> to load for this repository.</param>
        /// <param name="logger">Gravity.Abstraction.Logging.ILogger implementation for this connector.</param>
        public AzureConnector(RhinoConfiguration configuration, IEnumerable<Type> types, ILogger logger)
            : this(configuration, types, logger, connect: true)
        { }

        /// <summary>
        /// Creates a new instance of this Rhino.Api.Components.RhinoConnector.
        /// </summary>
        /// <param name="configuration">Rhino.Api.Contracts.Configuration.RhinoConfiguration to use with this connector.</param>
        /// <param name="types">A collection of <see cref="Type"/> to load for this repository.</param>
        /// <param name="logger">Gravity.Abstraction.Logging.ILogger implementation for this connector.</param>
        /// <param name="connect"><see cref="true"/> for immediately connect after construct <see cref="false"/> skip connection.</param>
        /// <remarks>If you skip connection you must explicitly call Connect method.</remarks>
        public AzureConnector(RhinoConfiguration configuration, IEnumerable<Type> types, ILogger logger, bool connect)
            : base(configuration, types, logger)
        {
            // setup
            ProviderManager = new AzureAutomationProvider(configuration, types, logger);

            // connect on constructing
            if (connect)
            {
                Connect();
            }
        }
        #endregion
    }
}