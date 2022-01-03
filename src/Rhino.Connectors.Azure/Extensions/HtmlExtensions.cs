/*
 * CHANGE LOG - keep only last 5 threads
 * 
 * RESOURCES
 */
using Gravity.Services.DataContracts;

using Newtonsoft.Json.Linq;

using Rhino.Api.Contracts.AutomationProvider;
using Rhino.Api.Extensions;
using Rhino.Connectors.Azure.Contracts;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using TestConfiguration = Microsoft.VisualStudio.Services.TestManagement.TestPlanning.WebApi.TestConfiguration;

namespace Rhino.Connectors.Azure.Extensions
{
    /// <summary>
    /// Extension package for creating HTML from Rhino objects.
    /// </summary>
    internal static class HtmlExtensions
    {
        #region *** Bug  ***
        /// <summary>
        /// Gets a bug HTML based on RhinoTestCase including context and results.
        /// </summary>
        /// <param name="testCase">RhinoTestCase to create a bug by.</param>
        /// <returns>An HTML bug representation of the RhinoTestCase.</returns>
        public static string GetBugHtml(this RhinoTestCase testCase)
        {
            // setup
            var title = GetBugTitle(testCase);
            var body = GetBugBody(testCase);
            var footer = GetBugFooter(testCase);

            // get
            return $"<div>{title}{body}{footer}</div>";
        }

        // gets bug title HTML
        private static string GetBugTitle(RhinoTestCase testCase)
        {
            // setup
            const string DateFormat = "M/d/yyyy  hh:mm tt";
            const string Html =
                "<hr style=\"border-color:black;\">" +
                "<table id=\"rhTitle\">" +
                "   <tbody>" +
                "       <tr>" +
                "           <td style=\"vertical-align:top;padding:2px 7px;font-weight:bold;\">$(DateTime)</td>" +
                "           <td style=\"vertical-align:top;padding:2px 7px 2px 10px;\">Bug filed on &quot;$(Title)&quot;</td>" +
                "       </tr>" +
                "   </tbody>" +
                "</table>" +
                "<hr style=\"border-color:black;\">";

            // build
            return Html
                .Replace("$(DateTime)", DateTime.Now.ToString(DateFormat))
                .Replace("$(Title)", testCase.Scenario);
        }

        // gets bug body (reproduce steps)
        private static string GetBugBody(RhinoTestCase testCase)
        {
            // setup
            const string Html =
                "<table id=\"rhActions\">" +
                "   <tbody>" +
                "       <tr>" +
                "           <td style=\"vertical-align:top;padding:2px 7px;font-weight:bold;\">Step no.</td>" +
                "           <td style=\"vertical-align:top;padding:2px 7px;font-weight:bold;\">Result</td>" +
                "           <td style=\"vertical-align:top;padding:2px 7px;font-weight:bold;\">Title</td>" +
                "       </tr>" +
                "       $(Actions)" +
                "   </tbody>" +
                "</table>";

            const string ActionHtml =
                "<tr>" +
                "   <td style=\"vertical-align:top;padding:2px 7px;font-weight:bold;\">$(ActionNumber).</td>" +
                "   <td style=\"vertical-align:top;padding:2px 7px;font-weight:bold;color:$(ResultColor);\">$(ActionResult)</td>" +
                "   <td style=\"vertical-align:top;padding:2px 7px;\" id=\"action$(ActionNumber)\">" +
                "       <div>$(Action)</div>" +
                "       <div style=\"padding-top:10px;\">Expected Result</div>" +
                "       <div>$(ExpectedResult)</div>" +
                "       <div style=\"padding-top:10px;\">Comment: $(Comment)</div>" +
                "   </td>" +
                "</tr>";

            // build
            var steps = testCase.Steps.ToArray();
            var actions = new List<string>();
            for (int i = 0; i < steps.Length; i++)
            {
                var action = GetBugAction(steps[i], ActionHtml, i + 1);
                actions.Add(action);
            }

            // get
            return Html.Replace("$(Actions)", string.Concat(actions));
        }

        private static string GetBugAction(RhinoTestStep testStep, string bugHtml, int actionNumber)
        {
            // setup
            var (color, phrase) = testStep.Actual ? ("green", "Passed") : ("red", "Failed");
            var html = bugHtml
                .Replace("$(ActionNumber)", $"{actionNumber}")
                .Replace("$(ResultColor)", color)
                .Replace("$(ActionResult)", phrase)
                .Replace("$(Action)", testStep.Action);

            // conditional
            html = string.IsNullOrEmpty(testStep.Expected)
                ? html
                    .Replace("<div style=\"padding-top:10px;\">Expected Result</div>", string.Empty)
                    .Replace("<div>$(ExpectedResult)</div>", string.Empty)
                : html.Replace("$(ExpectedResult)", testStep.Expected);

            // get
            return string.IsNullOrEmpty(testStep.ReasonPhrase)
                ? html.Replace("<div style=\"padding-top:10px;\">Comment: $(Comment)</div>", string.Empty)
                : html.Replace("$(Comment)", testStep.ReasonPhrase);
        }

