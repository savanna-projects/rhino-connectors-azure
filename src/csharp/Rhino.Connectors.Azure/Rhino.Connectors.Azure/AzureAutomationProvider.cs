/*
 * CHANGE LOG - keep only last 5 threads
 * 
 * RESOURCES
 * https://docs.microsoft.com/en-us/azure/devops/boards/queries/wiql-syntax?view=azure-devops
 * https://oshamrai.wordpress.com/vsts-rest-api-examples/
 * https://stackoverflow.com/questions/44495814/how-to-add-test-results-to-a-test-run-in-vsts-using-rest-api-programatically
 * https://developercommunity.visualstudio.com/content/problem/77426/need-a-tutorial-how-to-create-test-run-and-post-te.html
 * https://developercommunity.visualstudio.com/content/problem/602005/rest-api-to-post-steps-and-steps-result-of-test-ca.html
 * https://stackoverflow.com/questions/44697226/how-to-add-update-individual-result-to-each-test-step-in-testcase-of-vsts-tfs-pr
 */
using Gravity.Abstraction.Logging;
using Gravity.Extensions;

using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.TestManagement.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.TestManagement.TestPlanning.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch;

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

using Rhino.Api;
using Rhino.Api.Contracts.AutomationProvider;
using Rhino.Api.Contracts.Configuration;
using Rhino.Api.Extensions;
using Rhino.Connectors.Azure.Contracts;
using Rhino.Connectors.Azure.Extensions;
using Rhino.Connectors.Azure.Framework;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using TestPoint = Microsoft.TeamFoundation.TestManagement.WebApi.TestPoint;
using Utilities = Rhino.Api.Extensions.Utilities;
using WorkItem = Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models.WorkItem;

namespace Rhino.Connectors.Azure
{
    public class AzureAutomationProvider : ProviderManager
    {
        // constants
        private const string TypeField = "System.WorkItemType";
        private const string TestCase = "Test Case";
        private const string GetTestCasesMethod = "GetTestCases";
        private const string CapabilitesKey = "capabilities";
        private const StringComparison Compare = StringComparison.OrdinalIgnoreCase;
        private const SuiteEntryTypes TestEntryType = SuiteEntryTypes.TestCase;

        // members: clients
        private readonly WorkItemTrackingHttpClient itemManagement;
        private readonly TestManagementHttpClient testManagement;
        private readonly TestPlanHttpClient planManagement;
        private readonly VssConnection connection;
        private readonly TeamProjectReference projectReference;

        // members
        private readonly ILogger logger;
        private readonly ParallelOptions options;
        private readonly AzureBugsManager bugsManager;
        private readonly string project;

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
            connection = new VssConnection(new Uri(configuration.ConnectorConfiguration.Collection), credentials);
            project = configuration.ConnectorConfiguration.Project;
            bugsManager = new AzureBugsManager(connection);
            BucketSize = configuration.GetCapability(ProviderCapability.BucketSize, 15);
            Configuration.Capabilities ??= new Dictionary<string, object>();
            options = new ParallelOptions { MaxDegreeOfParallelism = BucketSize };

            // cache project
            var projectManagement = connection.GetClient<ProjectHttpClient>();
            projectReference = projectManagement.GetProjects(ProjectState.All)
                .GetAwaiter()
                .GetResult()
                .FirstOrDefault(i => i.Name.Equals(Configuration.ConnectorConfiguration.Project, StringComparison.OrdinalIgnoreCase));
            TestRun.Context[AzureContextEntry.Project] = projectReference;

            // create clients
            itemManagement = connection.GetClient<WorkItemTrackingHttpClient>();
            testManagement = connection.GetClient<TestManagementHttpClient>();
            planManagement = connection.GetClient<TestPlanHttpClient>();
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
            var invalidTestCases = testCases
                .Where(i => i.Steps.Any(i => string.IsNullOrEmpty(i.Action)))
                .Select(i => i.Key)
                .Distinct();
            if (invalidTestCases.Any())
            {
                logger?.Warn($"Get-TestCases -Invalid = {JsonConvert.SerializeObject(invalidTestCases)}");
            }

