/*
 * CHANGE LOG - keep only last 5 threads
 * 
 * RESOURCES
 */
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Rhino.Connectors.Azure.Extensions
{
    /// <summary>
    /// Extension package for Rhino objects.
    /// </summary>
    internal static class CollectionExtensions
    {
        /// <summary>
        /// Returns all valid int numbers in the collection.
        /// </summary>
        /// <param name="strings">A collection of <see cref="string"/> to parse by.</param>
        /// <returns>A collection of numbers parsed from the input.</returns>
        public static IEnumerable<int> ToNumbers(this IEnumerable<string> strings)
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
        public static IEnumerable<string> SkipNumbers(this IEnumerable<string> strings)
        {
            return strings.Where(i => !Regex.IsMatch(input: i, pattern: @"^\d+(((,\d+)?)+((\.\d+)?)+)+$"));
        }

        /// <summary>
        /// Enqueue a collection of items.
        /// </summary>
        /// <typeparam name="T">The items type.</typeparam>
        /// <param name="queue">The <see cref="ConcurrentQueue{T}"/> to add items to.</param>
        /// <param name="range">A collection of items to add.</param>
        public static void EnqueueRange<T>(this ConcurrentQueue<T> queue, IEnumerable<T> range)
        {
            try
            {
                foreach (var item in range)
                {
                    queue.Enqueue(item);
                }
            }
            catch (Exception e) when (e != null)
            {
                // ignore exceptions
            }
        }

        /// <summary>
        /// Push a collection of items.
        /// </summary>
        /// <typeparam name="T">The items type.</typeparam>
        /// <param name="stack">The <see cref="ConcurrentStack{T}"/> to add items to.</param>
        /// <param name="range">A collection of items to add.</param>
        public static void PushRange<T>(this ConcurrentStack<T> stack, IEnumerable<T> range)
        {
            try
            {
                foreach (var item in range)
                {
                    stack.Push(item);
                }
            }
            catch (Exception e) when (e != null)
            {
                // ignore exceptions
            }
        }
    }
}