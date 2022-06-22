using Gravity.Abstraction.Logging;

using Rhino.Api;
using Rhino.Api.Contracts.AutomationProvider;
using Rhino.Api.Contracts.Configuration;
using Rhino.Api.Extensions;
using Rhino.Api.Interfaces;
using Rhino.Connectors.Azure.Text.Extensions;
using Rhino.Connectors.Text;

using System;
using System.Collections.Generic;
using System.Linq;

namespace Rhino.Connectors.Azure.Text
{
    /// <summary>
    /// Azure connector for using Azure Test Manager tests as Rhino Specs.
    /// </summary>
    public class AzureTextAutomationProvider : ProviderManager
    {
        // state: global parameters
        private readonly ILogger _logger;
        private readonly IProviderManager _azureProvider;
        private readonly IProviderManager _textProvider;

        #region *** Constructors      ***
        /// <summary>
        /// Creates a new instance of AzureTextAutomationProvider.
        /// </summary>
        /// <param name="configuration">Rhino.Api.Contracts.Configuration.RhinoConfiguration to use with this provider.</param>
        public AzureTextAutomationProvider(RhinoConfiguration configuration)
            : this(configuration, Utilities.Types)
        { }

        /// <summary>
        /// Creates a new instance of AzureTextAutomationProvider.
        /// </summary>
        /// <param name="configuration">Rhino.Api.Contracts.Configuration.RhinoConfiguration to use with this provider.</param>
        /// <param name="types">A collection of <see cref="Type"/> to load for this repository.</param>
        public AzureTextAutomationProvider(RhinoConfiguration configuration, IEnumerable<Type> types)
            : this(configuration, types, Utilities.CreateDefaultLogger(configuration))
        { }

        /// <summary>
        /// Creates a new instance of AzureTextAutomationProvider.
        /// </summary>
        /// <param name="configuration">Rhino.Api.Contracts.Configuration.RhinoConfiguration to use with this provider.</param>
        /// <param name="types">A collection of <see cref="Type"/> to load for this repository.</param>
        /// <param name="logger">Gravity.Abstraction.Logging.ILogger implementation for this provider.</param>
        public AzureTextAutomationProvider(RhinoConfiguration configuration, IEnumerable<Type> types, ILogger logger)
            : base(configuration, types, logger)
        {
            _logger = logger?.Setup(loggerName: nameof(AzureTextAutomationProvider));
            _azureProvider = new AzureAutomationProvider(configuration, types, _logger);
            _textProvider = new TextAutomationProvider(configuration, types, _logger);
        }
        #endregion

        #region *** Get: Test Cases   ***
        /// <summary>
        /// Returns a list of test cases for a project.
        /// </summary>
        /// <param name="ids">A list of issue id or key to get test cases by.</param>
        /// <returns>A collection of Rhino.Api.Contracts.AutomationProvider.RhinoTestCase</returns>
        protected override IEnumerable<RhinoTestCase> OnGetTestCases(params string[] ids)
        {
            // setup
            var testCases = _textProvider.InvokeMethod<IEnumerable<RhinoTestCase>>("OnGetTestCases", new object[] { ids });

            // sync
            foreach (var testCase in testCases)
            {
                _azureProvider.UpdateTestCase(testCase);
            }

            // get
            return testCases.Any()
                ? _azureProvider.InvokeMethod<IEnumerable<RhinoTestCase>>("OnGetTestCases", new object[] { testCases.Select(i => i.Key).ToArray() })
                : _azureProvider.InvokeMethod<IEnumerable<RhinoTestCase>>("OnGetTestCases", new object[] { ids });
        }
        #endregion

        #region *** Create: Test Case ***
        /// <summary>
        /// Creates a new test case under the specified automation provider.
        /// </summary>
        /// <param name="testCase">Rhino.Api.Contracts.AutomationProvider.RhinoTestCase by which to create automation provider test case.</param>
        /// <returns>The ID of the newly created entity.</returns>
        protected override string OnCreateTestCase(RhinoTestCase testCase)
        {
            return _azureProvider.InvokeMethod<string>(
                method: "OnCreateTestCase",
                parameters: new object[] { testCase });
        }
        #endregion

