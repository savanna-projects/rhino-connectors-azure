﻿/*
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
        /// Assigned to states that represent work that has finished.
        /// </summary>
        public const string CloseState = "closeState";

        /// <summary>
        /// Key/Value pairs of custom fields to apply when creating items (tests, bugs, etc.). The fields names must be system fields names.
        /// </summary>
        public const string CustomFields = "customFields";

        /// <summary>
        /// The iteration under which items will be created (tests, bugs, etc.). If not selected, default value will be used.
        /// </summary>
        public const string IterationPath = "iterationPath";

        /// <summary>
        /// The test configuration ID which will be used when running the current tests. If not selected, defaults values will be used.
        /// </summary>
        public const string TestConfiguration = "testConfiguration";

        /// <summary>
        /// The test plan ID to use. If set, tests will be created under this plan.
        /// </summary>
        public const string TestPlan = "testPlan";

        /// <summary>
        /// Use to hold Test Suite ID within the capabilities context.
        /// </summary>
        public const string TestSuite = "testSuite";
    }
}
