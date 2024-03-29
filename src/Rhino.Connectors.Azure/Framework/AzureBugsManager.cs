﻿/*
 * CHANGE LOG - keep only last 5 threads
 * 
 * RESOURCES
 */
using Gravity.Abstraction.Logging;

using Microsoft.TeamFoundation.TestManagement.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.WebApi;

using Rhino.Api.Contracts.AutomationProvider;
using Rhino.Api.Contracts.Configuration;
using Rhino.Connectors.Azure.Extensions;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using GlobalSettings = Rhino.Connectors.Azure.Extensions.GlobalSettings;

namespace Rhino.Connectors.Azure.Framework
{
    /// <summary>
    /// Bugs manager component common to all Jira connectors.
    /// </summary>
    public class AzureBugsManager
    {
        // constants
        private const StringComparison Compare = StringComparison.OrdinalIgnoreCase;

        // members: clients
        private readonly WorkItemTrackingHttpClient itemManagement;

        // members: state        
        private readonly ILogger logger;
        private readonly VssConnection connection;
        private readonly RhinoConfiguration configuration;

        #region *** Constructors ***
        /// <summary>
        /// Creates a new instance of this BugManager.
        /// </summary>
        /// <param name="configuration">RhinoConfiguration by which to create the BugManager.</param>
        public AzureBugsManager(RhinoConfiguration configuration)
            : this(configuration, logger: default)
        { }

        /// <summary>
        /// Creates a new instance of this BugManager.
        /// </summary>
        /// <param name="configuration">RhinoConfiguration by which to create the BugManager.</param>
        /// <param name="logger">Logger implementation for the BugManager.</param>
        public AzureBugsManager(RhinoConfiguration configuration, ILogger logger)
            : this(configuration.GetVssConnection(), logger)
        {
            this.configuration = configuration;
        }

        /// <summary>
        /// Creates a new instance of this BugManager.
        /// </summary>
        /// <param name="connection"><see cref="VssConnection"/> by which to factor Azure clients.</param>
        public AzureBugsManager(VssConnection connection)
            : this(connection, logger: default)
        { }

        /// <summary>
        /// Creates a new instance of this BugManager.
        /// </summary>
        /// <param name="connection"><see cref="VssConnection"/> by which to factor Azure clients.</param>
        /// <param name="logger">Logger implementation for the BugManager.</param>
        public AzureBugsManager(VssConnection connection, ILogger logger)
        {
            // setup
            this.connection = connection;
            this.logger = logger != default ? logger.CreateChildLogger(nameof(AzureBugsManager)) : logger;

            // clients
            itemManagement = connection.GetClient<WorkItemTrackingHttpClient>(GlobalSettings.ClientNumberOfAttempts);

            // logger
            logger?.Debug($"Create-BugManager -Connection {connection.Uri} = Created");
        }
        #endregion

        #region *** Get          ***
        /// <summary>
        /// Gets a list of open bugs.
        /// </summary>
        /// <param name="testCase">RhinoTestCase by which to find bugs.</param>
        /// <returns>A list of bugs (can be JSON or ID for instance).</returns>
        public IEnumerable<string> GetBugs(RhinoTestCase testCase)
        {
            return itemManagement.GetBugs(testCase).Select(i => JsonSerializer.Serialize(i));
        }

        /// <summary>
        /// Asserts if the RhinoTestCase has already an open bug.
        /// </summary>
        /// <param name="testCase">RhinoTestCase by which to assert against match bugs.</param>
        /// <returns>An open bug.</returns>
        public string GetOpenBug(RhinoTestCase testCase)
        {
            // setup
            var bugs = testCase.GetOpenBugs(connection);

            // exit conditions
            if (!bugs.Any())
            {
                return string.Empty;
            }

            // get
            return JsonSerializer.Serialize(bugs.First());
        }
        #endregion

        #region *** Create       ***
        /// <summary>
        /// Creates a new bug under the specified automation provider.
        /// </summary>
        /// <param name="testCase">Rhino.Api.Contracts.AutomationProvider.RhinoTestCase by which to create automation provider bug.</param>
        /// <returns>The ID of the newly created entity.</returns>
        public string OnCreateBug(RhinoTestCase testCase)
        {
            // exit conditions
            if (testCase.Actual)
            {
                return string.Empty;
            }

            // build
            var response = testCase.CreateBug(connection);

            // get
            return response == default ? "-1" : response.Url;
        }
        #endregion

        #region *** Update       ***
        /// <summary>
        /// Updates an existing bug (partial updates are supported, i.e. you can submit and update specific fields only).
        /// </summary>
        /// <param name="testCase">RhinoTestCase by which to update automation provider bug.</param>
        public string OnUpdateBug(RhinoTestCase testCase)
        {
            // setup
            var testRun = testCase.GetTestRun(connection);
            var openBugs = testCase
                .GetOpenBugs(connection)
                .Where(i => testCase.IsBugMatch(bug: i, assertDataSource: false))
                .ToArray();

            // exit conditions
            if (openBugs.Length == 0)
            {
                return string.Empty;
            }

            // setup
            var openBug = openBugs[0];

            // find duplicates
            if (openBugs.Length > 1)
            {
                SetDuplicates(openBugs, testRun);
            }

            // update Bug
            var bug = testCase.UpdateBug(openBug, connection);

            // get
            return bug == default ? string.Empty : JsonSerializer.Serialize(bug);
        }
        #endregion

