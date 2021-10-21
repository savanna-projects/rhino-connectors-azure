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
        public void Test()
        {
            var json = File.ReadAllText(@"D:\garbage\azure.txt");
            var c = JsonSerializer.Deserialize<RhinoConfiguration>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            var connector = new AzureConnector(c);
            connector.ProviderManager.DeleteTestRun("all");
            connector.Invoke();
        }
    }
}