/*
 * CHANGE LOG - keep only last 5 threads
 * 
 * RESOURCES
 */
using Gravity.Extensions;

using HtmlAgilityPack;

using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.TestManagement.TestPlanning.WebApi;

using Rhino.Api.Contracts.AutomationProvider;
using Rhino.Connectors.Azure.Contracts;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;

using WorkItem = Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models.WorkItem;

namespace Rhino.Connectors.Azure.Extensions
{
    /// <summary>
    /// Extension package for Azure Clients.
    /// </summary>
    internal static class AzureExtensions
    {
        // constants
        private const SuiteEntryTypes TestEntryType = SuiteEntryTypes.TestCase;

        #region *** Work Item Object      ***
        /// <summary>
        /// Gets a field from the WorkItem.Fields collection or default value if failed.
        /// </summary>
        /// <typeparam name="T">Type of field to return.</typeparam>
        /// <param name="item">WorkItem to get field from.</param>
        /// <param name="field">Field to extract.</param>
        /// <param name="defaultValue">Default value if failed to get fields.</param>
        /// <returns>Value.</returns>
        public static T GetField<T>(this WorkItem item, string field, T defaultValue)
        {
            return item.Fields.GetCastedValueOrDefault(key: field, @default: defaultValue);
        }
        #endregion

        #region *** Test Plan Http Client ***
        /// <summary>
        /// Gets all test suites associated with a <see cref="WorkItem"/>.
        /// </summary>
        /// <param name="client"><see cref="TestPlanHttpClient"/> to use for perfroming search.</param>
        /// <param name="id"><see cref="WorkItem.Id"/> for which to find test suites.</param>
        /// <returns>A collection of <see cref="TestSuite.Id"/>.</returns>
        public static IEnumerable<int> FindTestSuites(this TestPlanHttpClient client, int id)
        {
            return DoFindTestSuites(client, id);
        }

        /// <summary>
        /// Gets <see cref="TestPlan.Id"/> for the provided <see cref="TestSuite.Id"/>.
        /// </summary>
        /// <param name="client"><see cref="TestPlanHttpClient"/> to use for perfroming search.</param>
        /// <param name="suiteId"><see cref="TestSuite.Id"/> by which to get <see cref="TestPlan.Id"/>.</param>
        /// <returns><see cref="TestPlan.Id"/> or 0 if not found.</returns>
        public static int GetPlanForSuite(this TestPlanHttpClient client, string project, int suiteId)
        {
            try
            {
                // setup
                var plans = client.GetTestPlansWithContinuationTokenAsync(project).GetAwaiter().GetResult();
                var suites = plans.SelectMany(i => client.GetTestSuitesForPlanWithContinuationTokenAsync(project, i.Id).Result);

                // get
                var plan = suites.FirstOrDefault(i => i.Id == suiteId)?.Plan;
                return plan == default ? 0 : plan.Id;
            }
            catch (Exception e) when (e != null)
            {
                return 0;
            }
        }

        /// <summary>
        /// Gets the first parent <see cref="TestPlan.Id"/> for the provided <see cref="TestCase"/>.
        /// </summary>
        /// <param name="client"><see cref="TestPlanHttpClient"/> to use for perfroming search.</param>
        /// <param name="testId"><see cref="TestCase"/> by which to find <see cref="TestPlan.Id"/>.</param>
        /// <returns><see cref="TestPlan.Id"/> or 0 if not found.</returns>
        public static int GetPlanForTest(this TestPlanHttpClient client, string project, int testId)
        {
            try
            {
                // setup
                var plans = client.GetTestPlansWithContinuationTokenAsync(project).GetAwaiter().GetResult();
                var suites = plans.SelectMany(i => client.GetTestSuitesForPlanWithContinuationTokenAsync(project, i.Id).Result);

                var filteredSuites = suites.Where(i => client
                    .GetSuiteEntriesAsync(project, i.Id, TestEntryType)
                    .GetAwaiter()
                    .GetResult()
                    .Any(i => i.SuiteEntryType == TestEntryType && i.Id == testId))
                    .ToList();

                // get
                var plan = filteredSuites.FirstOrDefault()?.Plan.Id;
                return plan == default ? 0 : plan.ToInt();
            }
            catch (Exception e) when (e != null)
            {
                return 0;
            }
        }
        #endregion

