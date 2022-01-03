/*
 * CHANGE LOG - keep only last 5 threads
 * 
 * RESOURCES
 */
using Gravity.Abstraction.Logging;

using Microsoft.TeamFoundation.TestManagement.WebApi;

using Rhino.Api;
using Rhino.Api.Contracts.Attributes;
using Rhino.Api.Contracts.AutomationProvider;
using Rhino.Api.Contracts.Configuration;
using Rhino.Api.Extensions;
using Rhino.Connectors.Azure.Contracts;

using System;
using System.Collections.Generic;
using System.Linq;

namespace Rhino.Connectors.Azure
{
    /// <summary>
    /// Azure connector for running Test Manager tests as Rhino Automation Specs.
    /// </summary>
    [Connector(
        value: RhinoConnectors.AzureTestManager,
        Name = "Connector - Azure DevOps & Team Foundation Server (TFS)",
        Description = "Allows to execute Rhino Specs from Azure DevOps or Team Foundation Server Test Case work items and report back as Test Runs.")]
    public class AzureConnector : RhinoConnector
    {
        #region *** Constructors   ***
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

        #region *** Per Test Setup ***
        /// <summary>
        /// Performed just before each test is called.
        /// </summary>
        /// <param name="testCase">The Rhino.Api.Contracts.AutomationProvider.RhinoTestCase which is being executed.</param>
        public override RhinoTestCase OnTestSetup(RhinoTestCase testCase)
        {
            // setup
            testCase.Context[AzureContextEntry.Outcome] = nameof(TestOutcome.InProgress);

            // update
            ProviderManager.UpdateTestResult(testCase);

            // return with results
            return testCase;
        }
        #endregion

        #region *** Per Test Clean ***
        /// <summary>
        /// Performed just after each test is called.
        /// </summary>
        /// <param name="testCase">The Rhino.Api.Contracts.AutomationProvider.RhinoTestCase which was executed.</param>
        public override RhinoTestCase OnTestTeardown(RhinoTestCase testCase)
        {
            // setup
            var outcome = testCase.Actual ? nameof(TestOutcome.Passed) : nameof(TestOutcome.Failed);

            // inconclusive on this run (mark as default)
            if (ProviderManager.TestRun.TestCases.Any(i => i.Key.Equals(testCase.Key) && i.Inconclusive))
            {
                outcome = nameof(TestOutcome.Inconclusive);
            }

            // put
            testCase.Context[AzureContextEntry.Outcome] = outcome;

            // update
            ProviderManager.UpdateTestResult(testCase);

            // return with results
            return testCase;
        }
        #endregion
    }
}