/*
 * CHANGE LOG - keep only last 5 threads
 * 
 * RESOURCES
 */
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

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
        public static int ToInt(this int? number)
        {
            // setup
            int.TryParse($"{number}", out int numberOut);

            // get
            return numberOut;
        }

        /// <summary>
        /// Gets a <see cref="DataTable"/> based on a collection of <see cref="IDictionary{TKey, TValue}"/>.
        /// </summary>
        /// <param name="data">A collection of <see cref="IDictionary{TKey, TValue}"/> to build by.</param>
        /// <returns>A populated <see cref="DataTable"/> object.</returns>
        public static DataTable ToDataTable(this IEnumerable<IDictionary<string, object>> data)
        {
            // setup
            var dataTable = new DataTable();

            // exit conditions
            if (!data.Any())
            {
                return dataTable;
            }

            // setup: headers
            var headers = data.First().Select(r => new DataColumn(r.Key)).ToArray();
            dataTable.Columns.AddRange(headers);

            // setup: rows
            foreach (var item in data)
            {
                dataTable.Rows.Add(item.Select(c => c.Value).ToArray());
            }
            return dataTable;
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

        /// <summary>
        /// UTC - ISO 8601 (2012-03-19T07:22Z) format.
        /// </summary>
        /// <param name="dateTime"><see cref="DateTime"/> to convert.</param>
        /// <returns>ISO 8601 formattef.</returns>
        public static DateTime AzureNow(this DateTime dateTime, bool addMilliseconds)
        {
            return addMilliseconds
                ? new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, dateTime.Minute, dateTime.Second, dateTime.Millisecond, dateTime.Kind)
                : new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, dateTime.Minute, dateTime.Second, dateTime.Kind);
        }
    }
}