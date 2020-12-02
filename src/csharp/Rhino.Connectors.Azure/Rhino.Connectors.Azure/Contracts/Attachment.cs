/*
 * CHANGE LOG - keep only last 5 threads
 * 
 * RESOURCES
 */
using System.IO;

namespace Rhino.Connectors.Azure.Contracts
{
    /// <summary>
    /// Wrap information about Azure DevOps attachment. Used as "bridge" object.
    /// </summary>
    internal class Attachment
    {
        /// <summary>
        /// Gets or sets the project of this attachment.
        /// </summary>
        public string Project { get; set; }

        /// <summary>
        /// Gets or sets the iteration id for this attachment (if the attachment is used for test case).
        /// </summary>
        public int IterationId { get; set; }

        /// <summary>
        /// Gets or sets the area path of this attachment.
        /// </summary>
        public string AreaPath { get; set; }

        /// <summary>
        /// Gets or sets the attachment full name, including path.
        /// </summary>
        public string FullName { get; set; }

        /// <summary>
        /// Gets or sets the attachment file name, including suffix.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the attachment file type.
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Gets or sets the upload <see cref="Stream"/>.
        /// </summary>
        public Stream UploadStream { get; set; }

        /// <summary>
        /// Gets or sets the step action path.
        /// </summary>
        public string ActionPath { get; set; }

        /// <summary>
        /// Gets or sets the step action ID.
        /// </summary>
        public string ActionRuntime { get; set; }
    }
}