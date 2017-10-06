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
    /// <summary>
    /// Contract for NuGet abuse URL builder.
    /// </summary>
    public interface INuGetAbuseFeed : INuGetFeed
    {
        /// <summary>
        /// Create an abuse URI for the given package and version.
        /// </summary>
        /// <param name="packageId">Package ID to construct the abuse URI for. Cannot be null.</param>
        /// <param name="packageVersion">Package version to construct the abuse URI for. Cannot be null.</param>
        /// <returns>The abuse URI</returns>
        string MakeAbuseUri(string packageId, string packageVersion);
    }
}
