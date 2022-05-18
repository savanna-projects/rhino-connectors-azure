/*
 * CHANGE LOG - keep only last 5 threads
 * 
 * RESOURCES
 */
using Gravity.Extensions;
using Gravity.Services.Comet.Engine.Attributes;
using Gravity.Services.DataContracts;

using HtmlAgilityPack;

using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.TestManagement.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.TestManagement.TestPlanning.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;

using Rhino.Api.Contracts.AutomationProvider;
using Rhino.Api.Contracts.Configuration;
using Rhino.Api.Extensions;
using Rhino.Connectors.Azure.Contracts;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.XPath;

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
        private const StringComparison Compare = StringComparison.OrdinalIgnoreCase;

        #region *** Configuration       ***
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
            // setup
            const string OptionsKey = RhinoConnectors.AzureTestManager + ":options";
            var options = configuration.Capabilities.Get(OptionsKey, new Dictionary<string, object>());

            // get
            return options.Get(capability, defaultValue);
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
            // setup
            const string OptionsKey = RhinoConnectors.AzureTestManager + ":options";
            var options = configuration.Capabilities.Get(OptionsKey, new Dictionary<string, object>());

            // put
            options[capability] = value;
            configuration.Capabilities[OptionsKey] = options;
        }
        #endregion

        #region *** Rhino Test Case     ***
        // *** Bugs ***
        /// <summary>
        /// A collection of <see cref="WorkItem"/> for all open bugs.
        /// </summary>
        /// <param name="testCase">RhinoTestCase to get bugs for.</param>
        /// <param name="client"><see cref="WorkItemTrackingHttpClient"/> to use for getting bugs.</param>
        /// <returns>A collection of <see cref="WorkItem"/>.</returns>
        public static IEnumerable<WorkItem> GetOpenBugs(this RhinoTestCase testCase, WorkItemTrackingHttpClient client)
        {
            return InvokeGetOpenBugs(testCase, client);
        }

        /// <summary>
        /// A collection of <see cref="WorkItem"/> for all open bugs.
        /// </summary>
        /// <param name="testCase">RhinoTestCase to get bugs for.</param>
        /// <param name="connection"><see cref="VssConnection"/> to use for getting bugs.</param>
        /// <returns>A collection of <see cref="WorkItem"/>.</returns>
        public static IEnumerable<WorkItem> GetOpenBugs(this RhinoTestCase testCase, VssConnection connection)
        {
            // setup
            var client = connection.GetClient<WorkItemTrackingHttpClient>(GlobalSettings.ClientNumberOfAttempts);

            // get
            return InvokeGetOpenBugs(testCase, client);
        }

        // *** Work Item ***
        /// <summary>
        /// Gets the underline <see cref="WorkItem"/> from RhinoTestCase.Context or from ALM.
        /// </summary>
        /// <param name="testCase">RhinoTestCase to get <see cref="WorkItem"/> by.</param>
        /// <returns>A <see cref="WorkItem"/> or <see cref="null"/> if not found.</returns>
        public static WorkItem GetWorkItem(this RhinoTestCase testCase)
        {
            return InvokeGetWorkItem(testCase, null);
        }

        /// <summary>
        /// Gets the underline <see cref="WorkItem"/> from RhinoTestCase.Context or from ALM.
        /// </summary>
        /// <param name="testCase">RhinoTestCase to get <see cref="WorkItem"/> by.</param>
        /// <param name="connection"><see cref="VssConnection"/> to use for fall back.</param>
        /// <returns>A <see cref="WorkItem"/> or <see cref="null"/> if not found.</returns>
        public static WorkItem GetWorkItem(this RhinoTestCase testCase, VssConnection connection)
        {
            return InvokeGetWorkItem(testCase, connection);
        }

        private static WorkItem InvokeGetWorkItem(RhinoTestCase testCase, VssConnection connection)
        {
            // setup
            var item = testCase.Context.Get(AzureContextEntry.WorkItem, default(WorkItem));

            // exit conditions
            if (item != default || connection == null)
            {
                return item;
            }

            try
            {
                // setup
                var project = InvokeGetProjectName(testCase);
                _ = int.TryParse(testCase.Key, out int idOut);

                // build
                item = connection
                    .GetClient<WorkItemTrackingHttpClient>(GlobalSettings.ClientNumberOfAttempts)
                    .GetWorkItemAsync(project, id: idOut, fields: null, expand: WorkItemExpand.All)
                    .GetAwaiter()
                    .GetResult();
                testCase.Context[AzureContextEntry.WorkItem] = item;

                // get
                return item;
            }
            catch (Exception e) when (e != null)
            {
                return default;
            }
        }

        /// <summary>
        /// Gets a <see cref="TestRun"/> under which RhinoTestCase was invoked.
        /// </summary>
        /// <param name="testCase">RhinoTestCase to get <see cref="TestRun"/> by.</param>
        /// <returns>The <see cref="TestRun"/></returns>
        public static TestRun GetTestRun(this RhinoTestCase testCase, VssConnection connection)
        {
            try
            {
                // setup
                var isTestRun = int.TryParse(testCase.TestRunKey, out int testRunOut);
                if (!isTestRun)
                {
                    return default;
                }

                // build
                var client = connection.GetClient<TestManagementHttpClient>(GlobalSettings.ClientNumberOfAttempts);
                var project = InvokeGetProjectName(testCase);

                // build
                var testRun = client.GetTestRunByIdAsync(project, testRunOut).GetAwaiter().GetResult();
                testCase.Context[AzureContextEntry.TestRun] = testRun;

                // get
                return testRun;
            }
            catch (Exception e) when (e != null)
            {
                return default;
            }
        }

        /// <summary>
        /// Gets the project name under which you can find the RhinoTestCase.
        /// </summary>
        /// <param name="testCase">RhinoTestCase to find project by.</param>
        /// <returns>The project name.</returns>
        /// <remarks>The project name will be fetched from the entity context.</remarks>
        public static string GetProjectName(this RhinoTestCase testCase)
        {
            return InvokeGetProjectName(testCase);
        }

        /// <summary>
        /// Gets the bucket size registered for the automation provider parallel invokes.
        /// </summary>
        /// <param name="testCase"></param>
        /// <returns>The bucket size registered for the automation provider.</returns>
        public static int GetBucketSize(this RhinoTestCase testCase)
        {
            return InvokeGetBucketSize(testCase);
        }

        /// <summary>
        /// Gets a collection of <see cref="AttachmentReference"/> based on RhinoTestCase screen-shots.
        /// </summary>
        /// <param name="testCase">The RhinoTestCase to get by.</param>
        /// <param name="client"><see cref="WorkItemTrackingHttpClient"/> to use for uploading.</param>
        /// <returns>A collection of <see cref="AttachmentReference"/>.</returns>
        public static IEnumerable<AttachmentReference> CreateAttachments(this RhinoTestCase testCase, WorkItemTrackingHttpClient client)
        {
            // setup
            var references = new ConcurrentBag<AttachmentReference>();
            var project = InvokeGetProjectName(testCase);
            var filesPath = testCase.GetScreenshots();
            var maxParallel = InvokeGetBucketSize(testCase);
            var options = new ParallelOptions { MaxDegreeOfParallelism = maxParallel };

            // build
            Parallel.ForEach(filesPath, options, filePath =>
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                var reference = client.CreateAttachmentAsync(uploadStream: stream, project: project, fileName: filePath)
                    .GetAwaiter()
                    .GetResult();
                references.Add(reference);
            });

            // get
            return references;
        }

        /// <summary>
        /// Gets the RhinoTestCase priority or default.
        /// </summary>
        /// <param name="testCase">RhinoTestCase to get priority from.</param>
        /// <returns>The priority.</returns>
        public static string GetPriority(this RhinoTestCase testCase)
        {
            return InvokeGetPriority(testCase);
        }

        /// <summary>
        /// Gets the RhinoTestCase severity or default.
        /// </summary>
        /// <param name="testCase">RhinoTestCase to get severity from.</param>
        /// <returns>The severity.</returns>
        public static string GetSeverity(this RhinoTestCase testCase)
        {
            return InvokeGetSeverity(testCase);
        }

        /// <summary>
        /// Gets a list of static custom fields from RhinoTestCase.Context.
        /// </summary>
        /// <param name="testCase">RhinoTestCase to get fields by.</param>
        /// <returns>A list of static custom fields.</returns>
        public static IDictionary<string, object> GetCustomFields(this RhinoTestCase testCase)
        {
            return InvokeGetCustomFields(testCase);
        }

        // *** Bug document ***
        /// <summary>
        /// Gets a <see cref="JsonPatchDocument"/> for creating a bug <see cref="WorkItem"/>.
        /// </summary>
        /// <param name="testCase">RhinoTestCase to create a bug by.</param>
        /// <param name="operation">The <see cref="Operation"/> to use with the document.</param>
        /// <param name="comment">Comment to add when creating the bug.</param>
        /// <returns>A <see cref="JsonPatchDocument"/>.</returns>
        public static JsonPatchDocument GetBugDocument(this RhinoTestCase testCase, Operation operation, string comment)
        {
            // setup
            var customFields = InvokeGetCustomFields(testCase);

            // get
            return InvokeGetBugDocument(testCase, operation, customFields, comment);
        }

        /// <summary>
        /// Gets a <see cref="JsonPatchDocument"/> for creating a bug <see cref="WorkItem"/>.
        /// </summary>
        /// <param name="testCase">RhinoTestCase to create a bug by.</param>
        /// <param name="operation">The <see cref="Operation"/> to use with the document.</param>
        /// <param name="customFields">A collection of static custom fields to apply when creating the document.</param>
        /// <param name="comment">Comment to add when creating the bug.</param>
        /// <returns>A <see cref="JsonPatchDocument"/>.</returns>
        public static JsonPatchDocument GetBugDocument(
            this RhinoTestCase testCase, Operation operation, IDictionary<string, object> customFields, string comment)
        {
            return InvokeGetBugDocument(testCase, operation, customFields, comment);
        }

        private static JsonPatchDocument InvokeGetBugDocument(
            this RhinoTestCase testCase, Operation operation, IDictionary<string, object> customFields, string comment)
        {
            // build
            var data = new Dictionary<string, object>
            {
                ["System.Title"] = testCase.Scenario,
                ["Microsoft.VSTS.Common.Priority"] = InvokeGetPriority(testCase),
                ["Microsoft.VSTS.Common.Severity"] = InvokeGetSeverity(testCase),
                ["Microsoft.VSTS.TCM.ReproSteps"] = testCase.GetBugHtml(),
                ["System.History"] = comment
            };
            data.AddRange(customFields);

            // get
            return AzureUtilities.GetJsonPatchDocument(data, operation);
        }

        // *** Test Document ***
        /// <summary>
        /// Gets a <see cref="JsonPatchDocument"/> for creating a TestCase <see cref="WorkItem"/>.
        /// </summary>
        /// <param name="testCase">RhinoTestCase to create a TestCase by.</param>
        /// <param name="operation">The <see cref="Operation"/> to use with the Document.</param>
        /// <param name="comment">Comment to add when creating the TestCase.</param>
        /// <returns>A <see cref="JsonPatchDocument"/>.</returns>
        public static JsonPatchDocument GetTestDocument(this RhinoTestCase testCase, Operation operation, string comment)
        {
            // setup
            var customFields = InvokeGetCustomFields(testCase);

            // get
            return InvokeGetTestDocument(testCase, operation, customFields, comment);
        }

        /// <summary>
        /// Gets a <see cref="JsonPatchDocument"/> for creating a TestCase <see cref="WorkItem"/>.
        /// </summary>
        /// <param name="testCase">RhinoTestCase to create a TestCase by.</param>
        /// <param name="operation">The <see cref="Operation"/> to use with the Invoke.</param>
        /// <param name="customFields">A collection of static custom fields to apply when creating the Document.</param>
        /// <param name="comment">Comment to add when creating the TestCase.</param>
        /// <returns>A <see cref="JsonPatchDocument"/>.</returns>
        public static JsonPatchDocument GetTestDocument(
            this RhinoTestCase testCase, Operation operation, IDictionary<string, object> customFields, string comment)
        {
            return InvokeGetTestDocument(testCase, operation, customFields, comment);
        }

        private static JsonPatchDocument InvokeGetTestDocument(
            this RhinoTestCase testCase, Operation operation, IDictionary<string, object> customFields, string comment)
        {
            // setup
            var options = testCase
                .Context
                .Get($"{RhinoConnectors.AzureTestManager}:options", new Dictionary<string, object>());

            // initiate
            var data = new Dictionary<string, object>
            {
                ["System.Title"] = testCase.Scenario,
                ["Microsoft.VSTS.Common.Priority"] = InvokeGetPriority(testCase),
                ["Microsoft.VSTS.TCM.Steps"] = testCase.GetStepsHtml(),
                ["System.History"] = comment
            };

            // fields: area path
            var areaPath = options.Get(AzureCapability.AreaPath, string.Empty);
            data.SetWhenNotNullOrEmpty(key: "System.AreaPath", value: areaPath);

            // fields: iteration path
            var iterationPathPath = options.Get(AzureCapability.IterationPath, string.Empty);
            data.SetWhenNotNullOrEmpty(key: "System.IterationPath", value: iterationPathPath);

            // fields: data
            data.SetWhenNotNullOrEmpty(key: "Microsoft.VSTS.TCM.LocalDataSource", value: testCase.GetDataSourceXml());

            // concat
            data.AddRange(customFields);

            // get
            return AzureUtilities.GetJsonPatchDocument(data, operation);
        }
        #endregion

        #region *** Rhino Test Step     ***
        /// <summary>
        /// Gets the step action path from RhinoTestCase context.
        /// </summary>
        /// <param name="testStep">The RhinoTestStep to get path from.</param>
        /// <param name="defaultValue">The default value to return if path was not found.</param>
        /// <returns>The step action path.</returns>
        public static string GetActionPath(this RhinoTestStep testStep, string defaultValue)
        {
            // setup
            var isShared = testStep.Context.ContainsKey(AzureContextEntry.SharedStepActionPath);

            // get
            return isShared
                ? testStep.Context.Get(AzureContextEntry.SharedStepActionPath, defaultValue)
                : testStep.Context.Get(AzureContextEntry.ActionPath, defaultValue);
        }

        /// <summary>
        /// Gets the step action runtime identifier from RhinoTestCase context.
        /// </summary>
        /// <param name="testStep">The RhinoTestStep to get identifier from.</param>
        /// <param name="defaultValue">The default value to return if path was not found.</param>
        /// <returns>The step runtime identifier.</returns>
        public static string GetActionIdentifier(this RhinoTestStep testStep, string defaultValue)
        {
            // setup
            var isShared = testStep.Context.ContainsKey(AzureContextEntry.SharedStepActionPath);

            // get
            return isShared
                ? testStep.Context.Get(AzureContextEntry.SharedStepIdentifier, defaultValue)
                : testStep.Context.Get(AzureContextEntry.StepRuntime, defaultValue);
        }

        /// <summary>
        /// Gets the revision number of the shared steps related to the RhinoTestStep.
        /// </summary>
        /// <param name="testStep">The RhinoTestStep</param>
        /// <returns>The revision number.</returns>
        public static int GetSharedStepsRevision(this RhinoTestStep testStep)
        {
            // setup
            var item = testStep.Context.ContainsKey(AzureContextEntry.SharedStep)
                ? testStep.Context.Get(AzureContextEntry.SharedStep, new WorkItem())
                : new WorkItem();

            // get
            return item.Rev == null || item.Rev == 0 ? 1 : item.Rev.ToInt();
        }

        /// <summary>
        /// Gets an Attachment object with uploading information.
        /// </summary>
        /// <param name="testStep">RhinoTestCase to create information by.</param>
        /// <param name="filePath">File path to create information by.</param>
        /// <returns>An Attachment object.</returns>
        public static Attachment GetAttachment(this RhinoTestStep testStep, string filePath)
        {
            // setup
            var name = Path.GetFileName(filePath);
            var stepRuntime = testStep.Context.Get(AzureContextEntry.StepRuntime, string.Empty);
            var sharedRuntime = testStep.Context.Get(AzureContextEntry.SharedStepRuntime, string.Empty);
            var runtime = string.IsNullOrEmpty(stepRuntime) ? sharedRuntime : stepRuntime;
            var item = testStep.Context.Get(AzureContextEntry.WorkItem, new WorkItem() { Fields = new Dictionary<string, object>() });
            var project = item.Fields.Get("System.TeamProject", string.Empty);
            var areaPath = item.Fields.Get("System.AreaPath", string.Empty);

            // build
            return new Attachment
            {
                ActionPath = string.Empty,
                ActionRuntime = runtime,
                Type = nameof(AttachmentType.GeneralAttachment),
                FullName = filePath,
                Name = name,
                UploadStream = new FileStream(filePath, FileMode.Open, FileAccess.Read),
                Project = string.IsNullOrEmpty(project) ? null : project,
                AreaPath = string.IsNullOrEmpty(areaPath) ? null : areaPath,
                IterationId = testStep.Context.Get(AzureContextEntry.IterationDetails, 0)
            };
        }
        #endregion

        #region *** Work Item Object    ***
        // TODO: implement
        /// <summary>
        /// Gets a RhinoPlugin from a Test Case or Shared Steps <see cref="WorkItem"/>.
        /// </summary>
        /// <param name="client">The <see cref="WorkItemTrackingHttpClient"/> to use for fetching.</param>
        /// <param name="id">The <see cref="WorkItem.Id"/> by which to get the <see cref="WorkItem"/></param>
        /// <returns>A RhinoPlugin based on the Test Case or Shared Steps <see cref="WorkItem"/></returns>
        public static RhinoPlugin GetRhinoPlugin(this WorkItemTrackingHttpClient client, int id)
        {
            // setup
            InvokeGetRhinoTestCases(client, new[] { id });

            // build
            return new RhinoPlugin
            {
                Parameters = Array.Empty<RhinoPluginParameter>()
            };
        }

        private static ActionAttribute GetPluginPa(this WorkItem item)
        {
            // bad request
            var itemType = $"{item.Fields["System.WorkItemType"]}";
            if (!itemType.Equals("Shared Steps", StringComparison.OrdinalIgnoreCase))
            {
                var message = $"Create-ActionAttribute -ItemType {item.Fields[""]} = (BadRequest | Invalid item type)";
                throw new InvalidOperationException(message);
            }

            // TODO: get description
            const string description = "";
            var title = $"{item.Fields["System.Title"]}";

            // build
            var examples = new PluginExample
            {
                ActionExample = new ActionRule { ActionType = title },
                Description = $"A shared steps entity fetched from Azure DevOps: {item.Url}"
            };

            // get
            return new ActionAttribute
            {
                Description = description,
                Name = title,
                Examples = new[] { examples }
            };
        }

        /// <summary>
        /// Sets state and reason of a <see cref="WorkItem"/>.
        /// </summary>
        /// <param name="item">The <see cref="WorkItem"/>.</param>
        /// <param name="connection">The <see cref="VssConnection"/> to use for settings.</param>
        /// <param name="state">The state to set.</param>
        /// <param name="reason">The reason to set.</param>
        /// <returns><see cref="true"/> if successful or <see cref="false"/> if not.</returns>
        public static bool SetState(this WorkItem item, VssConnection connection, string state, string reason)
        {
            // setup
            var client = connection.GetClient<WorkItemTrackingHttpClient>(GlobalSettings.ClientNumberOfAttempts);

            // set
            return InvokeSetState(item, client, state, reason);
        }

        /// <summary>
        /// Sets state and reason of a <see cref="WorkItem"/>.
        /// </summary>
        /// <param name="item">The <see cref="WorkItem"/>.</param>
        /// <param name="client">The <see cref="WorkItemTrackingHttpClient"/> to use for settings.</param>
        /// <param name="state">The state to set.</param>
        /// <param name="reason">The reason to set.</param>
        /// <returns><see cref="true"/> if successful or <see cref="false"/> if not.</returns>
        public static bool SetState(
            this WorkItem item, WorkItemTrackingHttpClient client, string state, string reason)
        {
            return InvokeSetState(item, client, state, reason);
        }

        // TODO: allow passing custom comment
        private static bool InvokeSetState(
            this WorkItem item, WorkItemTrackingHttpClient client, string state, string reason)
        {
            try
            {
                // setup
                var document = new JsonPatchDocument();

                // build
                if (!string.IsNullOrEmpty(state))
                {
                    document.Add(new JsonPatchOperation
                    {
                        Operation = Operation.Replace,
                        Path = "/fields/System.State",
                        Value = state
                    });
                }
                if (!string.IsNullOrEmpty(reason))
                {
                    document.Add(new JsonPatchOperation
                    {
                        Operation = Operation.Replace,
                        Path = "/fields/System.Reason",
                        Value = reason
                    });
                }

                // exit conditions
                if (!document.Any() || string.IsNullOrEmpty(state))
                {
                    return false;
                }

                var comment = string.IsNullOrEmpty(reason)
                    ? $"Set to <b><u>{state}</u></b> by Rhino Engine."
                    : $"Set to <b><u>{state}</u></b> by Rhino Engine, because of <b><u>{reason}</u></b>.";
                document.Add(new JsonPatchOperation
                {
                    Operation = Operation.Add,
                    Path = "/fields/System.History",
                    Value = comment
                });

                // update
                client.UpdateWorkItemAsync(document, item.Id.ToInt(), bypassRules: true).GetAwaiter().GetResult();
                return true;
            }
            catch (Exception e) when (e != null)
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a <see cref="ShallowReference"/> for test results (bug or work item).
        /// </summary>
        /// <param name="item">The work item to create reference from.</param>
        /// <returns>A <see cref="ShallowReference"/> for test results.</returns>
        public static ShallowReference GetTestReference(this WorkItem item) => new()
        {
            Id = $"{item.Id}",
            Name = item.Fields.Get("System.Title", string.Empty),
            Url = item.Url
        };

        /// <summary>
        /// Creates a relation between 2 <see cref="WorkItem"/>.
        /// </summary>
        /// <param name="item">The source <see cref="WorkItem"/> from which create a relation.</param>
        /// <param name="target">The target <see cref="WorkItem"/> to which create a relation.</param>
        /// <param name="relation">The relation name (e.g. Tests, Child, Tested By, etc.).</param>
        public static void CreateReleation(this WorkItem item, VssConnection connection, ShallowReference target, string relation)
        {
            // setup
            var client = connection.GetClient<WorkItemTrackingHttpClient>(GlobalSettings.ClientNumberOfAttempts);

            // create
            InvokeCreateReleation(item, client, target, relation);
        }

        /// <summary>
        /// Creates a relation between 2 <see cref="WorkItem"/>.
        /// </summary>
        /// <param name="item">The source <see cref="WorkItem"/> from which create a relation.</param>
        /// <param name="target">The target <see cref="WorkItem"/> to which create a relation.</param>
        /// <param name="relation">The relation name (e.g. Tests, Child, Tested By, etc.).</param>
        public static void CreateReleation(
            this WorkItem item, WorkItemTrackingHttpClient client, ShallowReference target, string relation)
        {
            InvokeCreateReleation(item, client, target, relation);
        }

        private static void InvokeCreateReleation(
            WorkItem item, WorkItemTrackingHttpClient client, ShallowReference target, string relation)
        {
            // setup
            var relationType = client.GetRelationTypesAsync().GetAwaiter().GetResult().Find(i => i.Name.Equals(relation, Compare));

            // exit conditions
            if (relationType == default)
            {
                return;
            }

            // build
            var operation = new JsonPatchOperation
            {
                Operation = Operation.Add,
                Path = "/relations/-",
                Value = new
                {
                    rel = relationType.ReferenceName,
                    url = target?.Url
                }
            };
            var document = new JsonPatchDocument
            {
                operation
            };

            // update
            try
            {
                client.UpdateWorkItemAsync(document, item.Id.ToInt(), false, true).GetAwaiter().GetResult();
            }
            catch (Exception e) when (e != null)
            {
                // ignore exceptions
            }
        }
        #endregion

        #region *** Test Plan Client    ***
        /// <summary>
        /// Gets all test suites associated with a <see cref="WorkItem"/>.
        /// </summary>
        /// <param name="client"><see cref="TestPlanHttpClient"/> to use for performing search.</param>
        /// <param name="id"><see cref="WorkItem.Id"/> for which to find test suites.</param>
        /// <returns>A collection of <see cref="TestSuite.Id"/>.</returns>
        public static IEnumerable<int> FindTestSuites(this TestPlanHttpClient client, int id)
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

        /// <summary>
        /// Gets <see cref="TestPlan.Id"/> for the provided <see cref="TestSuite.Id"/>.
        /// </summary>
        /// <param name="client"><see cref="TestPlanHttpClient"/> to use for performing search.</param>
        /// <param name="project">Team project from which to get <see cref="TestPlan.Id"/>.</param>
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
                return (plan?.Id) ?? 0;
            }
            catch (Exception e) when (e != null)
            {
                return 0;
            }
        }

        /// <summary>
        /// Gets the first parent <see cref="TestPlan.Id"/> for the provided <see cref="TestCase"/>.
        /// </summary>
        /// <param name="client"><see cref="TestPlanHttpClient"/> to use for performing search.</param>
        /// <param name="project">Team project from which to get <see cref="TestPlan.Id"/>.</param>
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

        #region *** Work Item Client    ***
        public static WorkItem AddComment(this WorkItemTrackingHttpClient client, WorkItem item, string comment)
        {
            // setup
            var operation = new JsonPatchOperation
            {
                Operation = Operation.Add,
                Path = "/fields/System.History",
                Value = comment
            };
            var document = new JsonPatchDocument { operation };

            // add
            try
            {
                return client.UpdateWorkItemAsync(document, item.Id.ToInt()).GetAwaiter().GetResult();
            }
            catch (Exception e) when (e != null)
            {
                // ignore exceptions
            }
            return item;
        }

        /// <summary>
        /// Removes all related attachments from the <see cref="WorkItem"/>.
        /// </summary>
        /// <param name="client"><see cref="WorkItemTrackingHttpClient"/> to use.</param>
        /// <param name="item">The <see cref="WorkItem"/> to remove from.</param>
        /// <returns>The updated <see cref="WorkItem"/>.</returns>
        public static WorkItem RemoveAttachments(this WorkItemTrackingHttpClient client, WorkItem item)
        {
            // setup
            var relations = item.Relations.ToList();

            // find
            var document = new JsonPatchDocument();
            for (int i = 0; i < relations.Count; i++)
            {
                if (!relations[i].Rel.Equals("AttachedFile"))
                {
                    continue;
                }
                document.Add(new JsonPatchOperation
                {
                    Operation = Operation.Remove,
                    Path = $"/relations/{i}"
                });
            }

            // remove
            try
            {
                return client
                    .UpdateWorkItemAsync(document, item.Id.ToInt(), bypassRules: true)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception e) when (e != null)
            {
                // ignore exceptions
            }
            return item;
        }

        /// <summary>
        /// Gets a collection of bug <see cref="WorkItem"/>.
        /// </summary>
        /// <param name="client"><see cref="WorkItemTrackingHttpClient"/> to fetch data by.</param>
        /// <param name="testCase">RhinoTestCase to get bugs for.</param>
        /// <returns>A collection of bug <see cref="WorkItem"/>.</returns>
        public static IEnumerable<WorkItem> GetBugs(this WorkItemTrackingHttpClient client, RhinoTestCase testCase)
        {
            return InvokeGetBugs(client, testCase);
        }

        /// <summary>
        /// Gets a RhinoTestCase based on <see cref="WorkItem.Id"/>.
        /// </summary>
        /// <param name="client"><see cref="WorkItemTrackingHttpClient"/> to fetch data by.</param>
        /// <param name="id">The <see cref="WorkItem.Id"/> to create by.</param>
        /// <returns>RhinoTestCase object.</returns>
        public static RhinoTestCase GetRhinoTestCase(this WorkItemTrackingHttpClient client, int id)
        {
            return InvokeGetRhinoTestCases(client, new[] { id }).FirstOrDefault();
        }

        /// <summary>
        /// Gets a collection of RhinoTestCase based on a collection of <see cref="WorkItem.Id"/>.
        /// </summary>
        /// <param name="client"><see cref="WorkItemTrackingHttpClient"/> to fetch data by.</param>
        /// <param name="ids">A collection of <see cref="WorkItem.Id"/> to create by.</param>
        /// <returns>A collection RhinoTestCase objects.</returns>
        public static IEnumerable<RhinoTestCase> GetRhinoTestCases(this WorkItemTrackingHttpClient client, IEnumerable<int> ids)
        {
            return InvokeGetRhinoTestCases(client, ids);
        }

        private static IEnumerable<RhinoTestCase> InvokeGetRhinoTestCases(WorkItemTrackingHttpClient client, IEnumerable<int> ids)
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
                var stepsHtml = item.Fields.Get("Microsoft.VSTS.TCM.Steps", string.Empty).DecodeHtml();
                var stepsDocument = new HtmlDocument();

                // load
                stepsDocument.LoadHtml(stepsHtml);

                // put
                var testCase = new RhinoTestCase
                {
                    Key = $"{item.Id}"
                };
                testCase.Scenario = item.Fields.Get("System.Title", string.Empty);
                testCase.Priority = $"{item.Fields.Get("Microsoft.VSTS.Common.Priority", 2L)}";
                testCase.Steps = InvokeGetSteps(client, stepsDocument.DocumentNode.SelectNodes("//steps/*"));
                testCase.TotalSteps = testCase.Steps.Count();
                testCase.DataSource = InvokeGetDataSource(client, item);
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

        private static IEnumerable<RhinoTestStep> InvokeGetSteps(WorkItemTrackingHttpClient client, HtmlNodeCollection nodes)
        {
            // setup
            var nodesQueue = new ConcurrentStack<(IDictionary<string, object> Context, HtmlNode Node)>();
            var testSteps = new ConcurrentBag<RhinoTestStep>();

            // load initial
            foreach (var item in nodes.Select(i => (new Dictionary<string, object>(), i)))
            {
                nodesQueue.Push(item);
            }

            // iterate
            while (!nodesQueue.IsEmpty)
            {
                var testStep = InvokeGetStep(client, nodesQueue);
                if (testStep == default)
                {
                    continue;
                }
                testSteps.Add(testStep);
            }

            // get
            return testSteps;
        }

        private static RhinoTestStep InvokeGetStep(
            WorkItemTrackingHttpClient client,
            ConcurrentStack<(IDictionary<string, object> Context,HtmlNode Node)> nodes)
        {
            // setup > dequeue next
            nodes.TryPop(out (IDictionary<string, object> Context, HtmlNode Node) stepOut);

            // process step
            if (stepOut.Node.Name.Equals("step", StringComparison.OrdinalIgnoreCase))
            {
                return InvokeGetStep(step: stepOut);
            }

            // process shared steps
            var shared = client.GetWorkItemAsync(int.Parse(stepOut.Node.GetAttributeValue("ref", "0"))).GetAwaiter().GetResult();
            var stepsDocument = new HtmlDocument();
            stepsDocument.LoadHtml(shared.Fields.Get("Microsoft.VSTS.TCM.Steps", string.Empty).DecodeHtml());

            // setup > enqueue next
            var sharedStepAction = int.Parse(stepOut.Node.GetAttributeValue("id", "0")).ToString("x");
            var range = new List<(IDictionary<string, object>, HtmlNode)>();
            foreach (var node in stepsDocument.DocumentNode.SelectNodes(".//steps/step"))
            {
                var runtime = int.Parse(node.GetAttributeValue("id", "0")).ToString("x");
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

        private static RhinoTestStep InvokeGetStep((IDictionary<string, object> Context, HtmlNode Node) step)
        {
            // setups
            var onStep = step.Node.SelectNodes(".//parameterizedstring");
            var expectedResults = onStep[1]
                .InnerText.DecodeHtml()
                .Replace(pattern: "(?i)(verify|assert)", replacement: "\nverify")
                .Split('\n')
                .Where(i => !string.IsNullOrEmpty(i.Trim()))
                .ToList();

            // build
            var rhinoStep = new RhinoTestStep
            {
                Action = onStep[0].InnerText.DecodeHtml().Trim(),
                ExpectedResults = expectedResults?
                    .Select(i => new RhinoExpectedResult { ExpectedResult = i })
                    .ToArray()
            };
            rhinoStep.ExpectedResults ??= Array.Empty<RhinoExpectedResult>();

            // setup
            var id = step.Node.GetAttributeValue("id", "-1");
            _ = int.TryParse(id, out int idOut);
            var idHex = idOut.ToString("x");

            // context
            rhinoStep.Context[AzureContextEntry.StepRuntime] = id;
            rhinoStep.Context[AzureContextEntry.Step] = step;
            rhinoStep.Context[AzureContextEntry.ActionPath] = new string('0', 8 - idHex.Length) + idHex;

            foreach (var item in step.Context)
            {
                rhinoStep.Context[item.Key] = item.Value;
            }

            // put
            return rhinoStep;
        }

        private static IEnumerable<IDictionary<string, object>> InvokeGetDataSource(WorkItemTrackingHttpClient client, WorkItem item)
        {
            // setup
            var dataSource = item
                .Fields
                .Get("Microsoft.VSTS.TCM.LocalDataSource", string.Empty)
                .Replace(" encoding=\"utf-16\"", string.Empty);

            // get
            if (dataSource.IsXml())
            {
                return GetXsd(dataSource);
            }
            if(dataSource.IsJson())
            {
                return GetJson(client, dataSource);
            }

            // return
            return Array.Empty<IDictionary<string, object>>();
        }

        private static IEnumerable<IDictionary<string, object>> GetXsd(string xml)
        {
            // setup
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

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

        private static IEnumerable<IDictionary<string, object>> GetJson(WorkItemTrackingHttpClient client, string json)
        {
            try
            {
                // setup
                var dataMap = JsonDocument.Parse(json).RootElement.GetProperty("sharedParameterDataSetIds");
                var items = System.Text.Json.JsonSerializer.Deserialize<IEnumerable<int>>(dataMap.ToString());
                var id = items.Any() ? items.ElementAt(0) : default;

                // not found
                if(id == default)
                {
                    return Array.Empty<IDictionary<string, object>>();
                }

                // build
                var workItem = client.GetWorkItemAsync(id, expand: WorkItemExpand.All).GetAwaiter().GetResult();

                // get
                return InvokeGetSharedParameters(workItem).ToDictionary();
            }
            catch (Exception e) when (e != null)
            {
                return Array.Empty<IDictionary<string, object>>();
            }
        }

        /// <summary>
        /// Gets a collection of available states of a <see cref="WorkItem"/> by a <see cref="WorkItemStateColor.Category"/>.
        /// </summary>
        /// <param name="client">The <see cref="WorkItemTrackingHttpClient"/> to use for fetching data.</param>
        /// <param name="project">The project for which to get a list of available states.</param>
        /// <param name="itemType">The item type for which to get a list of available states.</param>
        /// <param name="category">The category for which to get a list of available states.</param>
        /// <returns>A collection of available states.</returns>
        public static IEnumerable<string> GetStatesByCategory(
            this WorkItemTrackingHttpClient client, string project, string itemType, string category)
        {
            return InvokeGetStatesByCategory(client, project, itemType, category);
        }
        #endregion

        #region *** Project Client      ***
        /// <summary>
        /// Gets all projects.
        /// </summary>
        /// <param name="client">The ProjectHttpClient to use.</param>
        /// <param name="numberOfAttempts">The number of attempts if getting project fails.</param>
        /// <returns>A collection of TeamProjectReference.</returns>
        public static IEnumerable<TeamProjectReference> GetProjects(this ProjectHttpClient client, int numberOfAttempts)
        {
            // setup
            var attempt = 1;

            // iterate
            while (attempt < numberOfAttempts)
            {
                try
                {
                    return client.GetProjects(ProjectState.All).GetAwaiter().GetResult();
                }
                catch (Exception e) when (e != null)
                {
                    if(attempt == numberOfAttempts)
                    {
                        throw;
                    }
                    Thread.Sleep(3000);
                    attempt++;
                }
            }

            // default
            return Array.Empty<TeamProjectReference>();
        }
        #endregion

        #region *** Test Run Object     ***
        /// <summary>
        /// Gets a collection of <see cref="TestCaseResult"/>.
        /// </summary>
        /// <param name="testRun"><see cref="TestRun"/> to get results by.</param>
        /// <param name="connection"><see cref="VssConnection"/> to use for getting results.</param>
        /// <returns>A collection of <see cref="TestCaseResult"/>.</returns>
        public static IEnumerable<TestCaseResult> GetTestRunResults(this TestRun testRun, VssConnection connection)
        {
            // setup
            var client = connection.GetClient<TestManagementHttpClient>(GlobalSettings.ClientNumberOfAttempts);

            // get
            return InvokeGetTestRunResults(testRun, client);
        }

        /// <summary>
        /// Gets a collection of <see cref="TestCaseResult"/>.
        /// </summary>
        /// <param name="testRun"><see cref="TestRun"/> to get results by.</param>
        /// <param name="client"><see cref="TestManagementHttpClient"/> to use for getting results.</param>
        /// <returns>A collection of <see cref="TestCaseResult"/>.</returns>
        public static IEnumerable<TestCaseResult> GetTestRunResults(this TestRun testRun, TestManagementHttpClient client)
        {
            return InvokeGetTestRunResults(testRun, client);
        }

        private static IEnumerable<TestCaseResult> InvokeGetTestRunResults(TestRun testRun, TestManagementHttpClient client)
        {
            // exit conditions
            if(testRun == null)
            {
                return Array.Empty<TestCaseResult>();
            }

            // setup
            const int BatchSize = 1000;
            var gropus = testRun.TotalTests > BatchSize ? (BatchSize / testRun.TotalTests) + 1 : 1;

            // get flat results
            var testCaseResults = new List<TestCaseResult>();
            for (int i = 0; i < gropus; i++)
            {
                var range = client
                    .GetTestResultsAsync(testRun.Project.Name, testRun.Id, top: BatchSize, skip: i * BatchSize)
                    .GetAwaiter()
                    .GetResult();
                testCaseResults.AddRange(range);
            }

            // get results with iterations
            var iterations = new List<TestCaseResult>();
            for (int i = 0; i < testCaseResults.Count; i++)
            {
                var id = 100000 + i;
                var testCaseResult = client
                    .GetTestResultByIdAsync(testRun.Project.Name, testRun.Id, id, ResultDetails.Iterations)
                    .GetAwaiter()
                    .GetResult();
                iterations.Add(testCaseResult);
            }

            // get
            return Gravity.Extensions.ObjectExtensions.Clone(iterations);
        }
        #endregion

        #region *** VSS Connection      ***
        /// <summary>
        /// Retrieves an HTTP client of the specified type.
        /// </summary>
        /// <typeparam name="T">The type of client to retrieve.</typeparam>
        /// <param name="connection">The connection to the parent host for this VSS connection.</param>
        /// <param name="numberOfAttempts">The number of attempts if getting client fails.</param>
        /// <returns>The client of the specified type.</returns>
        public static T GetClient<T>(this VssConnection connection, int numberOfAttempts) where T : VssHttpClientBase
        {
            // setup
            var attempt = 1;

            // iterate
            while (attempt < numberOfAttempts)
            {
                try
                {
                    return connection.GetClient<T>();
                }
                catch (Exception e) when (e != null)
                {
                    if (attempt == numberOfAttempts)
                    {
                        throw;
                    }
                    Thread.Sleep(3000);
                    attempt++;
                }
            }

            // default
            throw new ApplicationException(
                $"Get-Client -Type {typeof(T).FullName} = (InternalServerError|CannotGetClient)");
        }
        #endregion

        #region *** Test Manager Client ***
        public static TestAttachmentReference CreateAttachment(
            this TestManagementHttpClient client, TestAttachmentCreateModel createModel, int numberOfAttempts)
        {
            var message = "Add-Attachment" +
                $" -File {createModel.RequestModel.FileName}" +
                $" -Run {createModel.TestRun}" +
                $" -Result {createModel.TestResult}" +
                $" -Iteration {createModel.TestIteration} = ($[message])";

            // iterate
            var attempt = 1;
            while (attempt < numberOfAttempts)
            {
                try
                {
                    return client.CreateTestIterationResultAttachmentAsync(
                        createModel.RequestModel,
                        $"{createModel.Project}",
                        createModel.TestRun,
                        createModel.TestResult,
                        createModel.TestIteration).GetAwaiter().GetResult();
                }
                catch (Exception e) when (e != null)
                {
                    Trace.TraceError(message.Replace("$[message]", e.Message));
                }

                Thread.Sleep(3000);
                if (attempt++ == numberOfAttempts)
                {
                    break;
                }
            }

            // default
            return default;
        }
        #endregion

        #region *** Utilities           ***
        // gets the test severity or default
        private static string InvokeGetSeverity(RhinoTestCase testCase) => string.IsNullOrEmpty(testCase.Severity) || testCase.Severity == "0"
            ? "3 - Medium"
            : testCase.Severity;

        // get all open bugs for a test case & set context
        private static IEnumerable<WorkItem> InvokeGetOpenBugs(RhinoTestCase testCase, WorkItemTrackingHttpClient client)
        {
            try
            {
                // setup
                var project = InvokeGetProjectName(testCase);
                var closeStatus = InvokeGetStatesByCategory(client, project, "Bug", "Completed");
                var bugs = InvokeGetBugs(client, testCase).Where(i => !closeStatus.Contains($"{i.Fields["System.State"]}"));

                // exit conditions
                //var openBugs = !isClose
                //    ? bugs.Where(i => testCase.IsBugMatch(bug: i, assertDataSource: false))
                //    : bugs;
                var openBugs = bugs;
                if (!openBugs.Any())
                {
                    return Array.Empty<WorkItem>();
                }

                // build
                openBugs = client
                    .GetWorkItemsAsync(project, openBugs.Select(i => i.Id.ToInt()), null, expand: WorkItemExpand.All)
                    .GetAwaiter()
                    .GetResult();
                testCase.Context[AzureContextEntry.OpenBugs] = openBugs;

                // get
                return openBugs;
            }
            catch (Exception e) when (e != null)
            {
                return Array.Empty<WorkItem>();
            }
        }

        // get all bugs for a test case
        private static IEnumerable<WorkItem> InvokeGetBugs(WorkItemTrackingHttpClient client, RhinoTestCase testCase)
        {
            // get all related items
            var relations = client
                .GetWorkItemAsync(testCase.Key.ToInt(), expand: WorkItemExpand.All)
                .GetAwaiter()
                .GetResult()
                .Relations;

            if (relations?.Any() != true)
            {
                return Array.Empty<WorkItem>();
            }

            // filter related items by relevant relation > extract id
            var ids = relations
                .Where(i => i.Rel.Equals("Microsoft.VSTS.Common.TestedBy-Reverse", Compare))
                .Select(i => Regex.Match(i.Url, @"(?i)(?<=\/workItems\/)\d+").Value)
                .AsNumbers();

            // exit conditions
            if (!ids.Any())
            {
                return Array.Empty<WorkItem>();
            }

            // setup
            var items = new ConcurrentBag<WorkItem>();
            var groups = ids.Split(20);

            // fetch
            var options = new ParallelOptions { MaxDegreeOfParallelism = InvokeGetBucketSize(testCase) };
            Parallel.ForEach(groups, options, group =>
            {
                var range = client.GetWorkItemsAsync(group, expand: WorkItemExpand.All).GetAwaiter().GetResult();
                items.AddRange(range);
            });

            // get
            return items.Where(i => $"{i.Fields["System.WorkItemType"]}".Equals("Bug", Compare));
        }

        // gets the bucket size from the test context
        private static int InvokeGetBucketSize(RhinoTestCase testCase)
        {
            // setup
            var configuration = testCase.Context.Get(ContextEntry.Configuration, default(RhinoConfiguration));

            // exit conditions
            var isConfiguration = configuration != null;
            var isCapabilities = isConfiguration && configuration.Capabilities != null;
            var isBucket = isCapabilities && configuration.Capabilities.ContainsKey("bucketSize");

            if (!isBucket)
            {
                return Environment.ProcessorCount;
            }

            // parse
            var isValidBucket = int.TryParse($"{configuration.Capabilities["bucketSize"]}", out int bucketOut);
            return !isValidBucket || bucketOut < 0 ? Environment.ProcessorCount : bucketOut;
        }

        // gets the test priority or default
        private static string InvokeGetPriority(RhinoTestCase testCase) => string.IsNullOrEmpty(testCase.Priority)
            ? "3"
            : Regex.Match(testCase.Priority, @"\d+").Value;

        // gets a static list of custom fields from test context
        private static IDictionary<string, object> InvokeGetCustomFields(RhinoTestCase testCase)
        {
            // setup
            const string OptionsKey = RhinoConnectors.AzureTestManager + ":options";
            var options = testCase.Context.Get(OptionsKey, new Dictionary<string, object>());

            // get
            return options.Get(AzureCapability.CustomFields, new Dictionary<string, object>());
        }

        // gets azure project name from RhinoTestCase
        private static string InvokeGetProjectName(RhinoTestCase testCase)
        {
            // setup
            var item = testCase.Context.Get<WorkItem>(AzureContextEntry.WorkItem, default);

            // exit conditions
            if (item == default)
            {
                return string.Empty;
            }

            // get
            return item.Fields.Get("System.TeamProject", string.Empty);
        }

        // gets shared parameters as data table
        private static DataTable InvokeGetSharedParameters(WorkItem item)
        {
            // constants
            const string TcmParameters = "Microsoft.VSTS.TCM.Parameters";

            // setup conditions
            var isType = item.Fields["System.WorkItemType"].Equals("Shared Parameter");
            var isField = item.Fields.ContainsKey(TcmParameters);
            var isParameters = isField && !string.IsNullOrEmpty($"{item.Fields[TcmParameters]}");

            // exit conditions
            if (!isType || !isParameters || !$"{item.Fields[TcmParameters]}".IsXml())
            {
                return new DataTable();
            }

            // setup
            var xml = XDocument.Parse($"{item.Fields[TcmParameters]}");
            var table = new DataTable();

            // build
            var columns = xml.XPathSelectElements("//param").Select(i => new DataColumn(i.Value, typeof(string)));
            var rows = xml.XPathSelectElements("//dataRow").ToList();
            table.Columns.AddRange(columns.ToArray());
            table.AddRows(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                foreach (var parameter in rows[i].XPathSelectElements("kvp"))
                {
                    var key = parameter.Attribute("key").Value;
                    table.Rows[i][key] = parameter.Attribute("value").Value;
                }
            }

            // get
            return table;
        }

        // gets available states of a work-item by a category
        private static IEnumerable<string> InvokeGetStatesByCategory(WorkItemTrackingHttpClient client, string project, string itemType, string category) => client
            .GetWorkItemTypeStatesAsync(project, itemType)
            .GetAwaiter()
            .GetResult()
            .Where(i => i.Category.Equals(category, Compare))
            .Select(i => i.Name);
        #endregion
    }
}
