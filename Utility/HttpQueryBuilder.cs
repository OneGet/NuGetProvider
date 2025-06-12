//
//  Copyright (c) Microsoft Corporation. All rights reserved.
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//  http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//

namespace Microsoft.PackageManagement.NuGetProvider
{
    using System;
    using System.Globalization;
    using System.Text;

    /// <summary>
    /// Builds URL query parameters.
    /// </summary>
    public class HttpQueryBuilder
    {
        private StringBuilder sb = new StringBuilder();
        private bool appendAmpersand = false;
        private HttpQueryBuilder innerQueryBuilder = null;

        /// <summary>
        /// Adds a given name-value pair to this query string.
        /// </summary>
        public HttpQueryBuilder Add(string name, string val, string separator = "=", bool encode = true)
        {
            sb.AppendFormat("{0}{1}{2}{3}", appendAmpersand ? "&" : String.Empty, encode ? System.Net.WebUtility.UrlEncode(name) : name, 
                separator, encode ? System.Net.WebUtility.UrlEncode(val) : val);
            appendAmpersand = true;
            return this;
        }

        /// <summary>
        /// Adds the name-value pair to a new instance of HttpQueryBuilder, while keeping this instance's name-value pairs in the new instance.
        /// </summary>
        public HttpQueryBuilder CloneAdd(string name, string val, string separator = "=")
        {
            HttpQueryBuilder clone = new HttpQueryBuilder();
            clone.innerQueryBuilder = this;
            clone.Add(name, val, separator);
            return clone;
        }

        /// <summary>
        /// Creates the query string (without ?).
        /// </summary>
        /// <returns>Query string</returns>
        public string ToQueryString()
        {
            if (this.innerQueryBuilder != null)
            {
                string query = this.innerQueryBuilder.ToQueryString();
                if (!String.IsNullOrEmpty(query))
                {
                    query += "&";
                }

                return query + sb.ToString();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Append this query string to the given url.
        /// </summary>
        /// <param name="baseUrl">Base URL without any query string.</param>
        /// <returns>Base URL + this query string.</returns>
        public string AddQueryString(string baseUrl)
        {
            return String.Format(CultureInfo.InvariantCulture, "{0}?{1}", baseUrl, this.ToQueryString());
        }
    }
}
