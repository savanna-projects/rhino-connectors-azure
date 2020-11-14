/*
 * CHANGE LOG - keep only last 5 threads
 * 
 * RESOURCES
 */

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Rhino.Connectors.Azure.Extensions
{
    /// <summary>
    /// Extension package for Rhino objects.
    /// </summary>
    internal class CollectionExtensions
    {
        /// <summary>
        /// Returns all valid int numbers in the collection.
        /// </summary>
        /// <param name="strings">A collection of <see cref="string"/> to parse by.</param>
        /// <returns>A collection of numbers parsed from the input.</returns>
        public IEnumerable<int> AsNumbers(IEnumerable<string> strings)
        {
            foreach (var str in strings)
            {
                var isParsed = int.TryParse(str, out int numberOut);
                if (!isParsed)
                {
                    continue;
                }
                yield return numberOut;
            }
        }

        /// <summary>
        /// Returns all strings which are not composed of numbers only.
        /// </summary>
        /// <param name="strings">A collection of <see cref="string"/> to parse by.</param>
        /// <returns>A collection of non-numbers strings extracted from the input.</returns>
        public IEnumerable<string> SkipNumbers(IEnumerable<string> strings)
        {
            return strings.Where(i => !Regex.IsMatch(input: i, pattern: @"^\d+(((,\d+)?)+((\.\d+)?)+)+$"));
        }
    }
}