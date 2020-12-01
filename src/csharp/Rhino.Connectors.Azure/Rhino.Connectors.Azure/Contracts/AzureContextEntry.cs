/*
 * CHANGE LOG - keep only last 5 threads
 * 
 * RESOURCES
 */
namespace Rhino.Connectors.Azure.Contracts
{
    internal static class AzureContextEntry
    {
        /// <summary>
        /// Context entry to hold Azure Project.
        /// </summary>
        public const string Project = "azrProject";

        /// <summary>
        /// Context entry to hold Azure Test Configuration.
        /// </summary>
        public const string TestConfiguration = "azrTestConfiguration";

        /// <summary>
        /// Context entry to hold Azure Test Configuration ID.
        /// </summary>
        public const string ConfigurationId = "azrTestConfigurationId";

        /// <summary>
        /// Context entry to hold Azure Test Run.
        /// </summary>
        public const string TestRun = "azrTestRun";

        /// <summary>
        /// Context entry to hold Azure Work Item entity.
        /// </summary>
        public const string WorkItem = "azrWorkItem";

        /// <summary>
        /// Context entry to hold test action (step) id. This the ID given to the action by TCM.
        /// </summary>
        public const string StepRuntime = "runtimeid";

        /// <summary>
        /// Context entry to hold test action (step).
        /// </summary>
        public const string Step = "azrStep";

        /// <summary>
        /// Context entry to hold Azure Test Iteration Details.
        /// </summary>
        public const string IterationDetails = "azrIterationDetails";

        /// <summary>
        /// Context entry to hold Azure Test Action Path. This is the path given to the action by TCM.
        /// </summary>
        public const string ActionPath = "azrActionPath";

        /// <summary>
        /// Context entry to hold Azure Shared Step entity.
        /// </summary>
        public const string SharedStep = "azrSharedStep";

        /// <summary>
        /// Context entry to hold Azure Shared Step ID.
        /// </summary>
        public const string SharedStepId = "azrSharedStepId";

        /// <summary>
        /// Context entry to hold Azure Shared Step Action Path. This is the path given to the action by TCM.
        /// </summary>
        public const string SharedStepActionPath = "azrSharedStepActionPath";

        /// <summary>
        /// Context entry to hold Azure Test Action Path of a shared step. This is the path given to the action by TCM.
        /// </summary>
        public const string SharedStepPath = "azrSharedStepPath";

        /// <summary>
        /// Context entry to hold Azure Shared Step action.
        /// </summary>
        public const string SharedStepAction = "azrSharedStepAction";

        /// <summary>
        /// Context entry to hold test action (step) id. This the ID given to the action by TCM.
        /// </summary>
        public const string SharedStepRuntime = "azrSharedStepRuntimeid";

        /// <summary>
        /// Context entry to hold Azure Shared Step Identifier.
        /// </summary>
        public const string SharedStepIdentifier = "azrSharedStepIdentifier";

        /// <summary>
        /// Context entry to hold outcome of a Test Case, Test Run, Test Step, etc.
        /// </summary>
        public const string Outcome = "azrOutcome";
    }
}
