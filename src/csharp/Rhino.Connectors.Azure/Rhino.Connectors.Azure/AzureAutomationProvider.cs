/*
 * CHANGE LOG - keep only last 5 threads
 * 
 * RESOURCES
 * https://docs.microsoft.com/en-us/azure/devops/boards/queries/wiql-syntax?view=azure-devops
 * https://oshamrai.wordpress.com/vsts-rest-api-examples/
 */
using Gravity.Abstraction.Logging;
using Gravity.Extensions;

using Microsoft.TeamFoundation.TestManagement.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

using Newtonsoft.Json;

using Rhino.Api;
using Rhino.Api.Contracts.AutomationProvider;
using Rhino.Api.Contracts.Configuration;
using Rhino.Api.Contracts.Extensions;
using Rhino.Api.Extensions;
using Rhino.Connectors.Azure.Extensions;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using Utilities = Rhino.Api.Extensions.Utilities;

namespace Rhino.Connectors.Azure
{
    public class AzureAutomationProvider : ProviderManager
    {
        // constants
        private const string TypeField = "System.WorkItemType";
        private const string TestCase = "Test Case";
        private const string GetTestCasesMethod = "GetTestCases";
        private const string CapabilitesKey = "capabilites";
        private const StringComparison Compare = StringComparison.OrdinalIgnoreCase;
        private const Microsoft.VisualStudio.Services.TestManagement.TestPlanning.WebApi.SuiteEntryTypes TestEntryType = Microsoft.VisualStudio.Services.TestManagement.TestPlanning.WebApi.SuiteEntryTypes.TestCase;

        // members: clients
        private readonly WorkItemTrackingHttpClient wiClient;
        private readonly TestManagementHttpClient tmClient;
        private readonly Microsoft.VisualStudio.Services.TestManagement.TestPlanning.WebApi.TestPlanHttpClient tpClient;

        // members
        private readonly ILogger logger;

        #region *** Constructors      ***
        /// <summary>
        /// Creates a new instance of this Rhino.Api.Simulator.Framework.AutomationProvider.
        /// </summary>
        /// <param name="configuration">Rhino.Api.Contracts.Configuration.RhinoConfiguration to use with this provider.</param>
        public AzureAutomationProvider(RhinoConfiguration configuration)
            : this(configuration, Utilities.Types)
        { }

        /// <summary>
        /// Creates a new instance of this Rhino.Api.Simulator.Framework.AutomationProvider.
        /// </summary>
        /// <param name="configuration">Rhino.Api.Contracts.Configuration.RhinoConfiguration to use with this provider.</param>
        /// <param name="types">A collection of <see cref="Type"/> to load for this repository.</param>
        public AzureAutomationProvider(RhinoConfiguration configuration, IEnumerable<Type> types)
            : this(configuration, types, Utilities.CreateDefaultLogger(configuration))
        { }

        /// <summary>
        /// Creates a new instance of this Rhino.Api.Simulator.Framework.AutomationProvider.
        /// </summary>
        /// <param name="configuration">Rhino.Api.Contracts.Configuration.RhinoConfiguration to use with this provider.</param>
        /// <param name="types">A collection of <see cref="Type"/> to load for this repository.</param>
        /// <param name="logger">Gravity.Abstraction.Logging.ILogger implementation for this provider.</param>
        public AzureAutomationProvider(RhinoConfiguration configuration, IEnumerable<Type> types, ILogger logger)
            : base(configuration, types, logger)
        {
            // setup
            this.logger = logger;
            var credentials = configuration.GetVssCredentials();
            var connection = new VssConnection(new Uri(configuration.ConnectorConfiguration.Collection), credentials);
            BucketSize = configuration.GetCapability(ProviderCapability.BucketSize, 15);
            Configuration.Capabilities ??= new Dictionary<string, object>();

            // create clients
            wiClient = connection.GetClient<WorkItemTrackingHttpClient>();
            tmClient = connection.GetClient<TestManagementHttpClient>();
            tpClient = connection.GetClient<Microsoft.VisualStudio.Services.TestManagement.TestPlanning.WebApi.TestPlanHttpClient>();
        }
        #endregion

