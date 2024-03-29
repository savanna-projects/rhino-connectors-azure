﻿/*
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
using Gravity.Services.Comet.Engine.Attributes;

using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.TestManagement.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.TestManagement.TestPlanning.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch;

using Newtonsoft.Json;

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
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using GlobalSettings = Rhino.Connectors.Azure.Extensions.GlobalSettings;
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
        private readonly WorkItemTrackingHttpClient _itemManagement;
        private readonly TestManagementHttpClient _testManagement;
        private readonly TestPlanHttpClient _planManagement;
        private readonly VssConnection _connection;
        private readonly TeamProjectReference _projectReference;

        // members
        private readonly ILogger _logger;
        private readonly ParallelOptions _options;
        private readonly AzureBugsManager _bugsManager;
        private readonly string _project;

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
            var credentials = configuration.GetVssCredentials();

            _logger = logger;
            _connection = new VssConnection(new Uri(configuration.ConnectorConfiguration.Collection), credentials);
            _project = configuration.ConnectorConfiguration.Project;
            _bugsManager = new AzureBugsManager(configuration, logger);
            _options = new ParallelOptions { MaxDegreeOfParallelism = BucketSize };

            BucketSize = configuration.GetCapability(ProviderCapability.BucketSize, 15);
            Configuration.Capabilities ??= new Dictionary<string, object>();

            // allow service points
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls |
                SecurityProtocolType.Tls11 |
                SecurityProtocolType.Tls12 |
                SecurityProtocolType.Tls13;
            ServicePointManager.ServerCertificateValidationCallback += (_, _, _, _) => true;

            // cache project
            var projectManagement = _connection.GetClient<ProjectHttpClient>(GlobalSettings.ClientNumberOfAttempts);
            _projectReference = projectManagement.GetProjects(GlobalSettings.ClientNumberOfAttempts)
                .FirstOrDefault(i => i.Name.Equals(_project, StringComparison.OrdinalIgnoreCase));
            TestRun.Context[AzureContextEntry.Project] = _projectReference;

            // create clients
            _itemManagement = _connection.GetClient<WorkItemTrackingHttpClient>(GlobalSettings.ClientNumberOfAttempts);
            _testManagement = _connection.GetClient<TestManagementHttpClient>(GlobalSettings.ClientNumberOfAttempts);
            _planManagement = _connection.GetClient<TestPlanHttpClient>(GlobalSettings.ClientNumberOfAttempts);
        }
        #endregion

        // TODO: implement getting plugins from ALM (shared Steps)
        //       1. break Invoke on all sub method into extensions
        //       2. get description from work item to add as meta data
        #region *** Plugins           ***
        /// <summary>
        /// Implements to load extra collections of Rhino.Api.Contracts.AutomationProvider.RhinoPlugin.
        /// </summary>
        /// <param name="plugins">An existing collection of Rhino.Api.Contracts.AutomationProvider.RhinoPlugin.</param>
        /// <returns>A collection of Rhino.Api.Contracts.AutomationProvider.RhinoPlugin.</returns>
        /// <remarks>You can implement this method to load plugins from A.L.M or other source.</remarks>
        protected override (IEnumerable<RhinoPlugin> Rhino, IEnumerable<PluginAttribute> Gravity) OnGetPlugins()
        {
            //// setup
            //var wiql = new Wiql()
            //{
            //    Query = "SELECT * FROM WorkItems where [Work Item Type] = 'Shared Steps'"
            //};

            //// get shared steps
            //var items = itemManagement
            //    .QueryByWiqlAsync(wiql, project)
            //    .GetAwaiter()
            //    .GetResult()
            //    .WorkItems
            //    .Select(i => i.Id)
            //    .ToArray();

            //// work items and fetch titles
            //var sharedSteps = itemManagement
            //    .GetWorkItemsAsync(items, expand: WorkItemExpand.All)
            //    .GetAwaiter()
            //    .GetResult();

            // build
            //var connectorPlugins = sharedSteps[0].CreateActionAttribute();

            // get
            return (Array.Empty<RhinoPlugin>(), Array.Empty<PluginAttribute>());
        }
        #endregion

        #region *** Get: Test Cases   ***
        /// <summary>
        /// Returns a list of test cases for a project.
        /// </summary>
        /// <param name="ids">A list of test ids to get test cases by.</param>
        /// <returns>A collection of Rhino.Api.Contracts.AutomationProvider.RhinoTestCase</returns>
        protected override IEnumerable<RhinoTestCase> OnGetTestCases(params string[] ids)
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
                _logger?.Warn("Get-TestCases = MethodsNotFound");
                return Array.Empty<RhinoTestCase>();
            }

            // fetch
            foreach (var method in methods)
            {
                var range = GetTestCases(method, ids);
                testCases.AddRange(range);
            }

            // log
            var invalidTestCases = testCases
                .Where(i => i.Steps.Any(i => string.IsNullOrEmpty(i.Action)))
                .Select(i => i.Key)
                .Distinct();
            if (invalidTestCases.Any())
            {
                _logger?.Warn($"Get-TestCases -Invalid = {JsonConvert.SerializeObject(invalidTestCases)}");
            }

            // TODO: replace by .NET6.0 distinct by
            // log
            var distinctTestCases = Gravity
                .Extensions
                .CollectionExtensions
                .DistinctBy(testCases, (i) => i.Key)
                .Where(i => !invalidTestCases.Contains(i.Key))
                .ToList();
            _logger?.Debug($"Get-TestCases -Distinct = {distinctTestCases.Count}");

            // get
            return distinctTestCases.Select(i => i.AddToContext(CapabilitesKey, Configuration.Capabilities));
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
            return _itemManagement.GetRhinoTestCases(ids: itemsToFind);
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
            Parallel.ForEach(itemsToFind, _options, id =>
            {
                var range = _planManagement
                    .GetSuiteEntriesAsync(_project, id, TestEntryType)
                    .GetAwaiter()
                    .GetResult()
                    .Where(i => i.SuiteEntryType == TestEntryType)
                    .Select(i => i.Id);

                testCases.AddRange(range);
            });

            // get: rhino test cases
            var groups = testCases.Split(100);
            Parallel.ForEach(groups, _options, group =>
            {
                var items = GetTestCases(group).Select(i => i.Id.ToInt());
                var range = _itemManagement.GetRhinoTestCases(ids: items);
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
            Parallel.ForEach(itemsToFind, _options, id =>
            {
                try
                {
                    var testSuites = _planManagement.GetTestSuitesForPlanWithContinuationTokenAsync(_project, id).GetAwaiter().GetResult();
                    var suites = testSuites.Select(i => i.Id);
                    suitesByPlans.Add((Plan: id, Suites: suites));
                }
                catch (TestObjectNotFoundException)
                {
                    _logger?.Debug($"Get-ByTestPlans -Plan {id} = NotFound");
                }
            });
            _logger?.Debug($"Get-ByTestPlans -Plan = {suitesByPlans.Count}");
            _logger?.Debug($"Get-ByTestPlans -Suite = {suitesByPlans.SelectMany(i => i.Suites).Count()}");

            // get: rhino test cases
            Parallel.ForEach(suitesByPlans, _options, suiteByPlan =>
            {
                Parallel.ForEach(suiteByPlan.Suites, _options, suite =>
                {
                    var onTestCases = _testManagement.GetTestCasesAsync(_project, suiteByPlan.Plan, suite).GetAwaiter().GetResult();
                    var range = onTestCases.Select(i => i.Workitem.Id);
                    testCases.AddRange(range);
                });
            });

            // setup > log
            itemsToFind = testCases.ToNumbers();
            _logger?.Debug($"Get-ByTestPlans -TestCase = {itemsToFind.Count()}");

            // get: rhino test cases
            var groups = itemsToFind.Split(100);
            Parallel.ForEach(groups, _options, group =>
            {
                var items = GetTestCases(group).Select(i => i.Id.ToInt());
                var range = _itemManagement.GetRhinoTestCases(ids: items);
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
            Parallel.ForEach(queries, _options, query =>
            {
                try
                {
                    var isGuid = Guid.TryParse(query, out Guid queryOut);
                    var wiql = new Wiql() { Query = query };

                    var queryResults = isGuid
                        ? _itemManagement.QueryByIdAsync(_project, queryOut).GetAwaiter().GetResult()
                        : _itemManagement.QueryByWiqlAsync(wiql, _project).GetAwaiter().GetResult();

                    var range = queryResults.WorkItems.Select(i => i.Id);
                    testCases.AddRange(range);
                }
                catch (VssServiceException e)
                {
                    _logger?.Debug($"Get-ByQueries -Query {query} = {e.Message}");
                }
            });

            // setup > log
            var itemsToFind = testCases.ToList();
            _logger?.Debug($"Get-ByQueries = {itemsToFind.Count}");

            // get
            var items = GetTestCases(testCases).Select(i => i.Id.ToInt());
            return _itemManagement.GetRhinoTestCases(ids: items);
        }

        // get test cases by id
        private IEnumerable<RhinoTestCase> GetTestCases(MethodInfo method, IEnumerable<string> ids)
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
                _logger?.Warn("Get-TestCases = NotSupported");
            }
            catch (Exception e) when (e != null)
            {
                _logger?.Error($"Get-TestCases = {e.GetBaseException().Message}");
            }
            return testCases;
        }

        private IEnumerable<WorkItem> GetTestCases(IEnumerable<int> ids)
        {
            try
            {
                return _itemManagement
                    .GetWorkItemsAsync(ids, fields: null, asOf: null, expand: WorkItemExpand.All)
                    .GetAwaiter()
                    .GetResult()
                    .Where(i => $"{i.Fields[TypeField]}".Equals(TestCase, Compare))
                    .ToArray();
            }
            catch (Exception e)
            {
                _logger?.Error(e.Message, e);
                return Array.Empty<WorkItem>();
            }
        }
        #endregion

        // TODO: implement supporting for literal shared steps by creating
        //       a shared step entry if the literal step match a shared step title.
        #region *** Create: Test Case ***
        /// <summary>
        /// Creates a new test case under the specified automation provider.
        /// </summary>
        /// <param name="testCase">Rhino.Api.Contracts.AutomationProvider.RhinoTestCase by which to create automation provider test case.</param>
        /// <returns>The ID of the newly created entity.</returns>
        protected override string OnCreateTestCase(RhinoTestCase testCase)
        {
            // setup
            var document = testCase.GetTestDocument(Operation.Add, "Automatically created by Rhino engine.");

            // post
            var item = _itemManagement
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
            testPlan = testPlan <= 0 ? _planManagement.GetPlanForSuite(_project, testSuite) : 0;
            _logger?.Debug($"Get-PlanForSuite -Project {_project} -TestSuite {testSuite} = {testPlan}");

            // put
            try
            {
                _testManagement
                    .AddTestCasesToSuiteAsync(_project, testPlan, testSuite, testCase)
                    .GetAwaiter()
                    .GetResult();

                _logger?.Debug("Add-TestToSuite" +
                    $"-Project {_project} " +
                    $"-TestPlan {testPlan} " +
                    $"-TestSuite {testSuite} " +
                    $"-TestCase {testCase} = OK");
            }
            catch (Exception e)
            {
                _logger?.Error("Add-TestToSuite " +
                    $"-TestPlan {testPlan} " +
                    $"-TestSuite {testSuite} " +
                    $"-TestCase {testCase} = {e.GetBaseException().Message}");
            }
        }
        #endregion

        #region *** Update: Test Case ***
        /// <summary>
        /// Updates an existing test case (partial updates are supported, i.e. you can submit and update specific fields only).
        /// </summary>
        /// <param name="testCase">RhinoTestCase by which to update automation provider test case.</param>
        protected override void OnUpdateTestCase(RhinoTestCase testCase)
        {
            // setup
            var document = testCase.GetTestDocument(Operation.Add, "Automatically synced by Rhino engine.");
            _ = int.TryParse(testCase.Key, out int idOut);

            // post
            _itemManagement
                .UpdateWorkItemAsync(document, idOut, bypassRules: true, suppressNotifications: true, expand: WorkItemExpand.All)
                .GetAwaiter()
                .GetResult();

            // exit conditions
            if (!testCase.TestSuites.Any())
            {
                return;
            }

            // setup: add to suites
            var testPlan = Configuration.GetAzureCapability(AzureCapability.TestPlan, 0);

            // add
            foreach (var testSuite in testCase.TestSuites.AsNumbers())
            {
                AddTestToSuite(testPlan, testSuite, testCase: testCase.Key);
            }
        }
        #endregion

        #region *** Configuration     ***
        /// <summary>
        /// Implements a mechanism of setting a testing configuration for an automation provider.
        /// </summary>
        /// <remarks>Use this method for <see cref="SetConfiguration"/> customization.</remarks>
        protected override void OnSetConfiguration(RhinoConfiguration configuration)
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
                var testConfiguration = _planManagement
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
                testConfiguration = _planManagement
                    .CreateTestConfigurationAsync(parameters, Configuration.ConnectorConfiguration.Project)
                    .GetAwaiter()
                    .GetResult();

                _logger?.Debug($"Set-Configuration -Name {ConfigurationName} -Create = {testConfiguration.Id}");
                TestRun.Context[AzureContextEntry.TestConfiguration] = testConfiguration;

                id = testConfiguration.Id;
            }
            catch (Exception e) when (e.GetBaseException() is VssResourceNotFoundException)
            {
                _logger?.Warn($"Set-Configuration -Name {ConfigurationName} = NotSupported");
            }
            catch (Exception e) when (e != null)
            {
                _logger?.Error($"Set-Configuration -Name {ConfigurationName} = {e.GetBaseException().Message}");
            }

            // put to context
            var _configuration = GetConfiguration(id);
            AddConfigurationToTestContext(_configuration);
        }

        private Microsoft.VisualStudio.Services.TestManagement.TestPlanning.WebApi.TestConfiguration GetConfiguration(int id)
        {
            try
            {
                return _planManagement.GetTestConfigurationByIdAsync(_project, id).GetAwaiter().GetResult();
            }
            catch (Exception e) when (e != null)
            {
                _logger?.Warn($"Get-Configuration -Server {_connection.Uri} -Project {_project} = NotSupported");
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
                _logger?.Debug($"Get-Configuration -FromCapabilites {id} = OK");
            }
            else
            {
                _logger?.Debug($"Set-Configuration -Default {id} = OK");
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
        // TODO: add retry on connection failure
        /// <summary>
        /// Creates an automation provider test run entity. Use this method to implement the automation
        /// provider test run creation and to modify the loaded Rhino.Api.Contracts.AutomationProvider.RhinoTestRun.
        /// </summary>
        /// <param name="testRun">Rhino.Api.Contracts.AutomationProvider.RhinoTestRun object to modify before creating.</param>
        /// <returns>Rhino.Api.Contracts.AutomationProvider.RhinoTestRun based on provided test cases.</returns>
        protected override RhinoTestRun OnCreateTestRun(RhinoTestRun testRun)
        {
            // create
            try
            {
                // 1. Create Run
                var azureTestRun = InvokeCreateTestRun(testRun);

                // 2. Get Created Run
                var testCaseResults = azureTestRun.GetTestRunResults(_testManagement);

                // 3. Add Iterations                
                foreach (var testCaseResult in testCaseResults)
                {
                    var testCases = testRun.TestCases.Where(i => i.Key == testCaseResult.TestCase.Id);
                    var iterationDetails = testCases.Select(i => i.ToTestIterationDetails(false));
                    testCaseResult.IterationDetails.AddRange(iterationDetails);
                }

                _testManagement
                    .UpdateTestResultsAsync(testCaseResults.ToArray(), _project, azureTestRun.Id)
                    .GetAwaiter()
                    .GetResult();

                // 5. log
                _logger?.Debug($"Create-TestRun = {azureTestRun.Url}");
            }
            catch (Exception e) when (e != null)
            {
                Configuration.ConnectorConfiguration.DryRun = true;
                _logger?.Error($"Create-TestRun = {e.GetBaseException().Message}");
            }

            // get
            return testRun;
        }

        // TODO: investigate when TestRun property is not updated here
        private TestRun InvokeCreateTestRun(RhinoTestRun testRun)
        {
            // setup
            var runCreateModel = GetCreateModel(testRun);

            // create
            var onTestRun = _testManagement
                .CreateTestRunAsync(runCreateModel, Configuration.ConnectorConfiguration.Project)
                .GetAwaiter()
                .GetResult();

            // add results
            testRun.Key = $"{onTestRun.Id}";
            testRun.Context[AzureContextEntry.TestRun] = onTestRun;

            // get
            return onTestRun;
        }

        private RunCreateModel GetCreateModel(RhinoTestRun testRun)
        {
            // setup
            var plan = GetFromOptions(AzureCapability.TestPlan);
            _ = int.TryParse(testRun.TestCases.FirstOrDefault()?.Key, out int testCaseId);

            try
            {
                // setup
                plan = plan == -1 ? _planManagement.GetPlanForTest(_project, testCaseId) : plan;
                Configuration.AddAzureCapability(AzureCapability.TestPlan, plan);
                _logger?.Debug($"Get-PlanForTest -Project {_project} -TestCase {testCaseId} = {plan}");

                //  test points
                var points = GetAllTestPoints(testRun).Select(i => i.Id).ToArray();

                // get
                return new RunCreateModel(name: testRun.Title, pointIds: points, plan: new ShallowReference(id: $"{plan}"));
            }
            catch (Exception e) when (e != null)
            {
                var message = $"Get-CreateModel -Plan {plan} = {e.GetBaseException().Message}";
                _logger.Fatal(message, e);
                throw new InvalidOperationException(message, innerException: e);
            }
        }

        private IEnumerable<TestPoint> GetAllTestPoints(RhinoTestRun testRun)
        {
            // setup
            var testCasesGroups = testRun.TestCases.Split(100);
            var testPoints = new ConcurrentBag<TestPoint>();

            // build
            Parallel.ForEach(testCasesGroups, _options, testCases =>
            {
                var ids = testCases.Select(i => i.Key).Distinct().AsNumbers().ToList();
                var filter = new PointsFilter { TestcaseIds = ids };
                var query = new TestPointsQuery() { PointsFilter = filter };

                var range = _testManagement.GetPointsByQueryAsync(query, _project).GetAwaiter().GetResult().Points;
                range = ValidatePoints(range).ToList();

                testPoints.AddRange(range);
            });

            // get
            _logger?.Debug($"Get-AllTestPoints = {testPoints.Count}");
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
            Parallel.ForEach(testPoints, _options, testPoint =>
            {
                var onTestSuite = Regex.Match(testPoint.Url, pattern: @"(?<=Suites/)\d+").Value;
                _ = int.TryParse(onTestSuite, out int testSuite);

                try
                {
                    var point = _testManagement
                         .GetPointAsync(_project, testPlan, testSuite, testPoint.Id)
                         .GetAwaiter()
                         .GetResult();
                    if (!isTestSuiteProvided || testSuiteOption == testSuite)
                    {
                        testPointsResults.Add(point);
                    }
                }
                catch (Exception e) when (e != null)
                {
                    _logger?.Debug($"Confirm-TestPoint -Id {testPoint.Id} = {e.Message}");
                }
            });

            // get
            return testPointsResults;
        }

        private int GetFromOptions(string optionsEntry)
        {
            // setup
            const string OptionsKey = RhinoConnectors.AzureTestManager + ":options";
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
        protected override void OnRunTeardown(RhinoTestRun testRun)
        {
            // setup
            _ = int.TryParse(testRun.Key, out int runIdOut);
            var model = new RunUpdateModel(state: nameof(TestRunState.Completed));

            // get test results
            var azureTestRun = _testManagement.GetTestRunByIdAsync(_project, testRun.Key.ToInt()).GetAwaiter().GetResult();
            var runResults = azureTestRun.GetTestRunResults(_testManagement).Where(i => i.State != nameof(TestRunState.Completed)).ToList();

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
                _testManagement.UpdateTestRunAsync(model, _project, azureTestRun.Id).GetAwaiter().GetResult();
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
            _testManagement.UpdateTestResultsAsync(results.ToArray(), _project, runIdOut).GetAwaiter().GetResult();

            // setup
            azureTestRun = _testManagement.GetTestRunByIdAsync(_project, testRun.Key.ToInt()).GetAwaiter().GetResult();
            isRunStateOk = azureTestRun.State == nameof(TestRunState.Completed);

            // put
            if (!isRunStateOk)
            {
                _testManagement.UpdateTestRunAsync(model, _project, azureTestRun.Id).GetAwaiter().GetResult();
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
        protected override void OnUpdateTestResult(RhinoTestCase testCase)
        {
            // setup
            var outcome = CSharpExtensions.Get(testCase.Context, AzureContextEntry.Outcome, nameof(TestOutcome.Unspecified));
            var results = GetTestCaseResult(testCase);

            // exit conditions
            if (!results.Any())
            {
                _logger?.Debug($"Update-TestResults -Test {testCase.Key} = NotFound");
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
            _testManagement
                .UpdateTestResultsAsync(onResults.ToArray(), _project, int.Parse(testCase.TestRunKey))
                .GetAwaiter()
                .GetResult();
            _logger?.Debug($"Update-TestResults -Test {testCase.Key} = (Ok, {onResults.Count})");
        }

        private IEnumerable<TestCaseResult> GetTestCaseResult(RhinoTestCase testCase)
        {
            // setup
            _ = int.TryParse(testCase.TestRunKey, out int runIdOut);

            // get test results
            var partialResults = _testManagement.GetTestResultsAsync(_project, runIdOut)
                .GetAwaiter()
                .GetResult()
                .Where(i => i.TestCase.Id.Equals(testCase.Key));

            // exit conditions
            if (partialResults?.Any() == false)
            {
                _logger?.Debug($"Get-TestResults -TestCase {testCase.Key} = NotFound");
                return default;
            }

            // context
            testCase.Context[AzureContextEntry.TestResultId] = partialResults;
            partialResults ??= Array.Empty<TestCaseResult>();

            // get iterations results
            var results = new ConcurrentBag<TestCaseResult>();
            foreach (var partialResult in partialResults)
            {
                var fullResult = _testManagement
                    .GetTestResultByIdAsync(_project, testCase.TestRunKey.ToInt(), partialResult.Id, ResultDetails.Iterations)
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
        /// <param name="testCase">RhinoTestCase by which to update results.</param>
        protected override void OnAddAttachment(RhinoTestCase testCase)
        {
            // exit conditions
            var outcome = CSharpExtensions.Get(testCase.Context, AzureContextEntry.Outcome, nameof(TestOutcome.Unspecified));
            var includedOutcomes = new[]
            {
                nameof(TestOutcome.Passed),
                nameof(TestOutcome.Failed),
                nameof(TestOutcome.Inconclusive),
                nameof(TestOutcome.Warning)
            };
            if (!includedOutcomes.Contains(outcome))
            {
                return;
            }

            // setup
            var runId = -1;
            var isTestRun = int.TryParse(TestRun.Key, out runId);
            var isTestCase = int.TryParse(testCase.TestRunKey, out runId);
            if (!isTestCase && !isTestRun)
            {
                return;
            }

            var partialResults = _testManagement
                .GetTestResultsAsync(_project, runId)
                .GetAwaiter()
                .GetResult()
                .Where(i => i.TestCase.Id.Equals(testCase.Key));

            var testCaseResults = new List<TestCaseResult>();
            foreach (var partialResult in partialResults)
            {
                var result = _testManagement
                    .GetTestResultByIdAsync(_project, runId, partialResult.Id, ResultDetails.Iterations)
                    .GetAwaiter()
                    .GetResult();
                testCaseResults.Add(result);
            }

            // setup
            var attachments = testCase.Steps.SelectMany(i => i.GetAttachments()).ToList();
            var createModels = testCaseResults.SelectMany(test => attachments.Select(attachment => new TestAttachmentCreateModel
            {
                Project = _projectReference.Id,
                RequestModel = attachment,
                TestIteration = testCase.Iteration + 1,
                TestResult = test.Id,
                TestRun = runId,
            }));

            // post
            Parallel.ForEach(createModels, _options, createModel
                => _testManagement.CreateAttachment(createModel, GlobalSettings.ClientNumberOfAttempts));
        }
        #endregion

        #region *** Delete: Test Run  ***
        /// <summary>
        /// Deletes one of more an automation provider test run entity.
        /// </summary>
        /// <param name="testRuns">A collection of Rhino.Api.Contracts.AutomationProvider.RhinoTestRun.Key to delete by.</param>
        protected override void OnDeleteTestRun(params string[] testRuns)
        {
            // setup
            var ids = testRuns.Any(i => Regex.IsMatch(i, "(?i)all"))
                ? _testManagement.GetTestRunsAsync(_project).GetAwaiter().GetResult().Select(i => i.Id)
                : testRuns.AsNumbers();

            // iterate
            Parallel.ForEach(ids, _options, id =>
            {
                try
                {
                    _testManagement.DeleteTestRunAsync(_project, id).GetAwaiter().GetResult();
                }
                catch (Exception e) when (e != null)
                {
                    _logger?.Debug($"Delete-TestRun -Run {id} = {e.Message}");
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
        protected override IEnumerable<string> OnGetBugs(RhinoTestCase testCase)
        {
            return _bugsManager.GetBugs(testCase);
        }

        /// <summary>
        /// Asserts if the RhinoTestCase has already an open bug.
        /// </summary>
        /// <param name="testCase">RhinoTestCase by which to assert against match bugs.</param>
        /// <returns>An open bug.</returns>
        protected override string OnGetOpenBug(RhinoTestCase testCase)
        {
            return _bugsManager.GetOpenBug(testCase);
        }

        /// <summary>
        /// Creates a new bug under the specified automation provider.
        /// </summary>
        /// <param name="testCase">Rhino.Api.Contracts.AutomationProvider.RhinoTestCase by which to create automation provider bug.</param>
        /// <returns>The ID of the newly created entity.</returns>
        protected override string OnCreateBug(RhinoTestCase testCase)
        {
            return _bugsManager.OnCreateBug(testCase);
        }

        /// <summary>
        /// Updates an existing bug (partial updates are supported, i.e. you can submit and update specific fields only).
        /// </summary>
        /// <param name="testCase">Rhino.Api.Contracts.AutomationProvider.RhinoTestCase by which to update automation provider bug.</param>
        /// <returns>The updated bug.</returns>
        protected override string OnUpdateBug(RhinoTestCase testCase)
        {
            return _bugsManager.OnUpdateBug(testCase);
        }

        /// <summary>
        /// Close all existing bugs.
        /// </summary>
        /// <param name="testCase">Rhino.Api.Contracts.AutomationProvider.RhinoTestCase by which to close automation provider bugs.</param>
        /// <returns>A collection of updated bugs.</returns>
        protected override IEnumerable<string> OnCloseBugs(RhinoTestCase testCase)
        {
            return _bugsManager.OnCloseBugs(testCase);
        }

        /// <summary>
        /// Close all existing bugs.
        /// </summary>
        /// <param name="testCase">Rhino.Api.Contracts.AutomationProvider.RhinoTestCase by which to close automation provider bugs.</param>
        /// <returns>The closed bug.</returns>
        protected override string OnCloseBug(RhinoTestCase testCase)
        {
            // setup
            var closedBugs = _bugsManager.OnCloseBugs(testCase);

            // get
            return JsonConvert.SerializeObject(closedBugs);
        }
        #endregion
    }
}
