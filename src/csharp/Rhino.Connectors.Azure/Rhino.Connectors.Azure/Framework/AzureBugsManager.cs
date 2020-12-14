/*
 * CHANGE LOG - keep only last 5 threads
 * 
 * RESOURCES
 */
using Gravity.Abstraction.Logging;

using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.WebApi;

using Rhino.Api.Contracts.AutomationProvider;
using Rhino.Connectors.Azure.Extensions;

using System;
using System.Collections.Generic;
using System.Linq;

namespace Rhino.Connectors.Azure.Framework
{
    /// <summary>
    /// Bugs manager component common to all Jira connectors.
    /// </summary>
    public class AzureBugsManager
    {
        // members: state
        private readonly VssConnection connection;
        private readonly ILogger logger;

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
            return DoGetBugs(testCase).Select(i => $"{i}");
        }

        /// <summary>
        /// Asserts if the RhinoTestCase has already an open bug.
        /// </summary>
        /// <param name="testCase">RhinoTestCase by which to assert against match bugs.</param>
        /// <returns>An open bug.</returns>
        public string GetOpenBug(RhinoTestCase testCase)
        {
            // setup
            var bugs = DoGetBugs(testCase);

            // get
            var openBug = bugs.Where(i => testCase.IsBugMatch(bug: i, assertDataSource: false));

            // assert
            var onBug = openBug.FirstOrDefault();

            // get
            return GetOpenBug(testCase, $"{onBug?.Id}");
        }

        private string GetOpenBug(RhinoTestCase testCase, string id)
        {
            throw new NotImplementedException();
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

            // create bug
            return DoCreateBug(testCase);
        }

        private string DoCreateBug(RhinoTestCase testCase)
        {
            // get bug response
            var response = testCase.CreateBug(connection);

            // results
            return response == default ? "-1" : response.Url;
        }
        #endregion

        #region *** Update       ***
        /// <summary>
        /// Updates an existing bug (partial updates are supported, i.e. you can submit and update specific fields only).
        /// </summary>
        /// <param name="testCase">Rhino.Api.Contracts.AutomationProvider.RhinoTestCase by which to update automation provider bug.</param>
        public string OnUpdateBug(RhinoTestCase testCase, string status, string resolution)
        {
            // get existing bugs
            var isBugs = testCase.Context.ContainsKey("bugs") && testCase.Context["bugs"] != default;
            var bugs = isBugs ? (IEnumerable<string>)testCase.Context["bugs"] : Array.Empty<string>();

            // exit conditions
            if (bugs.All(i => string.IsNullOrEmpty(i)))
            {
                return "-1";
            }

            // possible duplicates
            if (bugs.Count() > 1)
            {
                // TODO: implement
            }

            // update
            bugs = Array.Empty<string>();

            testCase.UpdateBug(id: bugs.FirstOrDefault(), connection);

            // get
            return $"{"bug url"}";
        }
        #endregion

        #region *** Update       ***
        /// <summary>
        /// Close all existing bugs.
        /// </summary>
        /// <param name="testCase">Rhino.Api.Contracts.AutomationProvider.RhinoTestCase by which to close automation provider bugs.</param>
        public IEnumerable<string> OnCloseBugs(RhinoTestCase testCase, string status, string resolution)
        {
            // get existing bugs
            var bugs = DoGetBugs(testCase)
                .Select(i => $"{i.Id.ToInt()}")
                .Where(i => !string.IsNullOrEmpty(i) && i != "0");

            // close bugs
            return DoCloseBugs(testCase, status, resolution, Array.Empty<string>(), bugs);
        }

        /// <summary>
        /// Close all existing bugs.
        /// </summary>
        /// <param name="testCase">Rhino.Api.Contracts.AutomationProvider.RhinoTestCase by which to close automation provider bugs.</param>
        public IEnumerable<string> OnCloseBugs(RhinoTestCase testCase, string status, string resolution, IEnumerable<string> bugs)
        {
            // set existing bugs
            testCase.Context["bugs"] = bugs;

            // close bugs
            return DoCloseBugs(testCase, status, resolution, Array.Empty<string>(), bugs);
        }

        /// <summary>
        /// Close all existing bugs.
        /// </summary>
        /// <param name="testCase">Rhino.Api.Contracts.AutomationProvider.RhinoTestCase by which to close automation provider bugs.</param>
        public string OnCloseBug(RhinoTestCase testCase, string status, string resolution)
        {
            // get existing bugs
            var isBugs = testCase.Context.ContainsKey("bugs") && testCase.Context["bugs"] != default;
            var contextBugs = isBugs ? (IEnumerable<string>)testCase.Context["bugs"] : Array.Empty<string>();
            var bugs = Array.Empty<WorkItem>();

            // get conditions (double check for bugs)
            if (!bugs.Any())
            {
                return string.Empty;
            }

            // close bugs: first
            var onBug = $"{bugs.FirstOrDefault()?.Id.ToInt()}";
            testCase.CloseBug(id: onBug, connection);

            // close bugs: duplicate (if any)
            foreach (var bug in bugs.Skip(1))
            {
                var labels = new[] { "Duplicate" };
                testCase.CloseBug($"{bug.Id.ToInt()}", connection);
            }
            return onBug;
        }

        private IEnumerable<string> DoCloseBugs(RhinoTestCase testCase, string status, string resolution, IEnumerable<string> labels, IEnumerable<string> bugs)
        {
            // close bugs
            var closedBugs = new List<string>();
            foreach (var bug in bugs)
            {
                var isClosed = testCase.CloseBug(bug, connection);

                // logs
                if (isClosed)
                {
                    closedBugs.Add($"{"bug url"}");
                    continue;
                }
                logger?.Info($"Close-Bug -Bug [{bug}] -Test [{testCase.Key}] = false");
            }

            // context
            if (!testCase.Context.ContainsKey(ContextEntry.BugClosed) || !(testCase.Context[ContextEntry.BugClosed] is IEnumerable<string>))
            {
                testCase.Context[ContextEntry.BugClosed] = new List<string>();
            }
            var onBugsClosed = (testCase.Context[ContextEntry.BugClosed] as IEnumerable<string>).ToList();
            onBugsClosed.AddRange(closedBugs);
            testCase.Context[ContextEntry.BugClosed] = onBugsClosed;

            // get
            return onBugsClosed;
        }
        #endregion

        // Utilities
        private IEnumerable<WorkItem> DoGetBugs(RhinoTestCase testCase)
        {
            throw new NotImplementedException();
        }
    }
}