            // log
            var distinctTestCases = testCases.DistinctBy(i => i.Key).Where(i => !invalidTestCases.Contains(i.Key)).ToList();
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
                range ??= Array.Empty<RhinoTestCase>();
                testCases.AddRange(range);
            }
            catch (Exception e) when (e.GetBaseException() is VssResourceNotFoundException)
            {
                logger?.Warn("Get-TestCases = NotSupported");
            }
            catch (Exception e) when (e != null)
            {
                logger?.Error($"Get-TestCases = {e.GetBaseException().Message}");
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
            itemsToFind = GetTestCases(ids: itemsToFind).Select(i => i.Id.ToInt());

            // get
            return itemManagement.GetRhinoTestCases(ids: itemsToFind);
        }

        [Description(GetTestCasesMethod)]
        [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Used by reflection")]
        private IEnumerable<RhinoTestCase> ByTestSuites(IEnumerable<string> ids)
        {
            // setup
            var itemsToFind = ids.ToNumbers();
            var testCases = new ConcurrentBag<int>();
            var testCasesResults = new ConcurrentBag<RhinoTestCase>();

            // get: all test cases ids
            Parallel.ForEach(itemsToFind, options, id =>
            {
                var range = planManagement
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
                var items = GetTestCases(group).Select(i => i.Id.ToInt());
                var range = itemManagement.GetRhinoTestCases(ids: items);
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
            var suitesByPlans = new ConcurrentBag<(int Plan, IEnumerable<int> Suites)>();
            var testCases = new ConcurrentBag<string>();
            var testCasesResults = new ConcurrentBag<RhinoTestCase>();

            // get: suites and plans
            Parallel.ForEach(itemsToFind, options, id =>
            {
                try
                {
                    var testSuites = planManagement.GetTestSuitesForPlanWithContinuationTokenAsync(project, id).GetAwaiter().GetResult();
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
                    var onTestCases = testManagement.GetTestCasesAsync(project, suiteByPlan.Plan, suite).GetAwaiter().GetResult();
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
                var items = GetTestCases(group).Select(i => i.Id.ToInt());
                var range = itemManagement.GetRhinoTestCases(ids: items);
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
            var testCases = new ConcurrentBag<int>();

            // get: suites and plans
            Parallel.ForEach(queries, options, query =>
            {
                try
                {
                    var isGuid = Guid.TryParse(query, out Guid queryOut);
                    var wiql = new Wiql() { Query = query };

                    var queryResults = isGuid
                        ? itemManagement.QueryByIdAsync(project, queryOut).GetAwaiter().GetResult()
                        : itemManagement.QueryByWiqlAsync(wiql, project).GetAwaiter().GetResult();

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
            var items = GetTestCases(testCases).Select(i => i.Id.ToInt());
            return itemManagement.GetRhinoTestCases(ids: items);
        }

        // get test cases by id
        private IEnumerable<WorkItem> GetTestCases(IEnumerable<int> ids)
        {
            try
            {
                return itemManagement
                    .GetWorkItemsAsync(ids, fields: null, asOf: null, expand: WorkItemExpand.All)
                    .GetAwaiter()
                    .GetResult()
                    .Where(i => $"{i.Fields[TypeField]}".Equals(TestCase, Compare))
                    .ToArray();
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
            var document = testCase.GetTestDocument(Operation.Add, "Automatically created by Rhino engine.");

            // post
            var item = itemManagement
                .CreateWorkItemAsync(document, Configuration.ConnectorConfiguration.Project, TestCase)
                .GetAwaiter()
                .GetResult();
            var itemResult = JsonConvert.SerializeObject(item);
            var id = $"{item.Id.ToInt()}";

            // exit conditions
            if (!testCase.TestSuites.Any())
            {
                return itemResult;
            }

            // setup: add to suites
            var testPlan = Configuration.GetAzureCapability(AzureCapability.TestPlan, 0);

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
            testPlan = testPlan <= 0 ? planManagement.GetPlanForSuite(project, testSuite) : 0;
            logger?.Debug($"Get-PlanForSuite -Project {project} -TestSuite {testSuite} = {testPlan}");

            // put
            try
            {
                testManagement
                    .AddTestCasesToSuiteAsync(project, testPlan, testSuite, testCase)
                    .GetAwaiter()
                    .GetResult();

                logger?.Debug("Add-TestToSuite" +
                    $"-Project {project} " +
                    $"-TestPlan {testPlan} " +
                    $"-TestSuite {testSuite} " +
                    $"-TestCase {testCase} = OK");
            }
            catch (Exception e)
            {
                logger?.Error("Add-TestToSuite " +
                    $"-TestPlan {testPlan} " +
                    $"-TestSuite {testSuite} " +
                    $"-TestCase {testCase} = {e.GetBaseException().Message}");
            }
        }
        #endregion

        #region *** Configuration     ***
        /// <summary>
        /// Implements a mechanism of setting a testing configuration for an automation provider.
        /// </summary>
        /// <remarks>Use this method for <see cref="SetConfiguration"/> customization.</remarks>
        public override void OnSetConfiguration()
        {
            // setup
            const string ConfigurationName = "Rhino - Automation Configuration";

            // from capabilities
            var id = GetConfigurationId();
            if (id != -1)
            {
                AddConfigurationToTestContext(GetConfiguration(id));
                return;
            }

            // get or create
            try
            {
                var testConfiguration = planManagement
                    .GetTestConfigurationsWithContinuationTokenAsync(Configuration.ConnectorConfiguration.Project)
                    .GetAwaiter()
                    .GetResult()
                    .FirstOrDefault(i => i.Name.Equals(ConfigurationName, Compare));

                if (testConfiguration != default)
                {
                    TestRun.Context[AzureContextEntry.TestConfiguration] = testConfiguration;
                    AddConfigurationToTestContext(testConfiguration);
                    return;
                }

                var parameters = new TestConfigurationCreateUpdateParameters()
                {
                    Description = "Automation configuration for running test cases under Rhino API.",
                    IsDefault = false,
                    State = TestConfigurationState.Active,
                    Name = ConfigurationName
                };
                testConfiguration = planManagement
                    .CreateTestConfigurationAsync(parameters, Configuration.ConnectorConfiguration.Project)
                    .GetAwaiter()
                    .GetResult();

                logger?.Debug($"Set-Configuration -Name {ConfigurationName} -Create = {testConfiguration.Id}");
                TestRun.Context[AzureContextEntry.TestConfiguration] = testConfiguration;

                id = testConfiguration.Id;
            }
            catch (Exception e) when (e.GetBaseException() is VssResourceNotFoundException)
            {
                logger?.Warn($"Set-Configuration -Name {ConfigurationName} = NotSupported");
            }
            catch (Exception e) when (e != null)
            {
                logger?.Error($"Set-Configuration -Name {ConfigurationName} = {e.GetBaseException().Message}");
            }

            // put to context
            var configuration = GetConfiguration(id);
            AddConfigurationToTestContext(configuration);
        }

        private Microsoft.VisualStudio.Services.TestManagement.TestPlanning.WebApi.TestConfiguration GetConfiguration(int id)
        {
            try
            {
                return planManagement.GetTestConfigurationByIdAsync(project, id).GetAwaiter().GetResult();
            }
            catch (Exception e) when (e != null)
            {
                logger?.Warn($"Get-Configuration -Server {connection.Uri} -Project {project} = NotSupported");
            }
            return default;
        }

        private int GetConfigurationId()
        {
            // setup
            TestRun ??= new RhinoTestRun();
            TestRun.Context ??= new Dictionary<string, object>();

            // from capabilities
            var id = Configuration.GetAzureCapability(AzureCapability.TestConfiguration, -1);

            // set
            if (id != -1)
            {
                logger?.Debug($"Get-Configuration -FromCapabilites {id} = OK");
            }
            else
            {
                logger?.Debug($"Set-Configuration -Default {id} = OK");
            }

            // get
            return id;
        }

        private void AddConfigurationToTestContext(Microsoft.VisualStudio.Services.TestManagement.TestPlanning.WebApi.TestConfiguration configuration)
        {
            if (configuration == null)
            {
                return;
            }

            // test run
            TestRun.Context[AzureContextEntry.ConfigurationId] = configuration.Id;
            TestRun.Context[AzureContextEntry.TestConfiguration] = configuration;

            // test cases
            foreach (var testCase in TestRun.TestCases)
            {
                testCase.Context[AzureContextEntry.ConfigurationId] = configuration.Id;
                testCase.Context[AzureContextEntry.TestConfiguration] = configuration;
            }
        }
        #endregion

        #region *** Create: Test Run  ***
        /// <summary>
        /// Creates an automation provider test run entity. Use this method to implement the automation
        /// provider test run creation and to modify the loaded Rhino.Api.Contracts.AutomationProvider.RhinoTestRun.
        /// </summary>
        /// <param name="testRun">Rhino.Api.Contracts.AutomationProvider.RhinoTestRun object to modify before creating.</param>
        /// <returns>Rhino.Api.Contracts.AutomationProvider.RhinoTestRun based on provided test cases.</returns>
        public override RhinoTestRun OnCreateTestRun(RhinoTestRun testRun)
        {
            // create
            try
            {
                // 1. Create Run
                var azureTestRun = DoCreateTestRun(testRun);

                // 2. Get Created Run
                var testCaseResults = azureTestRun.GetTestRunResults(testManagement);

                // 3. Add Iterations                
                foreach (var testCaseResult in testCaseResults)
                {
                    var testCases = testRun.TestCases.Where(i => i.Key == testCaseResult.TestCase.Id);
                    var iterationDetails = testCases.Select(i => i.ToTestIterationDetails(false));
                    testCaseResult.IterationDetails.AddRange(iterationDetails);
                }

                testManagement
                    .UpdateTestResultsAsync(testCaseResults.ToArray(), project, azureTestRun.Id)
                    .GetAwaiter()
                    .GetResult();

                // 5. log
                logger?.Debug($"Create-TestRun = {azureTestRun.Url}");
            }
            catch (Exception e) when (e != null)
            {
                Configuration.ConnectorConfiguration.DryRun = true;
                logger?.Error($"Create-TestRun = {e.GetBaseException().Message}");
            }

            // get
            return testRun;
        }

        private TestRun DoCreateTestRun(RhinoTestRun testRun)
        {
            // setup
            var runCreateModel = GetCreateModel();

            // create
            var onTestRun = testManagement
                .CreateTestRunAsync(runCreateModel, Configuration.ConnectorConfiguration.Project)
                .GetAwaiter()
                .GetResult();

            // add results
            testRun.Key = $"{onTestRun.Id}";
            testRun.Context[AzureContextEntry.TestRun] = onTestRun;

            // get
            return onTestRun;
        }

        private RunCreateModel GetCreateModel()
        {
            // setup
            var plan = GetFromOptions(AzureCapability.TestPlan);
            _ = int.TryParse(TestRun.TestCases.FirstOrDefault()?.Key, out int testCaseId);

            try
            {
                // setup
                plan = plan == -1 ? planManagement.GetPlanForTest(project, testCaseId) : plan;
                Configuration.AddAzureCapability(AzureCapability.TestPlan, plan);
                logger?.Debug($"Get-PlanForTest -Project {project} -TestCase {testCaseId} = {plan}");

                //  test points
                var points = GetAllTestPoints().Select(i => i.Id).ToArray();

                // get
                return new RunCreateModel(name: TestRun.Title, pointIds: points, plan: new ShallowReference(id: $"{plan}"));
            }
            catch (Exception e) when (e != null)
            {
                var message = $"Get-CreateModel -Plan {plan} = {e.GetBaseException().Message}";
                logger.Fatal(message, e);
                throw new InvalidOperationException(message, innerException: e);
            }
        }

        private IEnumerable<TestPoint> GetAllTestPoints()
        {
            // setup
            var testCasesGroups = TestRun.TestCases.Split(100);
            var testPoints = new ConcurrentBag<TestPoint>();

            // build
            Parallel.ForEach(testCasesGroups, options, testCases =>
            {
                var ids = testCases.Select(i => i.Key).Distinct().AsNumbers().ToList();
                var filter = new PointsFilter { TestcaseIds = ids };
                var query = new TestPointsQuery() { PointsFilter = filter };

                var range = testManagement.GetPointsByQueryAsync(query, project).GetAwaiter().GetResult().Points;
                range = ValidatePoints(range).ToList();

                testPoints.AddRange(range);
            });

            // get
            logger?.Debug($"Get-AllTestPoints = {testPoints.Count}");
            return testPoints;
        }

        private IEnumerable<TestPoint> ValidatePoints(IEnumerable<TestPoint> testPoints)
        {
            // setup
            var testPlan = GetFromOptions(AzureCapability.TestPlan);
            var testSuiteOption = GetFromOptions(AzureCapability.TestSuite);
            var isTestSuiteProvided = testSuiteOption != -1;
            var testPointsResults = new ConcurrentBag<TestPoint>();

            // build
            Parallel.ForEach(testPoints, options, testPoint =>
            {
                var onTestSuite = Regex.Match(testPoint.Url, pattern: @"(?<=Suites/)\d+").Value;
                _ = int.TryParse(onTestSuite, out int testSuite);

                try
                {
                    var point = testManagement
                         .GetPointAsync(project, testPlan, testSuite, testPoint.Id)
                         .GetAwaiter()
                         .GetResult();
                    if (!isTestSuiteProvided || testSuiteOption == testSuite)
                    {
                        testPointsResults.Add(point);
                    }
                }
                catch (Exception e) when (e != null)
                {
                    logger?.Debug($"Confirm-TestPoint -Id {testPoint.Id} = {e.Message}");
                }
            });

            // get
            return testPointsResults;
        }

        private int GetFromOptions(string optionsEntry)
        {
            // setup
            const string OptionsKey = Connector.AzureTestManager + ":options";
            var azureOptions = CSharpExtensions.Get(Configuration.Capabilities, OptionsKey, new Dictionary<string, object>());

            // exit conditions
            if (!azureOptions.ContainsKey(optionsEntry))
            {
                return -1;
            }

            // get
            var found = int.TryParse($"{azureOptions[optionsEntry]}", out int intOut);
            return found ? intOut : -1;
        }
        #endregion

        #region *** Update: Test Run  ***
        /// <summary>
        /// Completes automation provider test run results, if any were missed or bypassed.
        /// </summary>
        /// <param name="testRun">Rhino.Api.Contracts.AutomationProvider.RhinoTestRun results object to complete by.</param>
        public override void OnCompleteTestRun(RhinoTestRun testRun)
        {
            // setup
            _ = int.TryParse(testRun.Key, out int runIdOut);
            var model = new RunUpdateModel(state: nameof(TestRunState.Completed));

            // get test results
            var azureTestRun = testManagement.GetTestRunByIdAsync(project, testRun.Key.ToInt()).GetAwaiter().GetResult();
            var runResults = azureTestRun.GetTestRunResults(testManagement).Where(i => i.State != nameof(TestRunState.Completed)).ToList();

            // setup conditions
            var isRunResultsOk = runResults.Count == 0;
            var isRunStateOk = azureTestRun.State == nameof(TestRunState.Completed);

            // exit condition
            if (isRunResultsOk && isRunStateOk)
            {
                return;
            }
            if (isRunResultsOk && !isRunStateOk)
            {
                testManagement.UpdateTestRunAsync(model, project, azureTestRun.Id).GetAwaiter().GetResult();
                return;
            }

            // iterate
            var results = new List<TestCaseResult>();
            for (int i = 0; i < runResults.Count; i++)
            {
                var result = OnCompleteTestRun(testRun, runResults[i]);
                if (result != default)
                {
                    results.Add(result);
                }
            }

            // put
            testManagement.UpdateTestResultsAsync(results.ToArray(), project, runIdOut).GetAwaiter().GetResult();

            // setup
            azureTestRun = testManagement.GetTestRunByIdAsync(project, testRun.Key.ToInt()).GetAwaiter().GetResult();
            isRunStateOk = azureTestRun.State == nameof(TestRunState.Completed);

            // put
            if (!isRunStateOk)
            {
                testManagement.UpdateTestRunAsync(model, project, azureTestRun.Id).GetAwaiter().GetResult();
            }
        }

        private static TestCaseResult OnCompleteTestRun(RhinoTestRun testRun, TestCaseResult caseResult)
        {
            // setup
            var result = Gravity.Extensions.ObjectExtensions.Clone(caseResult);
            var testCase = testRun.TestCases.FirstOrDefault(i => i.Key == result.TestCase.Id);

            // exit conditions
            if (testCase == default)
            {
                return default;
            }

            // outcome
            result.Outcome = testRun.TestCases.Any(i => i.Key == result.TestCase.Id && !i.Actual)
                ? nameof(TestOutcome.Failed)
                : nameof(TestOutcome.Passed);
            if (testRun.TestCases.All(i => i.Key == result.TestCase.Id && i.Inconclusive))
            {
                result.Outcome = nameof(TestOutcome.Inconclusive);
            }

            // state & complete date
            result.State = nameof(TestRunState.Completed);
            result.CompletedDate = testRun
                .TestCases
                .Where(i => i.Key == result.TestCase.Id)
                .OrderByDescending(i => i.End)
                .First()
                .End
                .ToAzureDate(true);

            // get
            return result;
        }
        #endregion

        #region *** Put: Test Run     ***
        /// <summary>
        /// Updates a single test results iteration under automation provider.
        /// </summary>
        /// <param name="testCase">Rhino.Api.Contracts.AutomationProvider.RhinoTestCase by which to update results.</param>
        public override void OnUpdateTestResult(RhinoTestCase testCase)
        {
            // setup
            var outcome = CSharpExtensions.Get(testCase.Context, AzureContextEntry.Outcome, nameof(TestOutcome.Unspecified));
            var results = GetTestCaseResult(testCase);

            // exit conditions
            if (!results.Any())
            {
                logger?.Debug($"Update-TestResults -Test {testCase.Key} = NotFound");
                return;
            }

            // build
            var onResults = new List<TestCaseResult>();
            foreach (var result in results)
            {
                // setup
                result.Outcome = outcome;

                // create
                var iteration = testCase.ToTestIterationDetails(setOutcome: true);

                // status
                if (outcome == nameof(TestOutcome.Passed) || outcome == nameof(TestOutcome.Failed))
                {
                    SetIterationPassOrFail(testCase, result, iteration);
                }

                // timespan
                result.StartedDate = testCase.Start.ToAzureDate(addMilliseconds: true);
                result.CompletedDate = testCase.End.ToAzureDate(addMilliseconds: true);
                iteration.StartedDate = testCase.Start.ToAzureDate(addMilliseconds: true);
                iteration.CompletedDate = testCase.End.ToAzureDate(addMilliseconds: true);

                // set
                onResults.Add(result);
            }

            // update
            testManagement
                .UpdateTestResultsAsync(onResults.ToArray(), project, int.Parse(testCase.TestRunKey))
                .GetAwaiter()
                .GetResult();
            logger?.Debug($"Update-TestResults -Test {testCase.Key} = (Ok, {onResults.Count})");
        }

        private IEnumerable<TestCaseResult> GetTestCaseResult(RhinoTestCase testCase)
        {
            // setup
            _ = int.TryParse(testCase.TestRunKey, out int runIdOut);

            // get test results
            var partialResults = testManagement.GetTestResultsAsync(project, runIdOut)
                .GetAwaiter()
                .GetResult()
                .Where(i => i.TestCase.Id.Equals(testCase.Key));

            // exit conditions
            if (partialResults?.Any() == false)
            {
                logger?.Debug($"Get-TestResults -TestCase {testCase.Key} = NotFound");
                return default;
            }

            // context
            testCase.Context[AzureContextEntry.TestResultId] = partialResults;

            // get iterations results
            var results = new ConcurrentBag<TestCaseResult>();
            foreach (var partialResult in partialResults)
            {
                var fullResult = testManagement
                    .GetTestResultByIdAsync(project, testCase.TestRunKey.ToInt(), partialResult.Id, ResultDetails.Iterations)
                    .GetAwaiter()
                    .GetResult();
                results.Add(fullResult);
            }
            return results;
        }

        private static void SetIterationPassOrFail(RhinoTestCase testCase, TestCaseResult result, TestIterationDetailsModel iteration)
        {
            // single iteration
            if (result.IterationDetails.Count == 1)
            {
                result.State = nameof(TestRunState.Completed);
            }

            // cleanup
            var itemToRemove = result.IterationDetails.Find(i => i.Id == testCase.Iteration + 1);
            if (itemToRemove != default)
            {
                result.IterationDetails.Remove(itemToRemove);
            }

            // update
            result.IterationDetails.Add(iteration);
        }

        /// <summary>
        /// Adds an attachment into a single test results iteration under automation provider.
        /// </summary>
        /// <param name="testCase">Rhino.Api.Contracts.AutomationProvider.RhinoTestCase by which to update results.</param>
        public override void OnAddAttachement(RhinoTestCase testCase)
        {
            // exit conditions
            var outcome = CSharpExtensions.Get(testCase.Context, AzureContextEntry.Outcome, nameof(TestOutcome.Unspecified));
            var incluededOutcomes = new[]
            {
                nameof(TestOutcome.Passed),
                nameof(TestOutcome.Failed),
                nameof(TestOutcome.Inconclusive),
                nameof(TestOutcome.Warning)
            };
            if (!incluededOutcomes.Contains(outcome))
            {
                return;
            }

            // setup
            _ = int.TryParse(TestRun.Key, out int runId);
            var partialResults = testManagement
                .GetTestResultsAsync(project, runId)
                .GetAwaiter()
                .GetResult()
                .Where(i => i.TestCase.Id.Equals(testCase.Key));

            var testCaseResults = new List<TestCaseResult>();
            foreach (var partialResult in partialResults)
            {
                var result = testManagement
                    .GetTestResultByIdAsync(project, runId, partialResult.Id, ResultDetails.Iterations)
                    .GetAwaiter()
                    .GetResult();
                testCaseResults.Add(result);
            }

            // setup
            var attachments = testCase.Steps.SelectMany(i => i.GetAttachments()).ToList();

            // post
            foreach (var testCaseResult in testCaseResults)
            {
                UploadAttachments(attachments, runId, testCaseResult.Id, testCase.Iteration + 1);
            }
        }

        private void UploadAttachments(IEnumerable<TestAttachmentRequestModel> attachments, int runId, int resultId, int iteration)
        {
            // setup
            var route = $"{projectReference.Id}/_apis/test/Runs/{runId}/Results/{resultId}/Attachments" +
                $"?iterationId={iteration}" +
                "&api-version=5.0-preview.1";
            var jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            };

            // upload
            Parallel.ForEach(attachments, options, attachment =>
            {
                try
                {
                    var content = JsonConvert.SerializeObject(attachment, jsonSettings);
                    var stringContent = new StringContent(content, Encoding.UTF8, "application/json");
                    var response = testManagement.HttpClient.PostAsync(route, stringContent).GetAwaiter().GetResult();

                    if (!response.IsSuccessStatusCode)
                    {
                        logger?.Warn("Add-Attachment" +
                            $" -File {attachment.FileName}" +
                            $" -Run {runId}" +
                            $" -Result {resultId}" +
                            $" -Iteration {iteration} = ({response.StatusCode}; {response.ReasonPhrase})");
                    }
                }
                catch (Exception e) when (e != null)
                {
                    logger?.Error("Add-Attachment" +
                        $"-File {attachment.FileName}" +
                        $"-Run {runId}" +
                        $"-Result {resultId}" +
                        $"-Iteration {iteration} = {e.Message}");
                }
            });
        }
        #endregion

        #region *** Delete: Test Run  ***
        /// <summary>
        /// Deletes one of more an automation provider test run entity.
        /// </summary>
        /// <param name="testRuns">A collection of Rhino.Api.Contracts.AutomationProvider.RhinoTestRun.Key to delete by.</param>
        public override void DeleteTestRun(params string[] testRuns)
        {
            // setup
            var ids = testRuns.Any(i => Regex.IsMatch(i, "(?i)all"))
                ? testManagement.GetTestRunsAsync(project).GetAwaiter().GetResult().Select(i => i.Id)
                : testRuns.AsNumbers();

            // iterate
            Parallel.ForEach(ids, options, id =>
            {
                try
                {
                    testManagement.DeleteTestRunAsync(project, id).GetAwaiter().GetResult();
                }
                catch (Exception e) when (e != null)
                {
                    logger?.Debug($"Delete-TestRun -Run {id} = {e.Message}");
                }
            });
        }
        #endregion

        #region *** Bugs & Defects    ***
        /// <summary>
        /// Gets a list of open bugs.
        /// </summary>
        /// <param name="testCase">RhinoTestCase by which to find bugs.</param>
        /// <returns>A list of bugs (can be JSON or ID for instance).</returns>
        public override IEnumerable<string> OnGetBugs(RhinoTestCase testCase)
        {
            return bugsManager.GetBugs(testCase);
        }

        /// <summary>
        /// Asserts if the RhinoTestCase has already an open bug.
        /// </summary>
        /// <param name="testCase">RhinoTestCase by which to assert against match bugs.</param>
        /// <returns>An open bug.</returns>
        public override string OnGetOpenBug(RhinoTestCase testCase)
        {
            return bugsManager.GetOpenBug(testCase);
        }

        /// <summary>
        /// Creates a new bug under the specified automation provider.
        /// </summary>
        /// <param name="testCase">Rhino.Api.Contracts.AutomationProvider.RhinoTestCase by which to create automation provider bug.</param>
        /// <returns>The ID of the newly created entity.</returns>
        public override string OnCreateBug(RhinoTestCase testCase)
        {
            return bugsManager.OnCreateBug(testCase);
        }

        /// <summary>
        /// Updates an existing bug (partial updates are supported, i.e. you can submit and update specific fields only).
        /// </summary>
        /// <param name="testCase">Rhino.Api.Contracts.AutomationProvider.RhinoTestCase by which to update automation provider bug.</param>
        /// <returns>The updated bug.</returns>
        public override string OnUpdateBug(RhinoTestCase testCase)
        {
            return bugsManager.OnUpdateBug(testCase);
        }

        /// <summary>
        /// Close all existing bugs.
        /// </summary>
        /// <param name="testCase">Rhino.Api.Contracts.AutomationProvider.RhinoTestCase by which to close automation provider bugs.</param>
        /// <returns>A collection of updated bugs.</returns>
        public override IEnumerable<string> OnCloseBugs(RhinoTestCase testCase)
        {
            // setup
            return bugsManager.OnCloseBugs(testCase, "Closed", "Fixed and verified");
        }

        /// <summary>
        /// Close all existing bugs.
        /// </summary>
        /// <param name="testCase">Rhino.Api.Contracts.AutomationProvider.RhinoTestCase by which to close automation provider bugs.</param>
        /// <returns>The closed bug.</returns>
        public override string OnCloseBug(RhinoTestCase testCase)
        {
            // setup
            var closedBugs = bugsManager.OnCloseBugs(testCase, "Closed", "Fixed and verified");

            // get
            return JsonConvert.SerializeObject(closedBugs);
        }
        #endregion
    }
}