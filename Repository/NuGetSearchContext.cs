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
    /// Contains all required search information.
    /// </summary>
    public class NuGetSearchContext
    {
        /// <summary>
        /// Gets or sets the specific package to find.
        /// </summary>
        public PackageEntryInfo PackageInfo { get; set; }

        /// <summary>
        /// Gets or sets the search terms to execute.
        /// </summary>
        public IEnumerable<NuGetSearchTerm> SearchTerms { get; set; }

        /// <summary>
        /// Gets or sets the required version.
        /// </summary>
        public SemanticVersion RequiredVersion { get; set; }

        /// <summary>
        /// Gets or sets the minimum version.
        /// </summary>
        public SemanticVersion MinimumVersion { get; set; }

        /// <summary>
        /// Gets or sets the maximum version.
        /// </summary>
        public SemanticVersion MaximumVersion { get; set; }

        /// <summary>
        /// Gets or sets if prerelease versions are allowed.
        /// </summary>
        public bool AllowPrerelease { get; set; }

        /// <summary>
        /// Gets or sets if all versions should be returned.
        /// </summary>
        public bool AllVersions { get; set; }

        /// <summary>
        /// Gets or sets if "deep" (but required) metadata should be skipped. Safe to enable for search scenarios, but not find scenarios.
        /// </summary>
        public bool EnableDeepMetadataBypass { get; set; }

        /// <summary>
        /// Create a result after executing this search.
        /// </summary>
        /// <param name="result">Result packages</param>
        /// <param name="versionPostFilterRequired">True if versions haven't been filtered by the search, meaning the client should still filter versions; false otherwise.</param>
        /// <returns>Search result object.</returns>
        public NuGetSearchResult MakeResult(IEnumerable<IPackage> result, bool versionPostFilterRequired = true, bool namePostFilterRequired = true, bool containsPostFilterRequired = true)
        {
            return new NuGetSearchResult()
            {
                Result = result,
                VersionPostFilterRequired = versionPostFilterRequired,
                NamePostFilterRequired = namePostFilterRequired,
                ContainsPostFilterRequired = containsPostFilterRequired
            };
        }

        public NuGetSearchResult MakeResult()
        {
            return new NuGetSearchResult();
        }
    }

    /// <summary>
    /// Result of a NuGet Find/Search request.
    /// </summary>
    public class NuGetSearchResult
    {
        public IEnumerable<IPackage> Result { get; set; }

        public bool VersionPostFilterRequired { get; set; }

        public bool NamePostFilterRequired { get; set; }

        public bool ContainsPostFilterRequired { get; set; }

        public NuGetSearchResult()
        {
            this.VersionPostFilterRequired = true;
            this.NamePostFilterRequired = true;
            this.ContainsPostFilterRequired = true;
        }
    }
}