        // get bug footer
        private static string GetBugFooter(RhinoTestCase testCase)
        {
            // setup
            const string Html =
                "<hr style=\"border-color:black;\">" +
                "<table id=\"rhConfiguration\" width=\"100%\">" +
                "   <tbody>" +
                "       <tr>" +
                "           <td style=\"font-weight:bold;\" width=\"255\">Test Configuration:</td>" +
                "           <td>$(Configuration)</td>" +
                "       </tr>" +
                "   </tbody>" +
                "</table>" +
                "<hr style=\"border-color:black;\">" +
                "$(Platform)" +
                "<hr style=\"border-color:black;\">" +
                "$(DataTable)" +
                "<hr style=\"border-color:black;\">" +
                "$(ApplicationUnderTest)";

            // setup
            var isKey = testCase.Context.ContainsKey(AzureContextEntry.TestConfiguration);
            var isConfiguration = isKey && testCase.Context[AzureContextEntry.TestConfiguration] is TestConfiguration;
            var configuration = isConfiguration
                ? (testCase.Context[AzureContextEntry.TestConfiguration] as TestConfiguration)?.Name ?? string.Empty
                : string.Empty;

            // get
            return Html
                .Replace("$(Configuration)", configuration)
                .Replace("$(Platform)", GetPlatform(testCase))
                .Replace("$(DataTable)", GetDataSource(testCase))
                .Replace("$(ApplicationUnderTest)", GetApplicationUnderTest(testCase));
        }

        private static string GetDataSource(RhinoTestCase testCase)
        {
            // exit conditions
            if (!testCase.DataSource.Any())
            {
                return string.Empty;
            }

            // setup
            const string Html =
                "<div style=\"font-weight:bold\">Data Iteration: <span id=\"rhIteration\"><b>$(Iteration)</b></span></div>" +
                "<table id=\"rhDataSource\" style=\"border:1px solid black;\" width=\"100%\" cellspacing=\"0\">" +
                "   <tbody>" +
                "       <tr style=\"font-weight:bold\">$(Headers)</tr>" +
                "       $(Data)" +
                "   </tbody>" +
                "</table>";
            var keys = new List<string>();
            var values = new List<string>();

            // build: keys
            foreach (var key in testCase.DataSource.First().Keys)
            {
                keys.Add("<th style=\"border:1px solid black;\">" + key + "</th>");
            }
            // build: data
            foreach (var row in testCase.DataSource)
            {
                var rowHtml = new StringBuilder("<tr>");
                foreach (var item in row)
                {
                    rowHtml.Append("<td style=\"border:1px solid black;\">").Append(item.Value).Append("</td>");
                }
                rowHtml.Append("</tr>");
                values.Add(rowHtml.ToString());
            }

            // get
            return Html
                .Replace("$(Headers)", string.Concat(keys))
                .Replace("$(Data)", string.Concat(values))
                .Replace("$(Iteration)", $"{testCase.Iteration}");
        }

        private static string GetPlatform(RhinoTestCase testCase)
        {
            // setup
            var driverParams = testCase.Context[ContextEntry.DriverParams] as IDictionary<string, object>;
            var platform = driverParams.ContainsKey("driver") ? $"{driverParams["driver"]}" : "N/A";

            // set header
            const string html =
                "<table width=\"100%\">" +
                "<tbody>" +
                "   <tr>" +
                "       <td style=\"font-weight:bold;\" width=\"255\">Platform (driver type):</td>" +
                "       <td id=\"rhPlatform\">$(Platform)</td>" +
                "   </tr>" +
                "</tbody>" +
                "</table>";

            // get
            return html.Replace("$(Platform)", platform);
        }

