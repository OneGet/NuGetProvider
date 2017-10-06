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
    using System.Collections.Generic;

    /// <summary>
    /// Implements the package finding and downloading for local repositories.
    /// </summary>
    public class NuGetLocalPackageFeed : INuGetPackageFeed, INuGetFilesFeed
    {
        public INuGetResourceCollection ResourcesCollection { get; set; }

        public NuGetSearchResult Find(NuGetSearchContext findContext, NuGetRequest request) => throw new NotImplementedException();

        public bool DownloadPackage(PublicObjectView packageView, string destination, NuGetRequest request)
        {
            return DownloadPackage(packageView, destination, new RequestWrapper(request), request.AllowPrereleaseVersions.Value);
        }

        public bool InstallPackage(PublicObjectView packageView, NuGetRequest request)
        {
            return InstallPackage(packageView, new RequestWrapper(request), request.AllowPrereleaseVersions.Value);
        }

        public bool IsAvailable(RequestWrapper request) => true;

        /// <summary>
        /// No download URI.
        /// </summary>
        public string MakeDownloadUri(PackageBase package)
        {
            throw new NotImplementedException();
        }

        public PackageEntryInfo GetVersionInfo(PackageEntryInfo packageInfo, RequestWrapper request)
        {
            throw new NotImplementedException();
        }

        public NuGetSearchResult Find(NuGetSearchContext findContext, RequestWrapper request, bool allowPrerelease)
        {
            throw new NotImplementedException();
        }

        public bool DownloadPackage(PublicObjectView packageView, string destination, RequestWrapper request, bool allowPrerelease)
        {
            try
            {
                request.Debug(Messages.DebugInfoCallMethod3, "NuGetLocalPackageFeed", "DownloadPackage", destination);
                PackageItem package = packageView.GetValue<PackageItem>();
                // TODO: For now this has to require NuGetRequest, due to its usage of stuff like request.GetOptionValue and request.YieldPackage
                return NuGetClient.InstallOrDownloadPackageHelper(package, request.Request, Constants.Install,
                        (packageItem, progressTracker) => NuGetClient.DownloadSinglePackage(packageItem, request.Request, destination, progressTracker));
            }
            finally
            {
                request.Debug(Messages.DebugInfoReturnCall, "NuGetLocalPackageFeed", "DownloadPackage");
            }
        }

        public bool InstallPackage(PublicObjectView packageView, RequestWrapper request, bool allowPrerelease)
        {
            try
            {
                request.Debug(Messages.DebugInfoCallMethod, "NuGetLocalPackageFeed", "InstallPackage");
                PackageItem package = packageView.GetValue<PackageItem>();
                request.Debug(Messages.DebugInfoCallMethod3, "NuGetLocalPackageFeed", "InstallPackage", package.FastPath);
                // TODO: For now this has to require NuGetRequest, due to its usage of stuff like request.GetOptionValue and request.YieldPackage
                return NuGetClient.InstallOrDownloadPackageHelper(package, request.Request, Constants.Install,
                        (packageItem, progressTracker) => NuGetClient.InstallSinglePackage(packageItem, request.Request, progressTracker));
            }
            finally
            {
                request.Debug(Messages.DebugInfoReturnCall, "NuGetLocalPackageFeed", "InstallPackage");
            }
        }
    }
}
