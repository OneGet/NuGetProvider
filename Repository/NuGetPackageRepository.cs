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
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;

    public class NuGetPackageRepository : IPackageRepository
    {
        private string baseUrl; 

        public string Source
        {
            get
            {
                return this.baseUrl;
            }
        }

        public bool IsFile => false;

        public INuGetResourceCollection ResourceProvider { get; private set; }

        public NuGetPackageRepository(PackageRepositoryCreateParameters parms)
        {
            // First validate the input package source location
            if (!parms.ValidateLocation())
            {
                throw new ArgumentException(String.Format(CultureInfo.CurrentCulture, Messages.InvalidQueryUrl, parms.Location));
            }

            this.ResourceProvider = NuGetResourceCollectionFactory.GetResources(parms.Location, parms.Request);
            this.baseUrl = parms.Location;
        }

        public IPackage FindPackage(NuGetSearchContext findContext, NuGetRequest request)
        {
            request.Debug(Messages.DebugInfoCallMethod3, "NuGetPackageRepository", "FindPackage", findContext.PackageInfo.Id);
            NuGetSearchResult result = this.ResourceProvider.PackagesFeed.Find(findContext, request);
            if (result.Result != null)
            {
                return result.Result.FirstOrDefault();
            } else
            {
                return null;
            }
        }

        public NuGetSearchResult FindPackagesById(NuGetSearchContext findContext, NuGetRequest request)
        {
            request.Debug(Messages.DebugInfoCallMethod3, "NuGetPackageRepository", "FindPackagesById", findContext.PackageInfo.Id);
            return this.ResourceProvider.PackagesFeed.Find(findContext, request);
        }

        public NuGetSearchResult Search(NuGetSearchContext searchContext, NuGetRequest request)
        {
            request.Debug(Messages.DebugInfoCallMethod, "NuGetPackageRepository", "Search");
            return this.ResourceProvider.QueryFeed.Search(searchContext, request);
        }

        public bool DownloadPackage(PublicObjectView packageView, string destination, NuGetRequest request)
        {
            request.Debug(Messages.DebugInfoCallMethod, "NuGetPackageRepository", "DownloadPackage");
            return this.ResourceProvider.FilesFeed.DownloadPackage(packageView, destination, request);
        }

        public bool InstallPackage(PublicObjectView packageView, NuGetRequest request)
        {
            request.Debug(Messages.DebugInfoCallMethod, "NuGetPackageRepository", "InstallPackage");
            return this.ResourceProvider.FilesFeed.InstallPackage(packageView, request);
        }
    }
}
