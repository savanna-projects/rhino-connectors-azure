using Gravity.Services.DataContracts;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Rhino.Api.Contracts.AutomationProvider;
using Rhino.Api.Contracts.Configuration;
using Rhino.Connectors.Azure.Contracts;

using System.Collections.Generic;

namespace Rhino.Connectors.Azure.UnitTests
{
    [TestClass]
    public class OnGoing
    {
        // members: clients
        [TestMethod]
        public void TestMethod1()
        {
            Assert.IsTrue(true);
            var configu = new RhinoConfiguration
            {
                Name = "For Integration Testing",
                TestsRepository = new[]
                {
                    "3"
                    //"512","513","514","516"
                    //"select * from WorkItems where [System.WorkItemType] in group 'Test Case Category'"
                    //"SELECT [Id] FROM WorkItems WHERE [Work Item Type] = 'Test Case'"/*"RHIN-1"*//*"XT-58"*//*, "XT-8", "XT-9"*//*, "XT-1", "XT-6"*/
                },
                Authentication = new Authentication
                {
                    UserName = "automation@rhino.api",
                    Password = "Aa123456!"
                },
                ConnectorConfiguration = new RhinoConnectorConfiguration
                {
                    Collection = "http://localhost:8080/tfs/DefaultCollection",
                    Password = "qawsed2@",
                    UserName = "s_roe",
                    Project = "Rhino Automation Demo",
                    BugManager = true,
                    Connector = Connector.AzureTestManager,
                    AsOsUser = true

                    //Collection = "https://u-systems.visualstudio.com/",
                    //Password = "qawsed1!",
                    //UserName = "gravity.api@outlook.com",
                    //Project = "Rhino Automation Demo",
                    //BugManager = true,
                    //Connector = Connector.AzureTestManager,
                    //AsOsUser = false,
                    //DryRun = false
                },
                DriverParameters = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["driver"] = "ChromeDriver",
                        ["driverBinaries"] = @"D:\automation_env\web_drivers",
                        ["capabilities"] = new Dictionary<string, object>
                        {
                            //["build"] = "Test Build",
                            //["project"] = "Bug Manager"
                        },
                        ["options"] = new Dictionary<string, object>
                        {
                            //["arguments"] = new[]
                            //{
                            //    "--ignore-certificate-errors",
                            //    "--disable-popup-blocking",
                            //    "--incognito"
                            //}
                        }
                    }
                },
                ScreenshotsConfiguration = new RhinoScreenshotsConfiguration
                {
                    KeepOriginal = true,
                    ReturnScreenshots = true
                },
                EngineConfiguration = new RhinoEngineConfiguration
                {
                    MaxParallel = 8
                },
                Capabilities = new Dictionary<string, object>
                {
                    ["bucketSize"] = 40,
                    [$"{Connector.AzureTestManager}:options"] = new Dictionary<string, object>
                    {
                        [AzureCapability.TestPlan] = 10,
                        //[AzureCapability.TestSuite] = 20,
                        [AzureCapability.CustomFields] = new Dictionary<string, object>
                        {
                            ["Key"] = "Value"
                        }
                    }
                }
            };

            var t = new RhinoTestCase
            {
                Scenario = "Created by User - DataDriven",
                Priority = "2",
                DataSource = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["address"] = "https://www.google.com",
                        ["keyword"] = "automation"
                    },
                    new Dictionary<string, object>
                    {
                        ["address"] = "https://www.bing.com",
                        ["keyword"] = "automation"
                    }
                },
                Steps = new[]
                {
                    new RhinoTestStep
                    {
                        Action = "1. go to url {@address}"
                    },
                    new RhinoTestStep
                    {
                        Action = "2. send keys {@keyword} into {//input[@name='q']}"
                    },
                    new RhinoTestStep
                    {
                        Action = "3. wait {3000}",
                        Expected = "{attribute} from {value} of {//input[@name='q']} match {@keyword}"
                    },
                    new RhinoTestStep
                    {
                        Action = "4. close browser"
                    }
                }
            };

            var a = new AzureConnector(configu).Execute();
            //new AzureConnector(configu).ProviderManager.DeleteTestRun("all");
            //new AzureConnector(configu).ProviderManager.CreateTestCase(t);
        }
    }
}