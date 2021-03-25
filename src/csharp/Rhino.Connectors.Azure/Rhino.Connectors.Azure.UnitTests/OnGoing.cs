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
            var json = File.ReadAllText(@"C:\Users\yaniv\Desktop\temp\bug.txt");
            var configuration = JsonSerializer.Deserialize<RhinoConfiguration>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            new AzureConnector(configuration).ProviderManager.DeleteTestRun("all");
            configuration.Execute(Utilities.Types);
        }
    }
}