        #region *** Get: Test Cases   ***
        /// <summary>
        /// Returns a list of test cases for a project.
        /// </summary>
        /// <param name="ids">A list of test ids to get test cases by.</param>
        /// <returns>A collection of Rhino.Api.Contracts.AutomationProvider.RhinoTestCase</returns>
        public override IEnumerable<RhinoTestCase> OnGetTestCases(params string[] ids)
        {
            // constants
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

            // setup
            var testCases = new ConcurrentBag<RhinoTestCase>();
            var methods = GetType().GetMethods(Flags).Where(i => i.GetCustomAttribute<DescriptionAttribute>() != null);
            methods = methods.Where(i => i.GetCustomAttribute<DescriptionAttribute>().Description == GetTestCasesMethod);

            // exit conditions
            if (!methods.Any())
            {
                logger?.Warn("Get-TestCases = MethodsNotFound");
                return Array.Empty<RhinoTestCase>();
            }

            // fetch
            foreach (var method in methods)
            {
                var range = OnGetTestCases(method, ids);
                testCases.AddRange(range);
            }

            // log
            var distinctTestCases = testCases.DistinctBy(i => i.Key).ToList();
            logger?.Debug($"Get-TestCases -Distinct = {distinctTestCases.Count}");

            // get
            return distinctTestCases.Select(i => i.AddToContext(CapabilitesKey, Configuration.Capabilities));
        }

        private IEnumerable<RhinoTestCase> OnGetTestCases(MethodInfo method, IEnumerable<string> ids)
        {
            var testCases = new ConcurrentBag<RhinoTestCase>();
            try
            {
                var range = method.Invoke(this, new object[] { ids }) as IEnumerable<RhinoTestCase>;
                range = range == default ? Array.Empty<RhinoTestCase>() : range;
                testCases.AddRange(range);
            }
            catch (Exception e) when (e.GetBaseException() is VssResourceNotFoundException)
            {
                logger?.Warn("Get-TestCases = NotSupported");
            }
            catch (Exception e) when (e != null)
            {
                logger?.Error("Get-TestCases = Error");
            }
            return testCases;
        }

