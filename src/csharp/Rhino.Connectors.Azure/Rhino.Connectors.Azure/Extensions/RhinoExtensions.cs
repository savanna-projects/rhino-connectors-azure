/*
 * CHANGE LOG - keep only last 5 threads
 * 
 * RESOURCES
 */
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

namespace Rhino.Connectors.Azure.Extensions
{
    /// <summary>
    /// Extension package for Rhino objects.
    /// </summary>
    internal static class RhinoExtensions
    {
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
        /// Gets the TestConfiguration.Id from connector options or -1 if not exists.
        /// </summary>
        /// <param name="configuration">RhinoConfiguration by which to get <see cref="VssCredentials"/>.</param>
        /// <returns>TestConfiguration.Id to use when executing the current configuration.</returns>
        public static int GetTestConfiguration(this RhinoConfiguration configuration)
        {
            // setup
            var optionsKey = $"{Connector.AzureTestManager}:options";
            var options = configuration.Capabilities.GetCastedValueOrDefault(optionsKey, new Dictionary<string, object>());

            // get
            return options.GetCastedValueOrDefault(AzureCapability.TestConfiguration, -1);
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
            return GetDocument(data);
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
            if(value == null || string.IsNullOrEmpty($"{value}"))
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

        private static JsonPatchDocument GetDocument(IDictionary<string, object> data)
        {
            // setup
            var patchDocument = new JsonPatchDocument();

            // iterate
            foreach (var field in data)
            {
                var operation = new JsonPatchOperation()
                {
                    Operation = Operation.Add,
                    Path = $"/fields/{field.Key}",
                    Value = $"{field.Value}"
                };
                patchDocument.Add(item: operation);
            }

            // results
            return patchDocument;
        }
    }
}