/*
 * CHANGE LOG - keep only last 5 threads
 * 
 * RESOURCES
 */
using Gravity.Abstraction.Logging;

using Microsoft.TeamFoundation.TestManagement.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.VisualStudio.Services.WebApi;

using Rhino.Api.Contracts.AutomationProvider;
using Rhino.Connectors.Azure.Extensions;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Rhino.Connectors.Azure.Framework
{
    /// <summary>
    /// Bugs manager component common to all Jira connectors.
    /// </summary>
    public class AzureBugsManager
    {
        // members: clients
        private readonly WorkItemTrackingHttpClient itemManagement;

        // members: state        
        private readonly ILogger logger;
        private readonly VssConnection connection;

        #region *** Constructors ***
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
        /// <param name="logger">Logger implementation for this JiraClient</param>
        public AzureBugsManager(VssConnection connection, ILogger logger)
        {
            // setup
            this.connection = connection;
            this.logger = logger != default ? logger.CreateChildLogger(nameof(AzureBugsManager)) : logger;

            // clients
            itemManagement = connection.GetClient<WorkItemTrackingHttpClient>();

            // logger
            logger?.Debug($"Create-BugManager -Connection {connection.Uri} = OK");
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
            var openBugs = testCase.GetOpenBugs(connection).ToArray();

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
                var duplicatesClosed = openBugs
                    .Skip(1)
                    .Select(i => i.SetState(connection, "Resolved", "Duplicate"))
                    .All(i => i);
                logger?.Info($"Update-Bug -Duplicates = {duplicatesClosed}");
            }

            // update Bug
            var bug = testCase.UpdateBug(openBug, connection);

            // get
            return JsonSerializer.Serialize(bug);
        }
        #endregion

        #region *** Close        ***
        /// <summary>
        /// Close all existing bugs.
        /// </summary>
        /// <param name="testCase">Rhino.Api.Contracts.AutomationProvider.RhinoTestCase by which to close automation provider bugs.</param>
        public IEnumerable<string> OnCloseBugs(RhinoTestCase testCase, string status, string resolution)
        {
            // setup
            var testRun = testCase.GetTestRun(connection);
            var testCaseResults = testRun.GetTestRunResults(connection);
            var testCaseResult = testCaseResults.FirstOrDefault(i => i.TestCase.Id.Equals(testCase.Key));
            var comment = $"Automatically updated by Rhino engine on execution <a href=\"{testRun.WebAccessUrl}\">{testCase.TestRunKey}</a>.";
            var project = testCase.GetProjectName();

            // duplicates
            var matchingBugs = testCase.GetOpenBugs(connection);
            if (matchingBugs.Any())
            {
                _ = matchingBugs
                    .Skip(1)
                    .Select(i => itemManagement.AddComment(i, comment))
                    .Select(i => i.SetState(connection, "Resolved", "Duplicate"))
                    .ToArray();
            }

            // open bugs
            var closeStatus = new[] { "Closed", "Resolved" };
            var openBugs = itemManagement
                .GetBugs(testCase)
                .Where(i => !closeStatus.Contains($"{i.Fields["System.State"]}"))
                .Concat(new[] { matchingBugs.FirstOrDefault() })
                .Where(i => i != default)
                .Select(i => itemManagement.AddComment(i, comment));

            // exit conditions
            if (!openBugs.Any())
            {
                return Array.Empty<string>();
            }

            // setup
            var isAll = openBugs.Select(i => i.SetState(connection, status, resolution)).All(i => i);
            var bugsClosed = isAll
                ? openBugs.Concat(matchingBugs.Skip(1)).Select(i => i.Url)
                : Array.Empty<string>();

            // add test results
            if (testCaseResult == default)
            {
                return bugsClosed;
            }

            // update
            testCaseResult.AssociatedBugs ??= new List<ShallowReference>();
            testCaseResult.AssociatedBugs.AddRange(openBugs.Select(i => i.GetTestReference()));
            connection
                .GetClient<TestManagementHttpClient>()
                .UpdateTestResultsAsync(testCaseResults.ToArray(), project, testRun.Id)
                .GetAwaiter()
                .GetResult();

            testCase.Context[ContextEntry.BugClosed] = bugsClosed;

            // close bugs: duplicate (if any)
            return bugsClosed;
        }
        #endregion
    }
}