        #region *** Work Item HTTP Client ***
        /// <summary>
        /// Gets a RhinoTestCase based on <see cref="WorkItem.Id"/>.
        /// </summary>
        /// <param name="client"><see cref="WorkItemTrackingHttpClient"/> to fetch data by.</param>
        /// <param name="id">The <see cref="WorkItem.Id"/> to create by.</param>
        /// <returns>RhinoTestCase object.</returns>
        public static RhinoTestCase GetRhinoTestCase(this WorkItemTrackingHttpClient client, int id)
        {
            return DoGetRhinoTestCases(client, new[] { id }).FirstOrDefault();
        }

        /// <summary>
        /// Gets a collection of RhinoTestCase based on a collection of <see cref="WorkItem.Id"/>.
        /// </summary>
        /// <param name="client"><see cref="WorkItemTrackingHttpClient"/> to fetch data by.</param>
        /// <param name="ids">A collection of <see cref="WorkItem.Id"/> to create by.</param>
        /// <returns>A collection RhinoTestCase objects.</returns>
        public static IEnumerable<RhinoTestCase> GetRhinoTestCases(this WorkItemTrackingHttpClient client, IEnumerable<int> ids)
        {
            return DoGetRhinoTestCases(client, ids);
        }

        private static IEnumerable<RhinoTestCase> DoGetRhinoTestCases(WorkItemTrackingHttpClient client, IEnumerable<int> ids)
        {
            // exit conditions
            if (!ids.Any())
            {
                return Array.Empty<RhinoTestCase>();
            }

            // setup
            var items = client
                .GetWorkItemsAsync(ids, fields: null, asOf: null, expand: WorkItemExpand.All)
                .GetAwaiter()
                .GetResult();
            var testCases = new ConcurrentBag<RhinoTestCase>();

            // iterate
            foreach (var item in items)
            {
                // exit conditions
                var stepsHtml = item.Fields.GetCastedValueOrDefault("Microsoft.VSTS.TCM.Steps", string.Empty).DecodeHtml();
                var stepsDocument = new HtmlDocument();

                // load
                stepsDocument.LoadHtml(stepsHtml);

                // put
                var testCase = new RhinoTestCase
                {
                    Key = $"{item.Id}"
                };
                testCase.Scenario = item.Fields.GetCastedValueOrDefault("System.Title", string.Empty);
                testCase.Priority = $"{item.Fields.GetCastedValueOrDefault("Microsoft.VSTS.Common.Priority", 2L)}";
                testCase.Steps = DoGetSteps(client, stepsDocument.DocumentNode.SelectNodes("//steps/*"));
                testCase.TotalSteps = testCase.Steps.Count();
                testCase.DataSource = DoGetDataSource(item);
                testCase.Context[AzureContextEntry.WorkItem] = item;

                foreach (var testStep in testCase.Steps)
                {
                    testStep.Context[AzureContextEntry.WorkItem] = item;
                    testStep.Context[AzureContextEntry.IterationDetails] = testCase.Iteration + 1;
                }

                // put
                testCases.Add(testCase);
            }

            // get
            return testCases;
        }

        private static IEnumerable<RhinoTestStep> DoGetSteps(WorkItemTrackingHttpClient client, HtmlNodeCollection nodes)
        {
            // setup
            var nodesQueue = new ConcurrentStack<(Dictionary<string, object> Context, HtmlNode Node)>();
            var testSteps = new ConcurrentBag<RhinoTestStep>();

            // load initial
            nodesQueue.PushRange(nodes.Select(i => (new Dictionary<string, object>(), i)));

            // iterate
            while (!nodesQueue.IsEmpty)
            {
                var testStep = DoGetStep(client, nodesQueue);
                if (testStep == default)
                {
                    continue;
                }
                testSteps.Add(testStep);
            }

            // get
            return testSteps;
        }

