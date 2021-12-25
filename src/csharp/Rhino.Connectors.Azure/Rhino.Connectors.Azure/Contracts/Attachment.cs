/*
 * CHANGE LOG - keep only last 5 threads
 * 
 * RESOURCES
 */
using Microsoft.TeamFoundation.TestManagement.WebApi;

using System;
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

        /// <summary>
        /// Gets a <see cref="TestAttachmentRequestModel"/> based on this Attachment.
        /// </summary>
        /// <returns>A <see cref="TestAttachmentRequestModel"/>.</returns>
        public TestAttachmentRequestModel GetAttachmentRequestModel()
        {
            return GetAttachmentRequestModel("Automatically created by Rhino Engine", UploadStream, Type, Name);
        }

        /// <summary>
        /// Gets a <see cref="TestAttachmentRequestModel"/> based on this Attachment.
        /// </summary>
        /// <param name="comment">A comment to add when uploading the attachment.</param>
        /// <returns>A <see cref="TestAttachmentRequestModel"/>.</returns>
        public TestAttachmentRequestModel GetAttachmentRequestModel(string comment)
        {
            return GetAttachmentRequestModel(comment, UploadStream, Type, Name);
        }

        private static TestAttachmentRequestModel GetAttachmentRequestModel(
            string comment,
            Stream uploadStream,
            string type,
            string name)
        {
            // setup
            byte[] bytes;
            using (var memoryStream = new MemoryStream())
            {
                uploadStream.CopyTo(memoryStream);
                bytes = memoryStream.ToArray();
            }
            string base64 = Convert.ToBase64String(bytes);

            // get
            return new TestAttachmentRequestModel
            {
                AttachmentType = type,
                Comment = comment,
                FileName = name,
                Stream = base64
            };
        }
    }
}
