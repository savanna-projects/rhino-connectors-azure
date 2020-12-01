/*
 * CHANGE LOG - keep only last 5 threads
 * 
 * RESOURCES
 */
using Microsoft.VisualStudio.Services.TestManagement.TestPlanning.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;

using Rhino.Api.Contracts.Configuration;
using Rhino.Connectors.Azure.Contracts;

using System;
using System.Collections.Generic;
using System.Linq;

namespace Rhino.Connectors.Azure.Extensions
{
    internal static class AzureUtilities
    {
        /// <summary>
        /// Creates a <see cref="JsonPatchDocument"/> from creating a <see cref="WorkItem"/>.
        /// </summary>
        /// <param name="data">A list of fileds to craete the <see cref="WorkItem"/> by.</param>
        /// <returns><see cref="JsonPatchDocument"/> ready for posting.</returns>
        public static JsonPatchDocument GetJsonPatchDocument(IDictionary<string, object> data)
        {
            return DoGetJsonPatchDocument(data);
        }

        /// <summary>
        /// Gets the default TestPlan <see cref="JsonPatchDocument"/>.
        /// </summary>
        /// <param name="configuration">RhinoConfiguration to create <see cref="JsonPatchDocument"/> by.</param>
        /// <returns><see cref="JsonPatchDocument"/> ready for posting.</returns>
        public static JsonPatchDocument GetTestPlanDocument(RhinoConfiguration configuration)
        {
            // setup
            var areaPath = configuration.GetAzureCapability(AzureCapability.AreaPath, string.Empty);
            var iterationPath = configuration.GetAzureCapability(AzureCapability.IterationPath, string.Empty);
            var data = new Dictionary<string, object>
            {
                ["Microsoft.VSTS.Scheduling.FinishDate"] = DateTime.Now.AddDays(7),
                ["Microsoft.VSTS.Scheduling.StartDate"] = DateTime.Now,
                ["System.TeamProject"] = configuration.ConnectorConfiguration.Project,
                ["System.Title"] = "Rhino - Default Automation Test Plan"
            };

            // configuration fields
            if (!string.IsNullOrEmpty(areaPath))
            {
                data["System.AreaPath"] = areaPath;
            }
            if (!string.IsNullOrEmpty(iterationPath))
            {
                data["System.IterationPath"] = iterationPath;
            }

            // get
            return DoGetJsonPatchDocument(data);
        }

        private static JsonPatchDocument DoGetJsonPatchDocument(IDictionary<string, object> data)
        {
            // setup
            var patchDocument = new JsonPatchDocument();

            // iterate
            var operations = data.Select(i => new JsonPatchOperation
            {
                Operation = Operation.Add,
                Path = $"/fields/{i.Key}",
                Value = $"{i.Value}"
            });

            // add
            patchDocument.AddRange(operations);

            // get
            return patchDocument;
        }
    }
}