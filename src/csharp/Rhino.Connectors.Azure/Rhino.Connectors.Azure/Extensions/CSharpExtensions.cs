/*
 * CHANGE LOG - keep only last 5 threads
 * 
 * RESOURCES
 */
namespace Rhino.Connectors.Azure.Extensions
{
    /// <summary>
    /// Extension package for native C# objects.
    /// </summary>
    internal static class CSharpExtensions
    {
        /// <summary>
        /// Gets nullable <see cref="int"/> as non-nullable <see cref="int"/>.
        /// </summary>
        /// <param name="number">Nullable <see cref="int"/>.</param>
        /// <returns>Non-nullable <see cref="int"/>.</returns>
        public static int AsInt(this int? number)
        {
            // setup
            int.TryParse($"{number}", out int numberOut);

            // get
            return numberOut;
        }

        /// <summary>
        /// Normalize HTML encoded chars on Azure HTML fileds.
        /// </summary>
        /// <param name="str"><see cref="string"/> HTML to normalize.</param>
        /// <returns>Normalized <see cref="string"/>.</returns>
        public static string DecodeHtml(this string str) => str
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&amp;", "")
            .Replace("nbsp;", "")
            .Replace("<BR/>", "\n");
    }
}