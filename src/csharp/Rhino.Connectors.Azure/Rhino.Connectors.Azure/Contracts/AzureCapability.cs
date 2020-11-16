/*
 * CHANGE LOG - keep only last 5 threads
 * 
 * RESOURCES
 */
namespace Rhino.Connectors.Azure.Contracts
{
    public static class AzureCapability
    {
        /// <summary>
        /// The area path under which items will be created (tests, bugs, etc.). If not selected, default value will be used.
        /// </summary>
        public const string AreaPath = "areaPath";

        /// <summary>
        /// The iteration under which items will be created (tests, bugs, etc.). If not selected, default value will be used.
        /// </summary>
        public const string IterationPath = "iterationPath";

        /// <summary>
        /// Key/Value pairs of custom fields to apply when creating items (tests, bugs, etc.). The fileds names must be system fields names.
        /// </summary>
        public const string CustomFields = "customFields";

        /// <summary>
        /// The test plan ID to use. If set, tests will be created under this plan.
        /// </summary>
        public const string TestPlan = "testPlan";

        /// <summary>
        /// The test configuration ID which will be used when running the current tests. If not selected, defaults values will be used.
        /// </summary>
        public const string TestConfiguration = "testConfiguration";
    }
}