        private static string GetApplicationUnderTest(RhinoTestCase testCase)
        {
            // setup
            const string Capabilities = "capabilities";
            const string Options = "options";
            var driverParams = JObject.Parse(System.Text.Json.JsonSerializer.Serialize(testCase.Context[ContextEntry.DriverParams]));

            // setup conditions
            var isCapabilites = driverParams.ContainsKey(Capabilities);
            var isOptions = driverParams.ContainsKey(Options) && driverParams.SelectToken(Options) != null;

            // TODO: add aggregation method to calculate RhinoPlugins - currently not supported

            // build
            var environment = GetEnvironment(testCase);
            var capabilities = isCapabilites ? GetCapabilites(testCase) : string.Empty;
            var options = isOptions ? GetOptions(testCase) : string.Empty;

            // get
            return environment + capabilities + options;
        }

        private static bool IsWebAppAction(RhinoTestStep testStep)
        {
            return Regex.IsMatch(input: testStep.Action, pattern: "(?i)(go to url|navigate to|open|go to)");
        }

        private static string GetEnvironment(RhinoTestCase testCase)
        {
            // setup
            const string AppPath = "capabilities.app";
            var driverParams = JObject.Parse(System.Text.Json.JsonSerializer.Serialize(testCase.Context[ContextEntry.DriverParams]));

            // setup conditions
            var isWebApp = IsWebAppAction(testCase.Steps.First());
            var isCapabilites = driverParams.ContainsKey("capabilities");
            var isMobApp = !isWebApp && isCapabilites && driverParams.SelectToken(AppPath) != null;

            // build
            var driver = driverParams.ContainsKey("driver") ? $"{driverParams["driver"]}" : "N/A";
            var driverServer = driverParams.ContainsKey("driverBinaries") ? $"{driverParams["driverBinaries"]}" : "N/A";

            var application = isMobApp
                ? $"{driverParams.SelectToken(AppPath)}"
                : ((ActionRule)testCase.Steps.First(IsWebAppAction).Context[ContextEntry.StepAction]).Argument;

            // setup
            const string Html =
                "<div style=\"font-size:large;font-weight:bold;\">Application Under Test</div><br/>" +
                "<div style=\"font-weight:bold;\">Environment</div>" +
                "<table id=\"rhEnvironment\" width=\"100%\" style=\"border:1px solid black;\" cellspacing=\"0\">" +
                "<tbody>" +
                "   <tr style=\"font-weight:bold\">" +
                "       <th style=\"border:1px solid black;\">Name</td>" +
                "       <th style=\"border:1px solid black;\">Value</td>" +
                "   </tr>" +
                "   <tr>" +
                "       <td style=\"border:1px solid black;\">Driver</td>" +
                "       <td style=\"border:1px solid black;\">$(Driver)</td>" +
                "   </tr>" +
                "   <tr>" +
                "       <td style=\"border:1px solid black;\">Driver Server</td>" +
                "       <td style=\"border:1px solid black;\">$(DriverServer)</td>" +
                "   </tr>" +
                "   <tr>" +
                "       <td style=\"border:1px solid black;\">Application</td>" +
                "       <td style=\"border:1px solid black;\">$(Application)</td>" +
                "   </tr>" +
                "</tbody>" +
                "</table><br/>";

            // get
            return Html
                .Replace("$(Driver)", driver)
                .Replace("$(DriverServer)", driverServer)
                .Replace("$(Application)", application);
        }

        private static string GetCapabilites(RhinoTestCase testCase)
        {
            return GetDriverToken(testCase, "capabilities");
        }

        private static string GetOptions(RhinoTestCase testCase)
        {
            return GetDriverToken(testCase, "options");
        }

        private static string GetDriverToken(RhinoTestCase testCase, string token)
        {
            // setup
            var driverParams = testCase.Context[ContextEntry.DriverParams] as IDictionary<string, object>;

            // exit conditions
            if (!driverParams.ContainsKey(token))
            {
                return string.Empty;
            }

            // setup
            var json = System.Text.Json.JsonSerializer.Serialize(driverParams[token]);
            var data = System.Text.Json.JsonSerializer.Deserialize<IDictionary<string, object>>(json);

            // exit conditions
            if (data?.Any() != true)
            {
                return string.Empty;
            }

            // build
            json = System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                DictionaryKeyPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });

            // get
            var pascalToken = token.ToPascalCase();
            return
                $"<div style=\"font-weight:bold\">{pascalToken}</div>" +
                $"<pre id=\"rh{pascalToken}\">{json}</pre>";
        }
        #endregion

        #region *** Test ***
        /// <summary>
        /// Gets a collection of actions HTML based on RhinoTestCase.
        /// </summary>
        /// <param name="testCase">RhinoTestCase to create an HTML by.</param>
        /// <returns>An HTML representation of the RhinoTestCase.Steps.</returns>
        public static string GetStepsHtml(this RhinoTestCase testCase)
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
        #endregion
    }
}
