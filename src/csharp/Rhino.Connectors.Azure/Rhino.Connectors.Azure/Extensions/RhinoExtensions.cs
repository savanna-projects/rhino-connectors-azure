/*
 * CHANGE LOG - keep only last 5 threads
 * 
 * RESOURCES
 */
using Microsoft.TeamFoundation.TestManagement.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;

using Rhino.Api.Contracts.AutomationProvider;
using Rhino.Api.Contracts.Configuration;
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
        #region *** Test Case Results  ***
        /// <summary>
        /// Gets a basic <see cref="TestCaseResult"/> object.
        /// </summary>
        /// <param name="testCase">RhinoTestCase by which to create <see cref="TestCaseResult"/>.</param>
        /// <returns><see cref="TestCaseResult"/> object.</returns>
        public static TestCaseResult ToTestCaseResult(this RhinoTestCase testCase)
        {
            // setup
            int.TryParse(Regex.Match(testCase.Priority, @"\d+").Value, out int priority);
            var configuration = testCase.Context.GetCastedValueOrDefault("testConfigurationId", -1);

            // build
            var testCaseResults = new TestCaseResult
            {
                TestCaseTitle = testCase.Scenario,
                TestCase = new ShallowReference(id: testCase.Key),
                Comment = "Automatically created by Rhino engine.",
                ComputerName = Environment.MachineName,
                Outcome = nameof(TestOutcome.Paused),
                Priority = priority
            };

            // put
            if (configuration != -1)
            {
                testCaseResults.Configuration = new ShallowReference(id: $"{configuration}");
            }

            // get
            return testCaseResults;
        }
        #endregion

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

        // TODO: add comments
        public static T GetAzureCapability<T>(this RhinoConfiguration configuration, string capability, T defaultValue)
        {
            return DoGetAzureCapability(configuration, capability, defaultValue);
        }

        // TODO: add comments
        public static void AddAzureCapability(this RhinoConfiguration configuration, string capability, object value)
        {
            DoAddAzureCapability(configuration, capability, value);
        }

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

        #region *** JSON Test Document ***
        /// <summary>
        /// Gets a <see cref="JsonPatchDocument"/> ready for posting.
        /// </summary>
        /// <param name="testCase">RhinoTestCase to craeted <see cref="JsonPatchDocument"/> by.</param>
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
        /// <param name="testCase">RhinoTestCase to craeted <see cref="JsonPatchDocument"/> by.</param>
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
            int.TryParse(testCase.Priority, out int priorityOut);
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
            return $"<steps id=\"0\" last=\"{steps.Count}\">{string.Join(string.Empty, steps)}</steps>";
        }

        private static string GetActionHtml(RhinoTestStep step, int id)
        {
            // setup
            var expectedResults = step.Expected.Replace("\r", string.Empty).Replace("\n", "&lt;BR/&gt;").Trim();
            var type = string.IsNullOrEmpty(step.Expected) ? "ActionStep" : "ValidateStep";
            var expectedHtml = string.IsNullOrEmpty(step.Expected)
                ? "&lt;DIV&gt;&lt;P&gt;&amp;nbsp;&lt;/P&gt;&lt;/DIV&gt;"
                : "&lt;P&gt;[expected]&lt;/P&gt;";

            return
                $"<step id=\"{id}\" type=\"{type}\">" +
                $"<parameterizedString isformatted=\"true\">&lt;DIV&gt;&lt;DIV&gt;&lt;P&gt;{step.Action}&amp;nbsp;&lt;/P&gt;&lt;/DIV&gt;&lt;/DIV&gt;</parameterizedString>" +
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

        #region *** JSON Bug Document  ***
        public static JsonPatchDocument AsBugDocument(this RhinoTestCase testCase)
        {
            throw new NotImplementedException();
        }
        #endregion

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
    }
}