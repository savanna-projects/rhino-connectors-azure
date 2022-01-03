/*
 * CHANGE LOG - keep only last 5 threads
 * 
 * RESOURCES
 */
using Gravity.Extensions;

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace Rhino.Connectors.Azure.Extensions
{
    /// <summary>
    /// Extension package for native C# objects.
    /// </summary>
    internal static class CSharpExtensions
    {
        /// <summary>
        /// Add rows into a given <see cref="DataTable"/>.
        /// </summary>
        /// <param name="dataTable"><see cref="DataTable"/> to add rows to.</param>
        /// <param name="numberOfRows">The number of rows to add.</param>
        public static void AddRows(this DataTable dataTable, int numberOfRows)
        {
            // setup
            dataTable ??= new DataTable();

            // build
            for (int i = 0; i < numberOfRows; i++)
            {
                dataTable.Rows.Add(dataTable.NewRow());
            }
        }

        /// <summary>
        /// Gets nullable <see cref="int"/> as non-nullable <see cref="int"/>.
        /// </summary>
        /// <param name="number">Nullable <see cref="int"/>.</param>
        /// <returns>Non-nullable <see cref="int"/>.</returns>
        public static int ToInt(this int? number)
        {
            // setup
            _ = int.TryParse($"{number}", out int numberOut);

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
        /// Normalize HTML encoded chars on Azure HTML fields.
        /// </summary>
        /// <param name="str"><see cref="string"/> HTML to normalize.</param>
        /// <returns>Normalized <see cref="string"/>.</returns>
        public static string DecodeHtml(this string str) => HttpUtility.HtmlDecode(str.Replace("<BR/>", "\n"));

        /// <summary>
        /// UTC - ISO 8601 (2012-03-19T07:22Z) format.
        /// </summary>
        /// <param name="dateTime"><see cref="DateTime"/> to convert.</param>
        /// <returns>ISO 8601 formatter.</returns>
        public static DateTime ToAzureDate(this DateTime dateTime, bool addMilliseconds)
        {
            // with milliseconds
            if (addMilliseconds)
            {
                return new DateTime(
                    year: dateTime.Year,
                    month: dateTime.Month,
                    day: dateTime.Day,
                    hour: dateTime.Hour,
                    minute: dateTime.Minute,
                    second: dateTime.Second,
                    millisecond: dateTime.Millisecond,
                    kind: dateTime.Kind);
            }

            // without milliseconds
            return new DateTime(
                year: dateTime.Year,
                month: dateTime.Month,
                day: dateTime.Day,
                hour: dateTime.Hour,
                minute: dateTime.Minute,
                second: dateTime.Second,
                kind: dateTime.Kind);
        }

        /// <summary>
        /// Gets a value if key exists or default value if not.
        /// </summary>
        /// <typeparam name="T">The <see cref="Type"/> of the value.</typeparam>
        /// <param name="dictionary">The <see cref="IDictionary{TKey, TValue}"/> to get value from.</param>
        /// <param name="key">The key to get value by.</param>
        /// <param name="defaultValue">The default value to return.</param>
        /// <returns>A value or default value if value was not found.</returns>
        public static T Get<T>(this IDictionary<string, object> dictionary, string key, T defaultValue)
        {
            // exit conditions
            if (!dictionary.ContainsKey(key))
            {
                return defaultValue;
            }

            // build
            try
            {
                if (dictionary[key].GetType() == typeof(T))
                {
                    return (T)dictionary[key];
                }
                if(typeof(T) == typeof(string))
                {
                    dictionary[key] = $"{dictionary[key]}";
                    return (T)dictionary[key];
                }

                var isJsonElement = dictionary[key] is JsonElement;
                var isJsonToken = dictionary[key] is Newtonsoft.Json.Linq.JToken;

                if(!isJsonElement && !isJsonToken)
                {
                    return (T)dictionary[key];
                }

                var json = $"{dictionary[key]}";
                return JsonSerializer.Deserialize<T>(json);
            }
            catch (Exception e) when (e != null)
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Sets a filed in the dictionary if the value to set is not null or empty.
        /// </summary>
        /// <typeparam name="T">The <see cref="Type"/> of the value.</typeparam>
        /// <param name="dictionary">The <see cref="IDictionary{TKey, TValue}"/> to set value into.</param>
        /// <param name="key">The key to set value by.</param>
        /// <param name="value">The default value to set.</param>
        public static void SetWhenNotNullOrEmpty<T>(this IDictionary<string, object> dictionary, string key, T value)
        {
            // exit conditions
            if (value == null || string.IsNullOrEmpty($"{value}"))
            {
                return;
            }

            // add
            dictionary[key] = value;
        }

        /// <summary>
        /// In a specified input string, replaces all strings that match a regular expression
        /// pattern with a specified replacement string.
        /// </summary>
        /// <param name="input">The string to search for a match.</param>
        /// <param name="pattern">The regular expression pattern to match.</param>
        /// <param name="replacement">The replacement string.</param>
        /// <returns>A new string that is identical to the input string, except that the replacement string takes the place of each matched string.</returns>
        public static string Replace(this string input, string pattern, string replacement)
        {
            return Regex.Replace(input, pattern, replacement);
        }
    }
}