/*
 * CHANGE LOG - keep only last 5 threads
 * 
 * RESOURCES
 */
using HtmlAgilityPack;

using Microsoft.TeamFoundation.TestManagement.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Rhino.Api.Contracts.AutomationProvider;
using Rhino.Api.Contracts.Configuration;
using Rhino.Api.Extensions;
using Rhino.Connectors.Azure.Contracts;
using Rhino.Connectors.Azure.Framework;

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Rhino.Connectors.Azure.Extensions
{
    /// <summary>
    /// Extension package for Rhino objects.
    /// </summary>
    internal static class RhinoExtensions
    {
        #region *** Rhino Test Case: Models   ***
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
                StartedDate = DateTime.Now.AzureNow(addMilliseconds: true),
                CompletedDate = DateTime.Now.AddMinutes(5).AzureNow(addMilliseconds: true),
                Comment = "Automatically Created by Rhino Engine."
            };
            iteration.DurationInMs = (iteration.CompletedDate - iteration.StartedDate).TotalMilliseconds;

            // build
            iteration.ActionResults = GetActionResults(testCase, setOutcome).ToList();
            iteration.Parameters = GetParametersResults(testCase).ToList();

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
                var id = steps[i].Context.GetCastedValueOrDefault(AzureContextEntry.SharedStepId, -1);
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
            return actionResults.OrderBy(i => i.ActionPath);
        }

        private static TestActionResultModel GetSharedActionResultModel(RhinoTestStep testStep, int iteration, bool setOutcome)
        {
            // setup
            var id = testStep.Context.GetCastedValueOrDefault(AzureContextEntry.SharedStepId, -1);

            // build
            var actionResult = new TestActionResultModel
            {
                ActionPath = testStep.Context.GetCastedValueOrDefault(AzureContextEntry.SharedStepPath, "-1"),
                IterationId = iteration,
                SharedStepModel = new SharedStepModel { Id = id, Revision = GetSharedStepRevision(testStep) },
                StartedDate = DateTime.Now.AzureNow(addMilliseconds: false),
                CompletedDate = DateTime.Now.AddMinutes(5).AzureNow(addMilliseconds: false)
            };

            // outcome
            if (setOutcome)
            {
                actionResult.Outcome = testStep.Actual ? nameof(TestOutcome.Passed) : nameof(TestOutcome.Failed);
                actionResult.CompletedDate = DateTime.Now.AzureNow(addMilliseconds: false);
            }
            actionResult.ErrorMessage = actionResult.Outcome == nameof(TestOutcome.Failed)
                ? testStep.ReasonPhrase
                : string.Empty;

            // get
            return actionResult;
        }

        private static TestActionResultModel GetActionResultModel(RhinoTestStep testStep, int iteration, bool setOutcome)
        {
            // setup
            var actionPath = GetActionPath(testStep, $"{iteration}");

            // build
            var actionResult = new TestActionResultModel
            {
                ActionPath = actionPath,
                IterationId = iteration,
                StartedDate = DateTime.Now.AzureNow(addMilliseconds: false),
                CompletedDate = DateTime.Now.AddMinutes(5).AzureNow(addMilliseconds: false)
            };

            // outcome
            if (setOutcome)
            {
                actionResult.Outcome = testStep.Actual ? nameof(TestOutcome.Passed) : nameof(TestOutcome.Failed);
                actionResult.CompletedDate = DateTime.Now.AzureNow(addMilliseconds: false);
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
                var actionPath = GetActionPath(steps[i], $"{iteration}");
                var identifier = GetStepIdentifier(steps[i], $"{iteration}");
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
            var onStep = JsonConvert.SerializeObject(step);
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
        #endregion

        #region *** Rhino Test Case: Context  ***
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

        #region *** Rhino Test Case: Document ***
        /// <summary>
        /// Gets a <see cref="JsonPatchDocument"/> ready for posting.
        /// </summary>
        /// <param name="testCase">RhinoTestCase to created <see cref="JsonPatchDocument"/> by.</param>
        /// <returns><see cref="JsonPatchDocument"/>.</returns>
        public static JsonPatchDocument AsTestDocument(this RhinoTestCase testCase)
        {
            // setup
            var optionsKey = $"{Connector.AzureTestManager}:options";
            var options = testCase.Context.GetCastedValueOrDefault(optionsKey, new Dictionary<string, object>());
            var fields = options.GetCastedValueOrDefault(AzureCapability.CustomFields, new Dictionary<string, object>());

            // get
            return DoAsTestDocument(testCase, fields);
        }

        /// <summary>
        /// Gets a <see cref="JsonPatchDocument"/> ready for posting.
        /// </summary>
        /// <param name="testCase">RhinoTestCase to created <see cref="JsonPatchDocument"/> by.</param>
        /// <param name="customFields">A collection of custom fields to apply.</param>
        /// <returns><see cref="JsonPatchDocument"/>.</returns>
        public static JsonPatchDocument AsTestDocument(this RhinoTestCase testCase, IDictionary<string, object> customFields)
        {
            // setup
            var optionsKey = $"{Connector.AzureTestManager}:options";
            var options = testCase.Context.GetCastedValueOrDefault(optionsKey, new Dictionary<string, object>());
            var fields = options.GetCastedValueOrDefault(AzureCapability.CustomFields, new Dictionary<string, object>()).AddRange(customFields);

            // get
            return DoAsTestDocument(testCase, fields);
        }

        private static JsonPatchDocument DoAsTestDocument(RhinoTestCase testCase, IDictionary<string, object> customFields)
        {
            // setup
            _ = int.TryParse(testCase.Priority, out int priorityOut);
            var options = testCase
                .Context
                .GetCastedValueOrDefault($"{Connector.AzureTestManager}:options", new Dictionary<string, object>());

            // initiate
            var data = new Dictionary<string, object>
            {
                ["System.Title"] = testCase.Scenario,
                ["Microsoft.VSTS.Common.Priority"] = priorityOut,
                ["Microsoft.VSTS.TCM.Steps"] = GetStepsHtml(testCase)
            };

            // fields: area path
            var areaPath = options.GetCastedValueOrDefault(AzureCapability.AreaPath, string.Empty);
            AddToData(data, field: "System.AreaPath", value: areaPath);

            // fields: iteration path
            var iterationPathPath = options.GetCastedValueOrDefault(AzureCapability.IterationPath, string.Empty);
            AddToData(data, field: "System.IterationPath", value: iterationPathPath);

            // fields: data
            AddToData(data, field: "Microsoft.VSTS.TCM.LocalDataSource", value: GetDataSourceXml(testCase));

            // concat
            data = data.AddRange(customFields);

            // get
            return AzureUtilities.GetJsonPatchDocument(data);
        }

        private static string GetStepsHtml(RhinoTestCase testCase)
        {
            // setup
            var steps = new List<string>();
            var onSteps = testCase.Steps.ToArray();

            // iterate
            for (int i = 0; i < onSteps.Length; i++)
            {
                steps.Add(GetActionHtml(onSteps[i], id: i + 1));
            }

            // get
            return $"<steps id=\"0\" last=\"{steps.Count}\">{string.Concat(steps)}</steps>";
        }

        private static string GetActionHtml(RhinoTestStep step, int id)
        {
            // setup
            var expectedResults = step.Expected.Replace("\r", string.Empty).Replace("\n", "&lt;BR/&gt;").Trim();
            var type = string.IsNullOrEmpty(step.Expected) ? "ActionStep" : "ValidateStep";
            var expectedHtml = string.IsNullOrEmpty(step.Expected)
                ? "&lt;DIV&gt;&lt;P&gt;&amp;nbsp;&lt;/P&gt;&lt;/DIV&gt;"
                : "&lt;P&gt;[expected]&lt;/P&gt;";
            var action = Regex.Replace(input: step.Action, pattern: @"^\d+\.\s+", replacement: string.Empty);

            return
                $"<step id=\"{id}\" type=\"{type}\">" +
                $"<parameterizedString isformatted=\"true\">&lt;DIV&gt;&lt;DIV&gt;&lt;P&gt;{action}&amp;nbsp;&lt;/P&gt;&lt;/DIV&gt;&lt;/DIV&gt;</parameterizedString>" +
                $"<parameterizedString isformatted=\"true\">{expectedHtml.Replace("[expected]", expectedResults)}</parameterizedString>" +
                "<description/>" +
                "</step>";
        }

        private static string GetDataSourceXml(RhinoTestCase testCase)
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

        private static void AddToData<T>(IDictionary<string, object> data, string field, T value)
        {
            // exit conditions
            if (value == null || string.IsNullOrEmpty($"{value}"))
            {
                return;
            }

            // add
            data[field] = value;
        }
        #endregion

        #region *** Rhino Test Step: images   ***
        /// <summary>
        /// Get a collection of Attachment objects, ready to be uploaded.
        /// </summary>
        /// <param name="testStep">RhinoTestStep to build by.</param>
        /// <returns>A collection of <see cref="AttachmentReference"/> after uploaded.</returns>
        public static IEnumerable<TestAttachmentRequestModel> CreateAttachments(this RhinoTestStep testStep)
        {
            return DoCreateAttachments(testStep);
        }

        private static IEnumerable<TestAttachmentRequestModel> DoCreateAttachments(RhinoTestStep testStep)
        {
            // setup
            var images = testStep.GetScreenshots();

            // build
            var models = new List<TestAttachmentRequestModel>();
            foreach (var attachment in images.Select(i => CreateAttachments(i, testStep.Context)))
            {
                byte[] bytes;
                using (var memoryStream = new MemoryStream())
                {
                    attachment.UploadStream.CopyTo(memoryStream);
                    bytes = memoryStream.ToArray();
                }
                string base64 = Convert.ToBase64String(bytes);

                models.Add(new TestAttachmentRequestModel
                {
                    AttachmentType = attachment.Type,
                    Comment = "Automatically created by Rhino Engine",
                    FileName = attachment.Name,
                    Stream = base64
                });
            }

            // context
            testStep.Context[AzureContextEntry.StepAttachments] = models;

            // get
            return models;
        }

        private static Attachment CreateAttachments(string image, IDictionary<string, object> context)
        {
            // setup
            var name = Path.GetFileName(image);
            var stepRuntime = context.GetCastedValueOrDefault(AzureContextEntry.StepRuntime, string.Empty);
            var sharedRuntime = context.GetCastedValueOrDefault(AzureContextEntry.SharedStepRuntime, string.Empty);
            var runtime = string.IsNullOrEmpty(stepRuntime) ? sharedRuntime : stepRuntime;
            var item = context.GetCastedValueOrDefault(AzureContextEntry.WorkItem, new WorkItem() { Fields = new Dictionary<string, object>() });
            var project = item.Fields.GetCastedValueOrDefault("System.TeamProject", string.Empty);
            var areaPath = item.Fields.GetCastedValueOrDefault("System.AreaPath", string.Empty);

            // build
            return new Attachment
            {
                ActionPath = string.Empty,
                ActionRuntime = runtime,
                Type = nameof(AttachmentType.GeneralAttachment),
                FullName = image,
                Name = name,
                UploadStream = new FileStream(image, FileMode.Open, FileAccess.Read),
                Project = string.IsNullOrEmpty(project) ? null : project,
                AreaPath = string.IsNullOrEmpty(areaPath) ? null : areaPath,
                IterationId = context.GetCastedValueOrDefault(AzureContextEntry.IterationDetails, 0)
            };
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

        #region *** Rhino Test Case: Bugs     ***
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
            var bugHtml = $"{bug.Fields["Microsoft.VSTS.TCM.ReproSteps"]}";

            // load into DOM element
            var bugDocument = new HtmlDocument();
            bugDocument.LoadHtml(bugHtml);

            throw new NotImplementedException();
        }

        /// <summary>
        /// Creates a bug based on this RhinoTestCase.
        /// </summary>
        /// <param name="testCase">RhinoTestCase by which to create a bug.</param>
        /// <param name="connection"><see cref="VssConnection"/> by which to factor Azure clients.</param>
        /// <returns>Bug creation results from Jira.</returns>
        public static WorkItem CreateBug(this RhinoTestCase testCase, VssConnection connection)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Updates a bug based on this RhinoTestCase.
        /// </summary>
        /// <param name="testCase">RhinoTestCase by which to update a bug.</param>
        /// <param name="id">The <see cref="WorkItem.Id"/> of the bug.</param>
        /// <param name="connection"><see cref="VssConnection"/> by which to factor Azure clients.</param>
        /// <returns><see cref="true"/> if successful, <see cref="false"/> if not.</returns>
        public static bool UpdateBug(this RhinoTestCase testCase, string id, VssConnection connection)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Close a bug based on this RhinoTestCase.
        /// </summary>
        /// <param name="testCase">RhinoTestCase by which to close a bug.</param>
        /// <param name="id">The <see cref="WorkItem.Id"/> of the bug.</param>
        /// <param name="connection"><see cref="VssConnection"/> by which to factor Azure clients.</param>
        /// <returns><see cref="true"/> if close was successful, <see cref="false"/> if not.</returns>
        public static bool CloseBug(this RhinoTestCase testCase, string id, VssConnection connection)
        {
            throw new NotImplementedException();
        }
        #endregion

        #region *** Rhino Configuration       ***
        /// <summary>
        /// Gets <see cref="VssCredentials"/> by RhinoConfiguration.ConnectorConfiguration.
        /// </summary>
        /// <param name="configuration">RhinoConfiguration by which to get <see cref="VssCredentials"/>.</param>
        /// <returns><see cref="VssCredentials"/> object for interacting with Azure DevOps or Team Foundation Server.</returns>
        public static VssCredentials GetVssCredentials(this RhinoConfiguration configuration)
        {
            // setup
            var isOs = configuration.ConnectorConfiguration.AsOsUser;
            var userName = configuration.ConnectorConfiguration.UserName;
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

        /// <summary>
        /// Gets a value from connector_azure:options dictionary under RhinoConfiguration.Capabilites.
        /// </summary>
        /// <typeparam name="T">The type of value to return.</typeparam>
        /// <param name="configuration">RhinoConfiguration to get value from.</param>
        /// <param name="capability">Capability name to get value from.</param>
        /// <param name="defaultValue">The default value to return if the capability was not found.</param>
        /// <returns>The value from the capability or default if not found.</returns>
        public static T GetAzureCapability<T>(this RhinoConfiguration configuration, string capability, T defaultValue)
        {
            return DoGetAzureCapability(configuration, capability, defaultValue);
        }

        /// <summary>
        /// Adds a value to connector_azure:options dictionary under RhinoConfiguration.Capabilites.
        /// If the capability exists it will be overwritten.
        /// </summary>
        /// <param name="configuration">RhinoConfiguration to add value to.</param>
        /// <param name="capability">Capability name to add value to.</param>
        /// <param name="value">The value to add.</param>
        public static void AddAzureCapability(this RhinoConfiguration configuration, string capability, object value)
        {
            DoAddAzureCapability(configuration, capability, value);
        }
        #endregion

        #region *** JSON Bug Document         ***
        public static JsonPatchDocument AsBugDocument(this RhinoTestCase testCase)
        {
            throw new NotImplementedException();
        }
        #endregion

        #region *** Utilities ***
        private static T DoGetAzureCapability<T>(RhinoConfiguration configuration, string capability, T defaultValue)
        {
            // setup
            var optionsKey = $"{Connector.AzureTestManager}:options";
            var options = configuration.Capabilities.GetCastedValueOrDefault(optionsKey, new Dictionary<string, object>());

            // get
            return options.GetCastedValueOrDefault(capability, defaultValue);
        }

        private static void DoAddAzureCapability(RhinoConfiguration configuration, string capability, object value)
        {
            // setup
            var optionsKey = $"{Connector.AzureTestManager}:options";
            var options = configuration.Capabilities.GetCastedValueOrDefault(optionsKey, new Dictionary<string, object>());

            // put
            options[capability] = value;
            configuration.Capabilities[optionsKey] = options;
        }

        private static string GetActionPath(RhinoTestStep testStep, string defaultValue)
        {
            return testStep.Context.ContainsKey(AzureContextEntry.SharedStepActionPath)
                ? testStep.Context.GetCastedValueOrDefault(AzureContextEntry.SharedStepActionPath, defaultValue)
                : testStep.Context.GetCastedValueOrDefault(AzureContextEntry.ActionPath, defaultValue);
        }

        private static string GetStepIdentifier(RhinoTestStep testStep, string defaultValue)
        {
            return testStep.Context.ContainsKey(AzureContextEntry.SharedStepIdentifier)
                ? testStep.Context.GetCastedValueOrDefault(AzureContextEntry.SharedStepIdentifier, defaultValue)
                : testStep.Context.GetCastedValueOrDefault(AzureContextEntry.StepRuntime, defaultValue);
        }

        private static int GetSharedStepRevision(RhinoTestStep testStep)
        {
            // setup
            var item = testStep.Context.ContainsKey(AzureContextEntry.SharedStep)
                ? testStep.Context.GetCastedValueOrDefault(AzureContextEntry.SharedStep, new WorkItem())
                : new WorkItem();

            // get
            return item.Rev == null || item.Rev == 0 ? 1 : item.Rev.ToInt();
        }
        #endregion
    }
}