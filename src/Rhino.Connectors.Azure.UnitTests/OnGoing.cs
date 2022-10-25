using Microsoft.VisualStudio.TestTools.UnitTesting;

using Rhino.Api.Contracts.Configuration;
using Rhino.Api.Extensions;

using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

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
            var a = "";
            a = a.Replace("\"", @"\""");

            var json = System.Text.Json.JsonSerializer.Deserialize<IDictionary<string, object>>("");
            var endpoint = ((JsonElement)json["content"]).GetProperty("endpoint").GetString();

            var configuration = new RhinoConfiguration
            {
                DriverParameters = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["driver"] = "ChromeDriver",
                        ["driverBinaries"] = @"E:\AutomationEnvironment\WebDrivers",
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
                    "[test-id] 846\r\n" +
                    "[test-scenario] Open a Web Site\r\n" +
                    "[test-priority] high \r\n" +
                    "[test-actions]\r\n" +
                    "1. go to url {https://gravitymvctestapplication.azurewebsites.net/}\r\n" +
                    "2. wait {1000}\r\n" +
                    "3. register parameter {{$ --name:parameter --scope:session}} take {//a}  \r\n" +
                    "4. close browser\r\n" +
                    "[test-expected-results]\r\n" +
                    "[1] {url} match {azure}"
                },
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
                    Password = "qawsed1!",
                    Project = "Rhino Automation Demo",
                    Connector = "ConnectorAzureText",
                    DryRun = false,
                    BugManager = true
                },
                EngineConfiguration = new()
                {
                    ReturnPerformancePoints = true,
                    RetrunExceptions = true,
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

            var co = new RhinoConfiguration();
            var co2 = co ?? new();
        }
    }
}