        #region *** Close        ***
        /// <summary>
        /// Close all existing bugs.
        /// </summary>
        /// <param name="testCase">Rhino.Api.Contracts.AutomationProvider.RhinoTestCase by which to close automation provider bugs.</param>
        public IEnumerable<string> OnCloseBugs(RhinoTestCase testCase)
        {
            // force teardown
            testCase.Context["Phase"] = "Teardown";

            // setup
            var testRun = testCase.GetTestRun(connection);

            // exit conditions
            if (testRun == null)
            {
                logger?.Warn($"Close-Bugs -Test {testCase.Key} -Run -1 = (NotFound | NotSupported)");
                return Array.Empty<string>();
            }

            // setup
            var project = testCase.GetProjectName();
            var testCaseResults = testRun.GetTestRunResults(connection).Where(i => i.TestCase.Id.Equals(testCase.Key));

            // duplicates
            var openBugs = testCase.GetOpenBugs(connection);
            SetDuplicates(openBugs, testRun);

            // exit conditions
            if (!openBugs.Any())
            {
                return Array.Empty<string>();
            }

            // setup
            var isAllClosed = CloseBugs(openBugs, testRun);
            var bugsClosed = isAllClosed ? openBugs.Select(i => i.Url) : Array.Empty<string>();

            // add test results
            if (!testCaseResults.Any())
            {
                return bugsClosed;
            }

            // update
            foreach (var testCaseResult in testCaseResults)
            {
                testCaseResult.AssociatedBugs ??= new List<ShallowReference>();
                testCaseResult.AssociatedBugs.AddRange(openBugs.Select(i => i.GetTestReference()));
            }
            connection
                .GetClient<TestManagementHttpClient>(GlobalSettings.ClientNumberOfAttempts)
                .UpdateTestResultsAsync(testCaseResults.ToArray(), project, testRun.Id)
                .GetAwaiter()
                .GetResult();

            // set context
            testCase.Context[ContextEntry.BugClosed] = bugsClosed;

            // close bugs
            return bugsClosed;
        }

        private string GetCloseState()
        {
            // setup
            var fromConfiguration = configuration.GetAzureCapability("bugCloseState", string.Empty);
            var fromList = GetStatesByCategory("Completed").FirstOrDefault();

            // get
            if (!string.IsNullOrEmpty(fromConfiguration))
            {
                return fromConfiguration;
            }
            return string.IsNullOrEmpty(fromList) ? string.Empty : fromList;
        }

        private bool CloseBugs(IEnumerable<WorkItem> bugs, TestRun testRun)
        {
            // constants
            const string closeReason = "Fixed and verified";

            // setup
            var closeState = GetCloseState();
            var results = new List<bool>();
            var comment =
                "Automatically closed by Rhino engine on " +
                $"execution <a href=\"{testRun.WebAccessUrl}\">{testRun.Id}</a>.";

            // close
            foreach (var bug in bugs)
            {
                var result = bug.SetState(connection, closeState, closeReason);
                itemManagement.AddComment(bug, comment);
                results.Add(result);
            }

            // get
            return results.All(i => i);
        }
        #endregion

        // Utilities
        private void SetDuplicates(IEnumerable<WorkItem> bugs, TestRun testRun)
        {
            // exit conditions
            if (bugs?.Any() == false)
            {
                return;
            }
            // constants
            const string reason = "Duplicate";

            // setup
            var state = GetStatesByCategory("Resolved");
            var comment =
                "Automatically marked as duplicate by Rhino engine on " +
                $"execution <a href=\"{testRun.WebAccessUrl}\">{testRun.Id}</a>.";
            bugs = bugs.Where(i => !(state.Contains($"{i.Fields["System.State"]}") && $"{i.Fields["System.Reason"]}".Equals(reason)));

            // invoke
            foreach (var bug in bugs.Skip(1))
            {
                var stateResult = bug.SetState(connection, state.FirstOrDefault(), reason) ? "OK" : "InternalServerError";
                itemManagement.AddComment(bug, comment);
                logger?.Debug($"Set-Duplicates -Bug {bug.Id} = {stateResult}");
            }
        }

        private IEnumerable<string> GetStatesByCategory(string category) => itemManagement
            .GetWorkItemTypeStatesAsync(configuration.ConnectorConfiguration.Project, "Bug")
            .GetAwaiter()
            .GetResult()
            .Where(i => i.Category.Equals(category, Compare))
            .Select(i => i.Name);
    }
}
