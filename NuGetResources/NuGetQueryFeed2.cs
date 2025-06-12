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
    using Microsoft.PackageManagement.NuGetProvider.Resources;
    using System;

    /// <summary>
    /// Implements NuGet v2 query feed.
    /// </summary>
    public class NuGetQueryFeed2 : INuGetQueryFeed
    {
        private string baseUrl;

        public INuGetResourceCollection ResourcesCollection { get; set; }

        public NuGetQueryFeed2(string baseUrl)
        {
            this.baseUrl = baseUrl;
            if (!this.baseUrl.EndsWith("/", StringComparison.OrdinalIgnoreCase))
            {
                this.baseUrl = String.Concat(this.baseUrl, "/");
            }
        }

        public bool IsAvailable(RequestWrapper request) => true;

        public NuGetSearchResult Search(NuGetSearchContext searchContext, NuGetRequest nugetRequest)
        {
            nugetRequest.Debug(Messages.DebugInfoCallMethod, "NuGetSearchFeed2", "Search");
            if (nugetRequest == null)
            {
                return searchContext.MakeResult();
            }
            
            return Search(searchContext, new RequestWrapper(nugetRequest));
        }

        public NuGetSearchResult Search(NuGetSearchContext searchContext, RequestWrapper request)
        {
            request.Debug(Messages.DebugInfoCallMethod, "NuGetSearchFeed2", "Search");
            if (request == null)
            {
                return searchContext.MakeResult();
            }

            string searchString = this.ResourcesCollection.GetSearchQueryDelegate(searchContext.SearchTerms);

            request.Debug(Messages.DebugInfoCallMethod3, "NuGetSearchFeed2", "Search", searchString);

            var searchQuery = searchString.MakeSearchQuery(this.baseUrl, searchContext.AllowPrerelease, searchContext.AllVersions);
            return searchContext.MakeResult(NuGetWebUtility.SendRequest(searchQuery, request));
        }
    }
}
