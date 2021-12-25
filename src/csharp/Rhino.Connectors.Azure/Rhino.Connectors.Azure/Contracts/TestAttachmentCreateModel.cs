/*
 * CHANGE LOG - keep only last 5 threads
 * 
 * RESOURCES
 */
using Microsoft.TeamFoundation.TestManagement.WebApi;

using System;

namespace Rhino.Connectors.Azure.Contracts
{
    /// <summary>
    /// Wrap information about Azure DevOps attachment creation and uploading. Used as "bridge" object.
    /// </summary>
    internal class TestAttachmentCreateModel
    {
        public TestAttachmentRequestModel RequestModel { get; set; }
        public Guid Project { get; set; }
        public int TestRun { get; set; }
        public int TestResult { get; set; }
        public int TestIteration { get; set; }
    }
}
