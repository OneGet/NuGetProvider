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
    using System.Collections.Generic;

    /// <summary>
    /// Contract for a NuGet query feed. This type of feed is generally used to access package files like the nuspec, nupkg.
    /// </summary>
    public interface INuGetFilesFeed : INuGetFeed
    {
        /// <summary>
        /// Download a package and its dependencies to the specified destination.
        /// </summary>
        /// <param name="packageView">PublicObjectView containing PackageItem object.</param>
        /// <param name="destination">Where to download package to.</param>
        /// <param name="request">Current request.</param>
        /// <returns>True if download was successful; false otherwise.</returns>
        bool DownloadPackage(PublicObjectView packageView, string destination, NuGetRequest request);

        /// <summary>
        /// Download a package and its dependencies to the specified destination.
        /// </summary>
        /// <param name="packageView">PublicObjectView containing PackageItem object.</param>
        /// <param name="destination">Where to download package to.</param>
        /// <param name="request">Current request.</param>
        /// <returns>True if download was successful; false otherwise.</returns>
        bool DownloadPackage(PublicObjectView packageView, string destination, RequestWrapper request);

        /// <summary>
        /// Install a package and its dependencies to a common location.
        /// </summary>
        /// <param name="packageView">PublicObjectView containing PackageItem object.</param>
        /// <param name="request">Current request.</param>
        /// <returns>True if install was successful; false otherwise.</returns>
        bool InstallPackage(PublicObjectView packageView, NuGetRequest request);

        /// <summary>
        /// Install a package and its dependencies to a common location.
        /// </summary>
        /// <param name="packageView">PublicObjectView containing PackageItem object.</param>
        /// <param name="request">Current request.</param>
        /// <returns>True if install was successful; false otherwise.</returns>
        bool InstallPackage(PublicObjectView packageView, RequestWrapper request);

        /// <summary>
        /// Creates or gets the content download URI from a given package.
        /// </summary>
        /// <param name="package">Non-null PackageBase</param>
        /// <returns>Download URI</returns>
        string MakeDownloadUri(PackageBase package);

        /// <summary>
        /// Get all versions from blob store.
        /// </summary>
        /// <param name="packageInfo">Existing PackageEntryInfo to add versions to</param>
        /// <param name="request">Current request.</param>
        /// <returns>PackageEntryInfo with versions filled in</returns>
        PackageEntryInfo GetVersionInfo(PackageEntryInfo packageInfo, RequestWrapper request);
    }
}
