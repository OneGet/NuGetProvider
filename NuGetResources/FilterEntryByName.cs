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
    using System;
    using System.Linq;
    using System.Management.Automation;


    /// <summary>
    /// Filter PackageEntryInfo by the original PS pattern (e.g. "aws*")
    /// </summary>
    internal class FilterEntryByName : IPackageEntryFilter
    {
        public bool IsValid(PackageEntryInfo packageEntry, NuGetSearchContext searchContext)
        {
            NuGetSearchTerm originalPsTerm = searchContext.SearchTerms == null ?
                null: searchContext.SearchTerms.Where(st => st.Term == NuGetSearchTerm.NuGetSearchTermType.OriginalPSPattern).FirstOrDefault();
            bool valid = true;
            if (originalPsTerm != null)
            {
                if (!String.IsNullOrWhiteSpace(originalPsTerm.Text) && WildcardPattern.ContainsWildcardCharacters(originalPsTerm.Text))
                {
                    // Applying the wildcard pattern matching
                    const WildcardOptions wildcardOptions = WildcardOptions.CultureInvariant | WildcardOptions.IgnoreCase;
                    var wildcardPattern = new WildcardPattern(originalPsTerm.Text, wildcardOptions);

                    valid = wildcardPattern.IsMatch(packageEntry.Id);
                }
                else if (!String.IsNullOrWhiteSpace(originalPsTerm.Text))
                {
                    valid = packageEntry.Id.IndexOf(originalPsTerm.Text, StringComparison.OrdinalIgnoreCase) > -1;
                }
            }

            return valid;
        }
    }
}