        #region *** Create: Test Run  ***
        /// <summary>
        /// Creates an automation provider test run entity. Use this method to implement the automation
        /// provider test run creation and to modify the loaded Rhino.Api.Contracts.AutomationProvider.RhinoTestRun.
        /// </summary>
        /// <param name="testRun">Rhino.Api.Contracts.AutomationProvider.RhinoTestRun object to modify before creating.</param>
        /// <returns>Rhino.Api.Contracts.AutomationProvider.RhinoTestRun based on provided test cases.</returns>
        protected override RhinoTestRun OnCreateTestRun(RhinoTestRun testRun)
        {
            return _azureProvider.InvokeMethod<RhinoTestRun>(
                method: "OnCreateTestRun",
                parameters: new object[] { testRun });
        }

        /// <summary>
        /// Completes automation provider test run results, if any were missed or bypassed.
        /// </summary>
        /// <param name="testRun">Rhino.Api.Contracts.AutomationProvider.RhinoTestRun results object to complete by.</param>
        protected override void OnRunTeardown(RhinoTestRun testRun)
        {
            _azureProvider.InvokeMethod(
                method: "OnRunTeardown",
                parameters: new object[] { testRun });
        }
        #endregion

        #region *** Put: Test Results ***
        /// <summary>
        /// Updates a single test results iteration under automation provider.
        /// </summary>
        /// <param name="testCase">Rhino.Api.Contracts.AutomationProvider.RhinoTestCase by which to update results.</param>
        protected override void OnUpdateTestResult(RhinoTestCase testCase)
        {
            _azureProvider.InvokeMethod(
                method: "OnUpdateTestResult",
                parameters: new object[] { testCase });
        }
        #endregion

        #region *** Bugs & Defects    ***
        /// <summary>
        /// Gets a list of open bugs.
        /// </summary>
        /// <param name="testCase">RhinoTestCase by which to find bugs.</param>
        /// <returns>A list of bugs (can be JSON or ID for instance).</returns>
        protected override IEnumerable<string> OnGetBugs(RhinoTestCase testCase)
        {
            return _azureProvider.InvokeMethod<IEnumerable<string>>(
                method: "OnGetBugs",
                parameters: new object[] { testCase });
        }

        /// <summary>
        /// Asserts if the RhinoTestCase has already an open bug.
        /// </summary>
        /// <param name="testCase">RhinoTestCase by which to assert against match bugs.</param>
        /// <returns>An open bug.</returns>
        protected override string OnGetOpenBug(RhinoTestCase testCase)
        {
            return _azureProvider.InvokeMethod<string>(
                method: "OnGetOpenBug",
                parameters: new object[] { testCase });
        }

        /// <summary>
        /// Creates a new bug under the specified automation provider.
        /// </summary>
        /// <param name="testCase">Rhino.Api.Contracts.AutomationProvider.RhinoTestCase by which to create automation provider bug.</param>
        /// <returns>The ID of the newly created entity.</returns>
        protected override string OnCreateBug(RhinoTestCase testCase)
        {
            return _azureProvider.InvokeMethod<string>(
                method: "OnCreateBug",
                parameters: new object[] { testCase });
        }

        /// <summary>
        /// Executes a routine of post bug creation.
        /// </summary>
        /// <param name="testCase">RhinoTestCase to execute routine on.</param>
        protected override void OnCreateBugTeardown(RhinoTestCase testCase)
        {
            _azureProvider.InvokeMethod(
                method: "OnCreateBugTeardown",
                parameters: new object[] { testCase });
        }

        /// <summary>
        /// Updates an existing bug (partial updates are supported, i.e. you can submit and update specific fields only).
        /// </summary>
        /// <param name="testCase">Rhino.Api.Contracts.AutomationProvider.RhinoTestCase by which to update automation provider bug.</param>
        protected override string OnUpdateBug(RhinoTestCase testCase)
        {
            return _azureProvider.InvokeMethod<string>(
                method: "OnUpdateBug",
                parameters: new object[] { testCase });
        }

        /// <summary>
        /// Close all existing bugs.
        /// </summary>
        /// <param name="testCase">Rhino.Api.Contracts.AutomationProvider.RhinoTestCase by which to close automation provider bugs.</param>
        protected override IEnumerable<string> OnCloseBugs(RhinoTestCase testCase)
        {
            return _azureProvider.InvokeMethod<IEnumerable<string>>(
                method: "OnCloseBugs",
                parameters: new object[] { testCase });
        }

        /// <summary>
        /// Close all existing bugs.
        /// </summary>
        /// <param name="testCase">Rhino.Api.Contracts.AutomationProvider.RhinoTestCase by which to close automation provider bugs.</param>
        protected override string OnCloseBug(RhinoTestCase testCase)
        {
            return _azureProvider.InvokeMethod<string>(
                method: "OnCloseBug",
                parameters: new object[] { testCase });
        }
        #endregion
    }
}
