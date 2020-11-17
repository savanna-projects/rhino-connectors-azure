﻿/*
 * CHANGE LOG - keep only last 5 threads
 * 
 * RESOURCES
 */
using Gravity.Extensions;

using HtmlAgilityPack;

using Microsoft.TeamFoundation.TestManagement.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;

using Newtonsoft.Json;

using Rhino.Api.Contracts.AutomationProvider;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;

namespace Rhino.Connectors.Azure.Extensions
{
    /// <summary>
    /// Extension package for Azure Clients.
    /// </summary>
    internal static class AzureExtensions
    {
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
            return DoGetField(item, field, defaultValue);
        }

        /// <summary>
        /// Gets all test suites associated with a work item.
        /// </summary>
        /// <param name="id"><see cref="WorkItem.Id"/> to find by.</param>
        /// <param name="client">Client to perfrom search with.</param>
        /// <returns>A collection of TestSuite IDs.</returns>
        public static IEnumerable<int> FindTestSuites(
            this Microsoft.VisualStudio.Services.TestManagement.TestPlanning.WebApi.TestPlanHttpClient client,
            int id)
        {
            return DoFindTestSuites(client, id);
        }

        /// <summary>
        /// Add test cases to suite.
        /// </summary>
        /// <param name="client"><see cref="WorkItemTrackingHttpClient"/> client by which to find test suites.</param>
        /// <param name="suiteId">ID of the test suite to find..</param>
        public static int GetPlanForSuite(
            this Microsoft.VisualStudio.Services.TestManagement.TestPlanning.WebApi.TestPlanHttpClient client,
            string project,
            int suiteId)
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

        #region *** Get Test Case ***
        /// <summary>
        /// Gets a RhinoTestCase based on <see cref="WorkItem"/> object.
        /// </summary>
        /// <param name="client"><see cref="WorkItemTrackingHttpClient"/> to fetch data by.</param>
        /// <param name="id">The <see cref="WorkItem.Id"/> to create by.</param>
        /// <returns>RhinoTestCase object.</returns>
        public static RhinoTestCase GetRhinoTestCase(this WorkItemTrackingHttpClient client, int id)
        {
            return DoGetRhinoTestCases(client, new[] { id }).FirstOrDefault();
        }

        /// <summary>
        /// Gets a collection of RhinoTestCase based on <see cref="WorkItem"/> objects.
        /// </summary>
        /// <param name="client"><see cref="WorkItemTrackingHttpClient"/> to fetch data by.</param>
        /// <param name="ids">A collection of <see cref="WorkItem.Id"/> to create by.</param>
        /// <returns>A collection RhinoTestCase object.</returns>
        public static IEnumerable<RhinoTestCase> GetRhinoTestCases(this WorkItemTrackingHttpClient client, IEnumerable<int> ids)
        {
            return DoGetRhinoTestCases(client, ids);
        }

        private static IEnumerable<RhinoTestCase> DoGetRhinoTestCases(WorkItemTrackingHttpClient client, IEnumerable<int> ids)
        {
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
                var stepsHtml = DoGetField(item, "Microsoft.VSTS.TCM.Steps", string.Empty).DecodeHtml();
                var stepsDocument = new HtmlDocument();

                // load
                stepsDocument.LoadHtml(stepsHtml);

                // put
                var testCase = new RhinoTestCase
                {
                    Key = $"{item.Id}"
                };
                testCase.Scenario = DoGetField(item, "System.Title", string.Empty);
                testCase.Priority = $"{DoGetField<long>(item, "Microsoft.VSTS.Common.Priority", 2)}";
                testCase.Steps = DoGetSteps(client, stepsDocument.DocumentNode.SelectNodes("//steps/*"));
                testCase.TotalSteps = testCase.Steps.Count();
                testCase.DataSource = DoGetDataSource(item);
                testCase.Context["workItem"] = item;

                // put
                testCases.Add(testCase);
            }

            // get
            return testCases;
        }

        private static IEnumerable<RhinoTestStep> DoGetSteps(WorkItemTrackingHttpClient client, HtmlNodeCollection nodes)
        {
            // setup
            var nodesQueue = new ConcurrentStack<HtmlNode>();
            var testSteps = new ConcurrentBag<RhinoTestStep>();

            // load initial
            nodesQueue.PushRange(nodes);

            // iterate
            while (nodesQueue.Count > 0)
            {
                var testStep = DoGetStep(client, nodesQueue);
                if(testStep == default)
                {
                    continue;
                }
                testSteps.Add(testStep);
            }

            // get
            return testSteps;
        }

        private static RhinoTestStep DoGetStep(WorkItemTrackingHttpClient client, ConcurrentStack<HtmlNode> nodes)
        {
            // setup > dequeue next
            nodes.TryPop(out HtmlNode stepOut);

            // process step
            if (stepOut.Name.Equals("step", StringComparison.OrdinalIgnoreCase))
            {
                return DoGetStep(step: stepOut);
            }

            // process shared steps
            var shared = client.GetWorkItemAsync(int.Parse(stepOut.GetAttributeValue("ref", "0"))).GetAwaiter().GetResult();
            var doc = new HtmlDocument();
            doc.LoadHtml(shared.GetField("Microsoft.VSTS.TCM.Steps", string.Empty).DecodeHtml());

            // setup > enqueue next
            var range = doc.DocumentNode.SelectNodes(".//steps/step").Concat(stepOut.SelectNodes("./*"));
            nodes.PushRange(range);

            // default
            return default;
        }

        private static RhinoTestStep DoGetStep(HtmlNode step)
        {
            // setups
            var onStep = step.SelectNodes(".//parameterizedstring");

            // build
            var rhinoStep = new RhinoTestStep
            {
                Action = onStep[0].InnerText,
                Expected = onStep[1].InnerText
            };
            rhinoStep.Context["runtime"] = step.GetAttributeValue("id", "-1");
            rhinoStep.Context["azureStep"] = step;

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

        #region *** Utilities     ***
        // gets a filed from work item
        private static T DoGetField<T>(WorkItem item, string field, T defaultValue)
        {
            // exit conditions
            if (!item.Fields.ContainsKey(field))
            {
                return defaultValue;
            }

            // get
            try
            {
                return (T)item.Fields[field];
            }
            catch (Exception e) when (e != null)
            {
                return defaultValue;
            }
        }

        // gets test suites collection from work item
        private static IEnumerable<int> DoFindTestSuites(
            this Microsoft.VisualStudio.Services.TestManagement.TestPlanning.WebApi.TestPlanHttpClient client,
            int id)
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
