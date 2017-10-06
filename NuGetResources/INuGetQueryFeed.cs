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
    /// Contract for a NuGet search feed. This type of feed is generally used to search for multiple packages given some criteria.
    /// </summary>
    public interface INuGetQueryFeed : INuGetFeed
    {
        /// <summary>
        /// Search for packages.
        /// </summary>
        /// <param name="searchContext">Contains search info.</param>
        /// <param name="nugetRequest">Current request.</param>
        /// <returns>All matching packages.</returns>
        NuGetSearchResult Search(NuGetSearchContext searchContext, NuGetRequest nugetRequest);

        /// <summary>
        /// Search for packages.
        /// </summary>
        /// <param name="searchContext">Contains search info.</param>
        /// <param name="nugetRequest">Current request.</param>
        /// <returns>All matching packages.</returns>
        NuGetSearchResult Search(NuGetSearchContext searchContext, RequestWrapper request, bool allowPrerelease);
    }
}