        // FACTORY
        [Description(GetTestCasesMethod)]
        [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Used by reflection")]
        private IEnumerable<RhinoTestCase> ByTestCases(IEnumerable<string> ids)
        {
            // setup
            var itemsToFind = ids.ToNumbers();

            // parse
            itemsToFind = GetTestCases(ids: itemsToFind).Select(i => i.Id.AsInt());

            // get
            return wiClient.GetRhinoTestCases(ids: itemsToFind);
        }

        [Description(GetTestCasesMethod)]
        [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Used by reflection")]
        private IEnumerable<RhinoTestCase> ByTestSuites(IEnumerable<string> ids)
        {
            // setup
            var itemsToFind = ids.ToNumbers();
            var options = new ParallelOptions { MaxDegreeOfParallelism = BucketSize };
            var testCases = new ConcurrentBag<int>();
            var testCasesResults = new ConcurrentBag<RhinoTestCase>();
            var project = Configuration.ConnectorConfiguration.Project;

            // get: all test cases ids
            Parallel.ForEach(itemsToFind, options, id =>
            {
                var range = tpClient
                    .GetSuiteEntriesAsync(project, id, TestEntryType)
                    .GetAwaiter()
                    .GetResult()
                    .Where(i => i.SuiteEntryType == TestEntryType)
                    .Select(i => i.Id);

                testCases.AddRange(range);
            });

            // get: rhino test cases
            var groups = testCases.Split(100);
            Parallel.ForEach(groups, options, group =>
            {
                var items = GetTestCases(group).Select(i => i.Id.AsInt());
                var range = wiClient.GetRhinoTestCases(ids: items);
                testCasesResults.AddRange(range);
            });

            // get
            return testCasesResults;
        }

        [Description(GetTestCasesMethod)]
        [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Used by reflection")]
        private IEnumerable<RhinoTestCase> ByTestPlans(IEnumerable<string> ids)
        {
            // setup
            var itemsToFind = ids.ToNumbers();
            var options = new ParallelOptions { MaxDegreeOfParallelism = BucketSize };
            var suitesByPlans = new ConcurrentBag<(int Plan, IEnumerable<int> Suites)>();
            var testCases = new ConcurrentBag<string>();
            var testCasesResults = new ConcurrentBag<RhinoTestCase>();
            var project = Configuration.ConnectorConfiguration.Project;

            // get: suites and plans
            Parallel.ForEach(itemsToFind, options, id =>
            {
                try
                {
                    var testSuites = tpClient.GetTestSuitesForPlanWithContinuationTokenAsync(project, id).GetAwaiter().GetResult();
                    var suites = testSuites.Select(i => i.Id);
                    suitesByPlans.Add((Plan: id, Suites: suites));
                }
                catch (TestObjectNotFoundException)
                {
                    logger?.Debug($"Get-ByTestPlans -Plan {id} = NotFound");
                }
            });
            logger?.Debug($"Get-ByTestPlans -Plan = {suitesByPlans.Count}");
            logger?.Debug($"Get-ByTestPlans -Suite = {suitesByPlans.SelectMany(i => i.Suites).Count()}");

            // get: rhino test cases
            Parallel.ForEach(suitesByPlans, options, suiteByPlan =>
            {
                Parallel.ForEach(suiteByPlan.Suites, options, suite =>
                {
                    var onTestCases = tmClient.GetTestCasesAsync(project, suiteByPlan.Plan, suite).GetAwaiter().GetResult();
                    var range = onTestCases.Select(i => i.Workitem.Id);
                    testCases.AddRange(range);
                });
            });

            // setup > log
            itemsToFind = testCases.ToNumbers();
            logger?.Debug($"Get-ByTestPlans -TestCase = {itemsToFind.Count()}");

            // get: rhino test cases
            var groups = itemsToFind.Split(100);
            Parallel.ForEach(groups, options, group =>
            {
                var items = GetTestCases(group).Select(i => i.Id.AsInt());
                var range = wiClient.GetRhinoTestCases(ids: items);
                testCasesResults.AddRange(range);
            });

            // get
            return testCasesResults;
        }

        [Description(GetTestCasesMethod)]
        [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Used by reflection")]
        private IEnumerable<RhinoTestCase> ByQueries(IEnumerable<string> queries)
        {
            // setup
            var options = new ParallelOptions { MaxDegreeOfParallelism = BucketSize };
            var testCases = new ConcurrentBag<int>();
            var project = Configuration.ConnectorConfiguration.Project;

            // get: suites and plans
            Parallel.ForEach(queries, options, query =>
            {
                try
                {
                    var wiql = new Wiql() { Query = query };
                    var queryResults = wiClient.QueryByWiqlAsync(wiql, project).GetAwaiter().GetResult();
                    var range = queryResults.WorkItems.Select(i => i.Id);
                    testCases.AddRange(range);
                }
                catch (VssServiceException e)
                {
                    logger?.Debug($"Get-ByQueries -Query {query} = {e.Message}");
                }
            });

            // setup > log
            var itemsToFind = testCases.ToList();
            logger?.Debug($"Get-ByQueries = {itemsToFind.Count}");

            // get
            var items = GetTestCases(testCases).Select(i => i.Id.AsInt());
            return wiClient.GetRhinoTestCases(ids: items);
        }

        // get test cases by id
        private IEnumerable<WorkItem> GetTestCases(IEnumerable<int> ids)
        {
            try
            {
                return wiClient
                    .GetWorkItemsAsync(ids, fields: null, asOf: null, expand: WorkItemExpand.All)
                    .GetAwaiter()
                    .GetResult()
                    .Where(i => $"{i.Fields[TypeField]}".Equals(TestCase, Compare));
            }
            catch (Exception e)
            {
                logger?.Error(e.Message, e);
                return Array.Empty<WorkItem>();
            }
        }
        #endregion

        #region *** Create: Test Case ***
        /// <summary>
        /// Creates a new test case under the specified automation provider.
        /// </summary>
        /// <param name="testCase">Rhino.Api.Contracts.AutomationProvider.RhinoTestCase by which to create automation provider test case.</param>
        /// <returns>The ID of the newly created entity.</returns>
        public override string CreateTestCase(RhinoTestCase testCase)
        {
            // setup
            var document = testCase.AsTestDocument();

            // post
            var item = wiClient
                .CreateWorkItemAsync(document, Configuration.ConnectorConfiguration.Project, TestCase)
                .GetAwaiter()
                .GetResult();
            var itemResult = JsonConvert.SerializeObject(item);
            var id = $"{item.Id.AsInt()}";

            // exit conditions
            if (!testCase.TestSuites.Any())
            {
                return itemResult;
            }

            // setup: add to suites
            var optionsKey = $"{Connector.AzureTestManager}:options";
            var options = testCase.Context.GetCastedValueOrDefault(optionsKey, new Dictionary<string, object>());
            var testPlan = options.GetCastedValueOrDefault("testPlan", 0);

            // add
            foreach (var testSuite in testCase.TestSuites.AsNumbers())
            {
                AddTestToSuite(testPlan, testSuite, testCase: id);
            }

            // get
            return id;
        }

        private void AddTestToSuite(int testPlan, int testSuite, string testCase)
        {
            // setup
            var project = Configuration.ConnectorConfiguration.Project;
            testPlan = testPlan <= 0 ? tpClient.GetPlanForSuite(project, testSuite) : 0;

            // put
            try
            {
                tmClient
                    .AddTestCasesToSuiteAsync(project, testPlan, testSuite, testCase)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception e)
            {
                var message = "Create-TestCase " +
                    $"-TestPlan {testPlan} " +
                    $"-TestSuite {testSuite} " +
                    $"-TestCase {testCase} = {e.GetBaseException().Message}";
                logger?.Error(message);
            }
        }
        #endregion
    }
}