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
    using System.Collections.Generic;
    using Microsoft.PackageManagement.NuGetProvider.Resources;
    using Microsoft.PackageManagement.Provider.Utility;

    /// <summary>
    /// Implements the install/download functions for NuGet v2.
    /// </summary>
    public class NuGetFilesFeed2 : INuGetFilesFeed
    {
        public INuGetResourceCollection ResourcesCollection { get; set; }

        public bool InstallPackage(PublicObjectView packageView, NuGetRequest request)
        {
            return InstallPackage(packageView, new RequestWrapper(request), request.AllowPrereleaseVersions.Value);
        }

        public bool DownloadPackage(PublicObjectView packageView, string destination, NuGetRequest request)
        {
            return DownloadPackage(packageView, destination, new RequestWrapper(request), request.AllowPrereleaseVersions.Value);
        }

        public bool IsAvailable(RequestWrapper request) => true;

        public string MakeDownloadUri(PackageBase package)
        {
            // v2 doesn't really build this dynamically, so assume ContentSrcUrl is already set. This shouldn't be called for v2 as it doesn't make sense.
            return package.ContentSrcUrl;
        }

        public PackageEntryInfo GetVersionInfo(PackageEntryInfo packageInfo, RequestWrapper request)
        {
            throw new System.NotImplementedException();
        }

        public bool DownloadPackage(PublicObjectView packageView, string destination, RequestWrapper request, bool allowPrerelease)
        {
            try
            {
                request.Debug(Messages.DebugInfoCallMethod3, "NuGetFilesFeed2", "DownloadPackage", destination);
                PackageItem package = packageView.GetValue<PackageItem>();
                // TODO: For now this has to require NuGetRequest, due to its usage of stuff like request.GetOptionValue and request.YieldPackage
                return NuGetClient.InstallOrDownloadPackageHelper(package, request.Request, Constants.Install,
                        (packageItem, progressTracker) => NuGetClient.DownloadSinglePackage(packageItem, request.Request, destination, progressTracker));
            }
            finally
            {
                request.Debug(Messages.DebugInfoReturnCall, "NuGetFilesFeed2", "InstallPackage");
            }
        }

        public bool InstallPackage(PublicObjectView packageView, RequestWrapper request, bool allowPrerelease)
        {
            try
            {
                request.Debug(Messages.DebugInfoCallMethod, "NuGetFilesFeed2", "InstallPackage");
                PackageItem package = packageView.GetValue<PackageItem>();
                request.Debug(Messages.DebugInfoCallMethod3, "NuGetFilesFeed2", "InstallPackage", package.FastPath);
                // TODO: For now this has to require NuGetRequest, due to its usage of stuff like request.GetOptionValue and request.YieldPackage
                return NuGetClient.InstallOrDownloadPackageHelper(package, request.Request, Constants.Install,
                        (packageItem, progressTracker) => NuGetClient.InstallSinglePackage(packageItem, request.Request, progressTracker));
            }
            finally
            {
                request.Debug(Messages.DebugInfoReturnCall, "NuGetFilesFeed2", "InstallPackage");
            }
        }
    }
}
