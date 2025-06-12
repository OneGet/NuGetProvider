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
    using Microsoft.PackageManagement.Provider.Utility;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;

    /// <summary>
    /// Creates the correct INuGetResourceCollection
    /// </summary>
    public class NuGetResourceCollectionFactory
    {
        // Simple dictionary to keep track of already-parsed base URLs. PackageManagement does this parse an excessive number of times. No reason to persist this past the session.
        private static Dictionary<string, INuGetResourceCollection> sessionResourceCollectionCache = new Dictionary<string, INuGetResourceCollection>();
        private static Dictionary<string, object> sessionResourceCollectionCacheLocks = new Dictionary<string, object>();

        private NuGetResourceCollectionFactory() { }

        /// <summary>
        /// Gets all feeds discovered from the NuGet base URL.
        /// v2 example: nuget.org/api/v2
        /// v3 example: api.nuget.org/v3/index.json
        /// </summary>
        /// <param name="baseUrl">NuGet base URL.</param>
        /// <param name="request">Current request</param>
        /// <returns>All NuGet feeds discovered from the base URL. Exceptions thrown on invalid base URL.</returns>
        public static INuGetResourceCollection GetResources(string baseUrl, NuGetRequest request)
        {
            return GetResources(baseUrl, new RequestWrapper(request));
        }

        public static INuGetResourceCollection GetResources(string baseUrl, RequestWrapper request)
        {
            object cacheLock;
            if (String.IsNullOrEmpty(baseUrl))
            {
                return NuGetResourceCollectionLocal.Make();
            }

            if (!sessionResourceCollectionCacheLocks.ContainsKey(baseUrl))
            {
                lock (sessionResourceCollectionCacheLocks)
                {
                    if (!sessionResourceCollectionCacheLocks.ContainsKey(baseUrl))
                    {
                        sessionResourceCollectionCacheLocks[baseUrl] = new object();
                    }
                }
            }

            cacheLock = sessionResourceCollectionCacheLocks[baseUrl];

            lock (cacheLock)
            {
                if (!sessionResourceCollectionCache.ContainsKey(baseUrl))
                {
                    sessionResourceCollectionCache[baseUrl] = GetResourcesImpl(baseUrl, request);
                }
            }

            return sessionResourceCollectionCache[baseUrl];
        }

        private static INuGetResourceCollection GetResourcesImpl(string baseUrl, RequestWrapper request)
        {
            INuGetResourceCollection res = null;
            HttpClient client = request.GetClientWithHeaders();
            HttpResponseMessage response = PathUtility.GetHttpResponse(client, baseUrl, (() => request.IsCanceled()),
                ((msg, num) => request.Verbose(Resources.Messages.RetryingDownload, msg, num)), (msg) => request.Verbose(msg), (msg) => request.Debug(msg));
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(Resources.Messages.NuGetEndpointDiscoveryFailed);
            }

            string content = new StreamReader(NuGetClient.DownloadDataToStream(baseUrl, request)).ReadToEnd();
            // If the response starts with the magic XML header, it's v2
            if (content.StartsWith(Constants.XmlStartContent))
            {
                res = NuGetResourceCollection2.Make(baseUrl);
            }
            else
            {
                try
                {
                    dynamic root = DynamicJsonParser.Parse(content);
                    string version = root.version;
                    if (version != null && version.StartsWith("3."))
                    {
                        // v3 feed
                        res = NuGetResourceCollection3.Make(root, baseUrl, request);
                    }
                }
                catch (Exception ex)
                {
                    Exception discoveryException = new Exception(Resources.Messages.NuGetEndpointDiscoveryFailed, ex);
                    throw discoveryException;
                }
            }

            if (res == null)
            {
                // Couldn't figure out what this is
                throw new Exception(Resources.Messages.NuGetEndpointDiscoveryFailed);
            }

            return res;
        }
    }
}
