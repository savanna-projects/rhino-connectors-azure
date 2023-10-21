using Microsoft.VisualStudio.TestTools.UnitTesting;

using Rhino.Api.Contracts.Configuration;
using Rhino.Api.Extensions;

using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Rhino.Connectors.Azure.Text;

namespace Rhino.Connectors.Azure.UnitTests
{
    [TestClass]
    public class OnGoing
    {
        [TestMethod]
        public void Test()
        {
            //var json = File.ReadAllText(@"E:\garbage\busoft.txt");
            var json = File.ReadAllText(@"E:\garbage\busoft.txt");
            var c = JsonSerializer.Deserialize<RhinoConfiguration>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            var connector = new AzureConnector(c);
            //connector.ProviderManager.DeleteTestRun("all");
            //var a = connector.ProviderManager.GetTestCases("846");
            //var test = a.First();
            //var steps = test.Steps.ToList();
            //steps[0] = new Api.Contracts.AutomationProvider.RhinoTestStep
            //{
            //    Action = "go to url {https://www.google.com}"
            //};

            //test.Steps = steps;
            //connector.ProviderManager.UpdateTestCase(test);
            connector.Invoke();
        }

        [TestMethod]
        public void TestA()
        {
            //var a = "";
            //a = a.Replace("\"", @"\""");

            //var json = System.Text.Json.JsonSerializer.Deserialize<IDictionary<string, object>>("");
            //var endpoint = ((JsonElement)json["content"]).GetProperty("endpoint").GetString();

            var configuration = new RhinoConfiguration
            {
                DriverParameters = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["driver"] = "ChromeDriver",
                        ["driverBinaries"] = @"E:\Binaries\Automation\WebDrivers",
                        //["capabilities"] = new Dictionary<string, object>
                        //{
                        //    ["selenoid:options"] = new Dictionary<string, object>
                        //    {
                        //        ["enableVNC"] = true,
                        //        ["enableVideo"] = true,
                        //        ["name"] = "this.test.is.launched.by.rhino",
                        //    }
                        //}
                    }
                },
                Name = "Created by Rhino",
                Authentication = new()
                {
                    Username = "automation@rhino.api",
                    Password = "Aa123456!"
                },
                //TestsRepository = new[]
                //{
                //    /*"RP-753",*/ /*"RP-666"*/"RP-842"
                //},
                ServiceHooks = Array.Empty<RhinoServiceEvent>(),
                TestsRepository = new[]
                {
                    "/**--[ General Information ]----------------------------------------------------------------------\n/**|\n/**| Connect & Invoke Test Case\n/**| ==========================\n/**| 1. Use [Ctrl]+[Shift]+[P] to bring up the commands palette.\n/**| 2. Type 'Rhino' to filter out all 'Rhino' commands.\n/**| 3. Click on the command 'Rhino: Connect to Rhino, fetch Metadata & activate commands'.\n/**| 4. Use [Ctrl]+[Shift]+[P] to bring up the commands palette.\n/**| 4. Type 'Rhino' to filter out all 'Rhino' commands.\n/**| 5. Click on the command 'Rhino: Runs the automation test(s) from the currently open document'.\n/**|\n/**| View Documentation\n/**| ==================\n/**| 1. Right-Click to bring up the context menu.\n/**| 2. Click on 'Rhino: Show Documentation' command.\n/**|\n/**-----------------------------------------------------------------------------------------------\n/**\n[test-id]         875\n[test-scenario]   Verify That Results Can Be Retrieved When Searching by Any Keyword\n[test-categories] Sanity, Ui, Search\n[test-priority]   2\n[test-severity]   0\n[test-tolerance]  0%\n\n[test-actions]\ngo to url {https://www.google.com}\nsend keys {automation is fun} into {//textarea[@*='q']}\nclick on {//ul[@*='listbox']/li}\nwait {1500}\nclose browser\n\n[test-expected-results]\n[1] verify that {url} match {google}\n[4] verify that {count} of {//div[@*='g']} greater than {5}"
                },
                //TestsRepository = new[]
                //{
                //    "[test-id] 846\r\n" +
                //    "[test-scenario] Open a Web Site\r\n" +
                //    "[test-priority] high \r\n" +
                //    "[test-actions]\r\n" +
                //    "1. go to url {https://gravitymvctestapplication.azurewebsites.net/}\r\n" +
                //    "2. wait {1000}\r\n" +
                //    "3. register parameter {{$ --name:parameter --scope:session}} take {//a}  \r\n" +
                //    "4. close browser\r\n" +
                //    "[test-expected-results]\r\n" +
                //    "[1] {url} match {azure}"
                //},
                //ConnectorConfiguration = new()
                //{
                //    Collection = "http://localhost:8082",
                //    UserName = "admin",
                //    Password = "admin",
                //    Project = "RP",
                //    Connector = "ConnectorXrayText",
                //    DryRun = false,
                //    BugManager = true
                //},
                //Capabilities = new Dictionary<string, object>
                //{
                //    ["customFields"] = new Dictionary<string, object>
                //    {
                //        ["Fixed drop"] = "1.0.0.0",
                //        ["Fix Version/s"] = "1.0.0.0",
                //        ["No Field"] = 1,
                //        ["Test Checkbox"] = "Option 2",
                //        ["Multi-Branch"] = "No",
                //        ["Story Points"] = 10
                //    },
                //    ["syncFields"] = new[] { "Sync Fields" }
                //},
                ConnectorConfiguration = new()
                {
                    Collection = "https://dev.azure.com/u-systems",
                    Username = "gravity.api@outlook.com",
                    Password = "i4ewajrsfrwka5a7kyljjcq2af73n2znan7c4uvbv2u2xpkt3t4q",
                    Project = "Rhino Automation Demo",
                    Connector = "ConnectorAzureText",
                    DryRun = false,
                    BugManager = true,
                    AttachScreenshot = true
                },
                EngineConfiguration = new()
                {
                    ReturnPerformancePoints = true,
                    ReturnExceptions = true,
                    ReturnEnvironment = true,
                    MaxParallel = 2
                },
                ScreenshotsConfiguration = new()
                {
                    ReturnScreenshots = true,
                    KeepOriginal = false,
                    OnExceptionOnly = false
                }
            };

            var c = JsonSerializer.Serialize(configuration, new JsonSerializerOptions
            {
                DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            var conn = new AzureTextConnector(configuration);
            conn.Invoke();


            var co = new RhinoConfiguration();
            var co2 = co ?? new();
            co.Invoke(Utilities.Types);
        }
    }
}
