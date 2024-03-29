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
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;

using Rhino.Api.Contracts.AutomationProvider;
using Rhino.Api.Contracts.Configuration;
using Rhino.Api.Converters;
using Rhino.Api.Extensions;
using Rhino.Connectors.Azure.Contracts;
using Rhino.Connectors.Azure.Framework;

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Rhino.Connectors.Azure.Extensions
{
    /// <summary>
    /// Extension package for Rhino objects.
    /// </summary>
    internal static class RhinoExtensions
    {
        // constants
        private const StringComparison Compare = StringComparison.OrdinalIgnoreCase;
        private static JsonSerializerOptions JsonOptions
        {
            get
            {
                var _options = new JsonSerializerOptions()
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                _options.Converters.Add(new ExceptionConverter());
                return _options;
            }
        }

        #region *** Test: Models   ***
        /// <summary>
        /// Gets a basic <see cref="TestIterationDetailsModel"/> object.
        /// </summary>
        /// <param name="testCase">RhinoTestCase by which to create <see cref="TestIterationDetailsModel"/>.</param>
        /// <returns><see cref="TestIterationDetailsModel"/> object.</returns>
        public static TestIterationDetailsModel ToTestIterationDetails(this RhinoTestCase testCase, bool setOutcome)
        {
            // setup
            var iteration = new TestIterationDetailsModel
            {
                Id = testCase.Iteration + 1,
                StartedDate = DateTime.Now.ToAzureDate(addMilliseconds: true),
                Comment = "Automatically Created by Rhino Engine.",
                ActionResults = GetActionResults(testCase, setOutcome).ToList(),
                Parameters = GetParametersResults(testCase).ToList()
            };

            // outcome
            if (setOutcome)
            {
                iteration.Outcome = testCase.Actual ? nameof(TestOutcome.Passed) : nameof(TestOutcome.Failed);
            }

            // context update
            testCase.Context[AzureContextEntry.IterationDetails] = iteration;

            // get
            return iteration;
        }

        // get TestActionResultModel
        private static IEnumerable<TestActionResultModel> GetActionResults(RhinoTestCase testCase, bool setOutcome)
        {
            // setup
            var steps = testCase.Steps.ToList();
            var actionResults = new List<TestActionResultModel>();

            // build - actions
            for (int i = 0; i < steps.Count; i++)
            {
                var id = steps[i].Context.Get(AzureContextEntry.SharedStepId, -1);
                var isShared = steps[i].Context.ContainsKey(AzureContextEntry.SharedStep);
                var isAdded = isShared && actionResults.Any(i => IsSharedStepModel(i, id));
                var iteration = testCase.Iteration + 1;

                if (!isAdded && isShared)
                {
                    var sharedModel = GetSharedActionResultModel(testStep: steps[i], iteration, setOutcome);
                    actionResults.Add(sharedModel);
                }

                var actionModel = GetActionResultModel(testStep: steps[i], iteration, setOutcome);
                actionResults.Add(actionModel);
            }

            // get
            return RepairSharedSteps(actionResults);
        }

        private static TestActionResultModel GetSharedActionResultModel(RhinoTestStep testStep, int iteration, bool setOutcome)
        {
            // setup
            var id = testStep.Context.Get(AzureContextEntry.SharedStepId, -1);

            // build
            var actionResult = new TestActionResultModel
            {
                ActionPath = testStep.GetActionPath(defaultValue: "-1"),
                StepIdentifier = testStep.GetActionIdentifier("-1"),
                IterationId = iteration,
                SharedStepModel = new SharedStepModel { Id = id, Revision = testStep.GetSharedStepsRevision() },
                StartedDate = DateTime.Now.ToAzureDate(addMilliseconds: false),
                CompletedDate = DateTime.Now.AddMinutes(5).ToAzureDate(addMilliseconds: false),
            };

            // outcome
            if (setOutcome)
            {
                actionResult.Outcome = testStep.Actual ? nameof(TestOutcome.Passed) : nameof(TestOutcome.Failed);
                actionResult.CompletedDate = DateTime.Now.ToAzureDate(addMilliseconds: false);
            }
            actionResult.ErrorMessage = actionResult.Outcome == nameof(TestOutcome.Failed)
                ? testStep.ReasonPhrase
                : string.Empty;

            // get
            return actionResult;
        }

        private static TestActionResultModel GetActionResultModel(RhinoTestStep testStep, int iteration, bool setOutcome)
        {
            // build
            var actionResult = new TestActionResultModel
            {
                ActionPath = testStep.GetActionPath(defaultValue: "-1"),
                StepIdentifier = testStep.GetActionIdentifier("-1"),
                IterationId = iteration,
                StartedDate = DateTime.Now.ToAzureDate(addMilliseconds: false)
            };

            // outcome
            if (setOutcome)
            {
                actionResult.Outcome = testStep.Actual ? nameof(TestOutcome.Passed) : nameof(TestOutcome.Failed);
                actionResult.CompletedDate = DateTime.Now.ToAzureDate(addMilliseconds: false);
                actionResult.DurationInMs = testStep.RunTime.TotalMilliseconds;
            }

            // get
            return actionResult;
        }

        // get TestResultParameterModel
        private static IEnumerable<TestResultParameterModel> GetParametersResults(RhinoTestCase testCase)
        {
            // build - parameters
            var steps = testCase.Steps.ToList();
            var iteration = testCase.Iteration + 1;
            var parameterResults = new List<TestResultParameterModel>();

            // build
            for (int i = 0; i < steps.Count; i++)
            {
                var actionPath = steps[i].GetActionPath(defaultValue: $"{iteration}");
                var identifier = steps[i].GetActionIdentifier($"{iteration}");
                var parameters = GetParameters(steps[i], testCase.DataSource);

                var range = parameters.Select(i => new TestResultParameterModel
                {
                    ActionPath = actionPath,
                    IterationId = iteration,
                    ParameterName = i.Name,
                    Value = i.Value,
                    StepIdentifier = identifier
                });

                parameterResults.AddRange(range);
            }

            // get
            return parameterResults;
        }

        private static IEnumerable<(string Name, string Value)> GetParameters(
            RhinoTestStep step,
            IEnumerable<IDictionary<string, object>> data)
        {
            // setup
            var onContext = JsonSerializer.Serialize(step.Context, JsonOptions);
            var onStep = $"[{JsonSerializer.Serialize(step, JsonOptions)},{onContext}]";
            var parameters = new List<(string Name, string Value)>();

            // iterate
            foreach (var item in data)
            {
                foreach (var key in item.Keys)
                {
                    var isValue = onStep.Contains($"{item[key]}");
                    if (isValue)
                    {
                        parameters.Add((Name: key, Value: $"{item[key]}"));
                    }
                }
            }

            // get
            return parameters;
        }

        // utilities
        private static bool IsSharedStepModel(TestActionResultModel testAction, int id)
        {
            // setup conditions
            var isModel = testAction.SharedStepModel != null;

            // get
            return isModel && testAction.SharedStepModel.Id == id;
        }

        // repair
        private static IEnumerable<TestActionResultModel> RepairSharedSteps(IEnumerable<TestActionResultModel> actionResults)
        {
            // setup
            var all = actionResults.Where(i => i.ActionPath.Length == 8);
            var sharedActions = actionResults.Where(i => i.SharedStepModel != null);

            // exit conditions
            if(!sharedActions.Any())
            {
                return actionResults;
            }

            // build
            foreach (var sharedAction in sharedActions)
            {
                // get
                var steps = actionResults.Where(i => i.ActionPath.StartsWith(sharedAction.ActionPath[..8]));
                var step = steps.FirstOrDefault(i => i.SharedStepModel != null);

                // build
                step.ActionPath = step.ActionPath[..8];
                step.StepIdentifier = step.StepIdentifier.Split(";")[0];

                // get
                all = all.Concat(steps.Where(i => i.SharedStepModel == null));
            }

            // get
            return all;
        }
        #endregion

        #region *** Test: Context  ***
        /// <summary>
        /// Adds an item to RhinoTestCase.Context, replacing existing one if already present.
        /// </summary>
        /// <param name="testCase">RhinoTestCase to add to.</param>
        /// <param name="key">Context key name.</param>
        /// <param name="value">Context value.</param>
        /// <returns>RhinoTestCase self reference.</returns>
        public static RhinoTestCase AddToContext(this RhinoTestCase testCase, string key, object value)
        {
            // put
            try
            {
                testCase.Context[key] = value;
            }
            catch (Exception e) when (e != null)
            {
                // ignore exceptions
            }

            // get
            return testCase;
        }
        #endregion

        #region *** Test Case      ***
        /// <summary>
        /// Gets the RhinoTestCase.DataSource as XML string.
        /// </summary>
        /// <param name="testCase">RhinoTestCase to get by.</param>
        /// <returns>XML string.</returns>
        public static string GetDataSourceXml(this RhinoTestCase testCase)
        {
            // exit conditions
            if (testCase.DataSource?.Any() == false)
            {
                return string.Empty;
            }

            // setup
            var dataTable = testCase.DataSource.ToDataTable();
            var dataSet = new DataSet();

            // add table
            dataSet.Tables.Add(dataTable);

            // write
            using var stream = new MemoryStream();
            dataSet.WriteXml(stream);

            // get
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        #endregion

        #region *** Action: Images ***
        /// <summary>
        /// Get a collection of Attachment objects, ready to be uploaded.
        /// </summary>
        /// <param name="testStep">RhinoTestStep to build by.</param>
        /// <returns>A collection of <see cref="AttachmentReference"/> after uploaded.</returns>
        public static IEnumerable<TestAttachmentRequestModel> GetAttachments(this RhinoTestStep testStep)
        {
            // build
            var models = testStep
                .GetScreenshots()
                .Select(i => testStep.GetAttachment(filePath: i))
                .Select(i => i.GetAttachmentRequestModel());

            // context
            testStep.Context[AzureContextEntry.StepAttachments] = models;

            // get
            return models;
        }

        /// <summary>
        /// Gets a collection of <see cref="TestAttachmentRequestModel"/> for RhinoTestStep.
        /// </summary>
        /// <param name="testStep">RhinoTestStep from which to get a collection of <see cref="TestAttachmentRequestModel"/></param>
        /// <returns>A collection of <see cref="TestAttachmentRequestModel"/></returns>
        public static IEnumerable<TestAttachmentRequestModel> GetAttachmentModels(this RhinoTestStep testStep)
        {
            // setup conditions
            var isKey = testStep.Context.ContainsKey(AzureContextEntry.StepAttachments);
            var isType = isKey && testStep.Context[AzureContextEntry.StepAttachments] is IEnumerable<TestAttachmentRequestModel>;

            // exit conditions
            if (!isType)
            {
                return Array.Empty<TestAttachmentRequestModel>();
            }

            // setup
            return testStep.Context[AzureContextEntry.StepAttachments] as IEnumerable<TestAttachmentRequestModel>;
        }
        #endregion

        #region *** Test: Bugs     ***
        /// <summary>
        /// Return true if a bug meta data match to test meta data.
        /// </summary>
        /// <param name="testCase">RhinoTestCase to match to.</param>
        /// <param name="bug">Bug <see cref="WorkItem"/> to match by.</param>
        /// <param name="assertDataSource"><see cref="true"/> to match also RhinoTestCase.DataSource</param>
        /// <returns><see cref="true"/> if match, <see cref="false"/> if not.</returns>
        public static bool IsBugMatch(this RhinoTestCase testCase, WorkItem bug, bool assertDataSource)
        {
            // setup
            var bugHtml = bug.Fields.ContainsKey("Microsoft.VSTS.TCM.ReproSteps")
                ? $"{bug.Fields["Microsoft.VSTS.TCM.ReproSteps"]}".DecodeHtml()
                : "<div>This bug have no steps to reproduce.</div>";
            
            var testHtml = testCase.GetBugHtml().DecodeHtml();

            // build: bug (target)
            var bugDocument = new HtmlDocument();
            bugDocument.LoadHtml(bugHtml);

            // build: test case (source)
            var testDocument = new HtmlDocument();
            testDocument.LoadHtml(testHtml);

            // assert
            var isIteration = AssertNode(bugDocument, testDocument, "//span[@id='rhIteration']");
            var isPlatform = AssertNode(bugDocument, testDocument, "//td[@id='rhPlatform']");
            var isEnvironment = AssertNode(bugDocument, testDocument, "//td[@id='rhEnvironment']");
            var isCapabilities = AssertNode(bugDocument, testDocument, "//pre[@id='rhCapabilities']");
            var isOptions = AssertNode(bugDocument, testDocument, "//pre[@id='rhOptions']");
            var isDataSource = AssertNode(bugDocument, testDocument, "//table[@id='rhDataSource']");

            // get
            return assertDataSource
                ? isIteration && isPlatform && isEnvironment && isCapabilities && isOptions && isDataSource
                : isIteration && isPlatform && isEnvironment && isCapabilities && isOptions;
        }

        private static bool AssertNode(HtmlDocument bug, HtmlDocument test, string path)
        {
            // setup
            var bugNode = bug.DocumentNode.SelectSingleNode(path);
            var testNode = test.DocumentNode.SelectSingleNode(path);

            // build
            var fromBug = bugNode == null ? string.Empty : bugNode.InnerText.Trim().Sort();
            var fromTest = testNode == null ? string.Empty : testNode.InnerText.Trim().Sort();

            // get
            return fromBug.Equals(fromTest, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Creates a bug based on this RhinoTestCase.
        /// </summary>
        /// <param name="testCase">RhinoTestCase by which to create a bug.</param>
        /// <param name="connection"><see cref="VssConnection"/> by which to factor Azure clients.</param>
        /// <returns>Bug creation results from Jira.</returns>
        public static WorkItem CreateBug(this RhinoTestCase testCase, VssConnection connection)
        {
            // setup
            var project = testCase.GetProjectName();
            var testRun = testCase.GetTestRun(connection);

            // exit conditions
            if(testRun == null)
            {
                return default;
            }

            // setup
            var testCaseResults = testRun.GetTestRunResults(connection).Where(i => i.TestCase.Id.Equals(testCase.Key, Compare));

            var document = testCase.GetBugDocument(
                operation: Operation.Add,
                comment: $"Automatically created by Rhino engine on execution <a href=\"{testRun.WebAccessUrl}\">{testCase.TestRunKey}</a>.");

            // build
            var client = connection.GetClient<WorkItemTrackingHttpClient>(GlobalSettings.ClientNumberOfAttempts);

            // get
            var bug = client.CreateWorkItemAsync(document, project, "Bug").GetAwaiter().GetResult();

            // post create
            bug = CreateAttachmentsForBug(client, testCase, bug);
            testCase.Context[ContextEntry.BugOpened] = JsonSerializer.Serialize(bug, JsonOptions);

            // link test results to bug
            if (testCaseResults.Any())
            {
                foreach (var testCaseResult in testCaseResults)
                {
                    testCaseResult.AssociatedBugs ??= new List<ShallowReference>();
                    testCaseResult.AssociatedBugs.Add(bug.GetTestReference());
                }

                connection
                    .GetClient<TestManagementHttpClient>(GlobalSettings.ClientNumberOfAttempts)
                    .UpdateTestResultsAsync(testCaseResults.ToArray(), project, testRun.Id)
                    .GetAwaiter()
                    .GetResult();
            }

            // link bug to test case
            bug.CreateReleation(connection, testCase.GetWorkItem().GetTestReference(), "Tested By");

            // get
            return bug;
        }

        /// <summary>
        /// Creates a bug based on this RhinoTestCase.
        /// </summary>
        /// <param name="testCase">RhinoTestCase by which to create a bug.</param>
        /// <param name="connection"><see cref="VssConnection"/> by which to factor Azure clients.</param>
        /// <returns>Bug creation results from Jira.</returns>
        public static WorkItem UpdateBug(this RhinoTestCase testCase, WorkItem bug, VssConnection connection)
        {
            // setup
            var testRun = testCase.GetTestRun(connection);

            // exit conditions
            if(testRun == null)
            {
                return default;
            }

            // setup
            var testCaseResults = testRun.GetTestRunResults(connection).Where(i => i.TestCase.Id.Equals(testCase.Key, Compare));
            var client = connection.GetClient<WorkItemTrackingHttpClient>(GlobalSettings.ClientNumberOfAttempts);
            var comment = $"Automatically updated by Rhino engine on execution <a href=\"{testRun.WebAccessUrl}\">{testCase.TestRunKey}</a>.";
            var project = testCase.GetProjectName();

            // update Bug
            var bugDocument = testCase.GetBugDocument(Operation.Replace, comment);
            bug = client.UpdateWorkItemAsync(bugDocument, bug.Id.ToInt(), bypassRules: true).GetAwaiter().GetResult();
            bug = client.RemoveAttachments(bug);

            // upload new attachments
            bug = CreateAttachmentsForBug(client, testCase, bug);
            testCase.Context[ContextEntry.BugOpened] = JsonSerializer.Serialize(bug, JsonOptions);

            // link test results to bug
            if (!testCaseResults.Any())
            {
                return bug;
            }

            // update
            foreach (var testCaseResult in testCaseResults)
            {
                testCaseResult.AssociatedBugs ??= new List<ShallowReference>();
                testCaseResult.AssociatedBugs.Add(bug.GetTestReference());
            }
            connection
                .GetClient<TestManagementHttpClient>(GlobalSettings.ClientNumberOfAttempts)
                .UpdateTestResultsAsync(testCaseResults.ToArray(), project, testRun.Id)
                .GetAwaiter()
                .GetResult();

            // get
            return bug;
        }

        private static WorkItem CreateAttachmentsForBug(WorkItemTrackingHttpClient client, RhinoTestCase testCase, WorkItem bug)
        {
            try
            {
                // create attachments
                var attachments = testCase.CreateAttachments(client);

                // build
                var operations = attachments.Select(i => new JsonPatchOperation
                {
                    Operation = Operation.Add,
                    Path = "/relations/-",
                    Value = new
                    {
                        rel = "AttachedFile",
                        url = i.Url
                    }
                });
                var patchDocument = new JsonPatchDocument();
                patchDocument.AddRange(operations);

                // update
                return client.UpdateWorkItemAsync(patchDocument, bug.Id.ToInt()).GetAwaiter().GetResult();
            }
            catch (Exception e) when (e != null)
            {
                Trace.TraceError($"Create-Attachments -Test {testCase.Key} = {e.GetBaseException().Message}");
            }
            return bug;
        }
        #endregion

        #region *** Configuration  ***
        /// <summary>
        /// Gets <see cref="VssCredentials"/> by RhinoConfiguration.ConnectorConfiguration.
        /// </summary>
        /// <param name="configuration">RhinoConfiguration by which to get <see cref="VssCredentials"/>.</param>
        /// <returns><see cref="VssCredentials"/> object for interacting with Azure DevOps or Team Foundation Server.</returns>
        public static VssCredentials GetVssCredentials(this RhinoConfiguration configuration)
        {
            return InvokeGetVssCredentials(configuration);
        }

        /// <summary>
        /// Gets <see cref="VssConnection"/> by RhinoConfiguration.ConnectorConfiguration.
        /// </summary>
        /// <param name="configuration">RhinoConfiguration by which to get <see cref="VssConnection"/>.</param>
        /// <returns><see cref="VssConnection"/> object for interacting with Azure DevOps or Team Foundation Server.</returns>
        public static VssConnection GetVssConnection(this RhinoConfiguration configuration)
        {
            // setup
            var credentials = InvokeGetVssCredentials(configuration);
            var baseUrl = new Uri(configuration.ConnectorConfiguration.Collection);

            // get
            return new VssConnection(baseUrl, credentials);
        }

        private static VssCredentials InvokeGetVssCredentials(RhinoConfiguration configuration)
        {
            // setup
            var isOs = configuration.ConnectorConfiguration.AsOsUser;
            var userName = configuration.ConnectorConfiguration.Username;
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
        #endregion
    }
}