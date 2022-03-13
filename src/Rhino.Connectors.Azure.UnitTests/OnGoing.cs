using Microsoft.VisualStudio.TestTools.UnitTesting;

using Rhino.Api.Contracts.Configuration;

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
            var json = File.ReadAllText(@"E:\garbage\azure.txt");
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