        private static RhinoTestStep DoGetStep(
            WorkItemTrackingHttpClient client,
            ConcurrentStack<(Dictionary<string, object> Context, HtmlNode Node)> nodes)
        {
            // setup > dequeue next
            nodes.TryPop(out (Dictionary<string, object> Context, HtmlNode Node) stepOut);

            // process step
            if (stepOut.Node.Name.Equals("step", StringComparison.OrdinalIgnoreCase))
            {
                return DoGetStep(step: stepOut);
            }

            // process shared steps
            var shared = client.GetWorkItemAsync(int.Parse(stepOut.Node.GetAttributeValue("ref", "0"))).GetAwaiter().GetResult();
            var doc = new HtmlDocument();
            doc.LoadHtml(shared.GetField("Microsoft.VSTS.TCM.Steps", string.Empty).DecodeHtml());

            // setup > enqueue next
            var sharedStepAction = stepOut.Node.GetAttributeValue("id", "0");
            var range = new List<(Dictionary<string, object>, HtmlNode)>();
            foreach (var node in doc.DocumentNode.SelectNodes(".//steps/step"))
            {
                var runtime = node.GetAttributeValue("id", "0");
                var path =
                    new string('0', 8 - sharedStepAction.Length) + sharedStepAction +
                    new string('0', 8 - runtime.Length) + runtime;

                var context = new Dictionary<string, object>
                {
                    [AzureContextEntry.SharedStepId] = shared.Id.ToInt(),
                    [AzureContextEntry.SharedStep] = shared,
                    [AzureContextEntry.SharedStepAction] = sharedStepAction,
                    [AzureContextEntry.SharedStepPath] = $"{new string('0', 8 - sharedStepAction.Length) + sharedStepAction}",
                    [AzureContextEntry.SharedStepIdentifier] = $"{sharedStepAction};{runtime}",
                    [AzureContextEntry.SharedStepActionPath] = path
                };
                range.Add((context, node));
            }
            foreach (var node in stepOut.Node.SelectNodes("./*"))
            {
                range.Add((new Dictionary<string, object>(), node));
            }
            nodes.PushRange(range);

            // default
            return default;
        }

        private static RhinoTestStep DoGetStep((Dictionary<string, object> Context, HtmlNode Node) step)
        {
            // setups
            var onStep = step.Node.SelectNodes(".//parameterizedstring");

            // build
            var rhinoStep = new RhinoTestStep
            {
                Action = onStep[0].InnerText,
                Expected = onStep[1].InnerText
            };

            // setup
            var id = step.Node.GetAttributeValue("id", "-1");

            // context
            rhinoStep.Context[AzureContextEntry.StepRuntime] = id;
            rhinoStep.Context[AzureContextEntry.Step] = step;
            rhinoStep.Context[AzureContextEntry.ActionPath] = new string('0', 8 - id.Length) + id;

            foreach (var item in step.Context)
            {
                rhinoStep.Context[item.Key] = item.Value;
            }

            // put
            return rhinoStep;
        }

        private static IEnumerable<IDictionary<string, object>> DoGetDataSource(WorkItem item)
        {
            // setup
            var xsd = item
                .Fields
                .GetCastedValueOrDefault("Microsoft.VSTS.TCM.LocalDataSource", string.Empty)
                .Replace(" encoding=\"utf-16\"", string.Empty);

            // exit conditions
            if (string.IsNullOrEmpty(xsd))
            {
                return Array.Empty<IDictionary<string, object>>();
            }

            // setup
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(xsd));

            // read
            var dataSet = new DataSet();
            dataSet.ReadXml(stream);

            // clean
            stream.Dispose();

            // extract
            var dataTable = dataSet.Tables.Count > 0 ? dataSet.Tables[0] : new DataTable();

            // get
            return dataTable.ToDictionary();
        }
        #endregion

        #region *** Utilities             ***
        // gets test suites collection from work item
        private static IEnumerable<int> DoFindTestSuites(this TestPlanHttpClient client, int id)
        {
            try
            {
                return client.GetSuitesByTestCaseIdAsync(id).GetAwaiter().GetResult().Select(i => i.Id);
            }
            catch (Exception e) when (e != null)
            {
                return Array.Empty<int>();
            }
        }
        #endregion
    }
}