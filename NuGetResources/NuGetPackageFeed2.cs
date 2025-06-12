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
    using Microsoft.PackageManagement.Provider.Utility;
    using System;
    using System.Linq;
    using System.Net.Http;

    /// <summary>
    /// Implements NuGet v2 package feed.
    /// </summary>
    public class NuGetPackageFeed2 : INuGetPackageFeed
    {
        private string baseUrl;
        private string nugetFindPackageIdQueryFormat;

        public INuGetResourceCollection ResourcesCollection { get; set; }

        public NuGetPackageFeed2(string baseUrl)
        {
            this.baseUrl = baseUrl;
            // if a query is http://www.nuget.org/api/v2 then we add / to the end
            if (!this.baseUrl.EndsWith("/", StringComparison.OrdinalIgnoreCase))
            {
                this.baseUrl = String.Concat(this.baseUrl, "/");
            }

            //we are constructing the url query like http://www.nuget.org/api/v2/FindPackagesById()?id='JQuery'           
            this.nugetFindPackageIdQueryFormat = PathUtility.UriCombine(this.baseUrl, NuGetConstant.FindPackagesById);
        }

        public NuGetSearchResult Find(NuGetSearchContext findContext, NuGetRequest request)
        {
            return Find(findContext, new RequestWrapper(request));
        }

        public bool IsAvailable(RequestWrapper request)
        {
            bool valid = false;
            HttpClient client = request.GetClient();
            string queryUri = Constants.DummyPackageId.MakeFindPackageByIdQuery(PathUtility.UriCombine(this.baseUrl, NuGetConstant.FindPackagesById));

            HttpResponseMessage response = PathUtility.GetHttpResponse(client, queryUri, (() => request.IsCanceled()),
                ((msg, num) => request.Verbose(Resources.Messages.RetryingDownload, msg, num)), (msg) => request.Verbose(msg), (msg) => request.Debug(msg));
            
            // The link is not valid
            if (response != null && response.IsSuccessStatusCode)
            {
                valid = true;
            }

            return valid;
        }

        public NuGetSearchResult Find(NuGetSearchContext findContext, RequestWrapper request)
        {
            if (string.IsNullOrWhiteSpace(findContext.PackageInfo.Id))
            {
                return null;
            }

            request.Debug(Messages.DebugInfoCallMethod3, "NuGetPackageFeed2", "FindPackage", findContext.PackageInfo.Id);

            var query = findContext.PackageInfo.Id.MakeFindPackageByIdQuery(this.nugetFindPackageIdQueryFormat);

            var packages = NuGetClient.FindPackage(query, request).Where(package => findContext.PackageInfo.Id.Equals(package.Id, StringComparison.OrdinalIgnoreCase));

            if (findContext.RequiredVersion != null)
            {
                //Usually versions has a limited number, ToArray should be ok. 
                var versions = findContext.RequiredVersion.GetComparableVersionStrings().ToArray();
                packages = packages.Where(package => versions.Contains(package.Version, StringComparer.OrdinalIgnoreCase));
            }

            //Will only enumerate packages once
            return findContext.MakeResult(packages);
        }
    }
}
