using Gravity.Services.DataContracts;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Rhino.Api.Contracts.AutomationProvider;
using Rhino.Api.Contracts.Configuration;
using Rhino.Api.Extensions;
using Rhino.Connectors.Azure.Contracts;

using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Rhino.Connectors.Azure.UnitTests
{
    [TestClass]
    public class OnGoing
    {
        [TestMethod]
        public void A()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var c = File.ReadAllText(@"D:\garbage\rhino-issue.txt");
            var configuration = JsonSerializer.Deserialize<RhinoConfiguration>(c, options);
            var connector = new AzureConnector(configuration).Connect();//Execute();
            //connector.ProviderManager.GetPlugins();
            connector.ProviderManager.DeleteTestRun("all");
            connector.Execute();
        }
    }
}