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
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using SMA = System.Management.Automation;

    /// <summary>
    /// Utilities to filter packages by various criteria.
    /// </summary>
    internal class PackageFilterUtility
    {
        public static IEnumerable<IPackage> FilterOnTags(IEnumerable<IPackage> pkgs, string[] tag)
        {
            if (tag.Length == 0)
            {
                return pkgs;
            }

            //Tags should be performed as *AND* intead of *OR"
            //For example -FilterOnTag:{ "A", "B"}, the returned package should have both A and B.
            return pkgs.Where(pkg => tag.All(
                tagFromUser =>
                {
                    if (string.IsNullOrWhiteSpace(pkg.Tags))
                    {
                        // if there are tags and a package has no tag, don't return it
                        return false;
                    }
                    var tagArray = pkg.Tags.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    return tagArray.Any(tagFromPackage => tagFromPackage.EqualsIgnoreCase(tagFromUser));
                }
                ));
        }

        public static IEnumerable<IPackage> FilterOnContains(IEnumerable<IPackage> pkgs, string containsPattern)
        {
            if (string.IsNullOrWhiteSpace(containsPattern))
            {
                return pkgs;
            }

            return pkgs.Where(each => each.Description.IndexOf(containsPattern, StringComparison.OrdinalIgnoreCase) > -1 ||
                each.Id.IndexOf(containsPattern, StringComparison.OrdinalIgnoreCase) > -1);
        }

        public static IEnumerable<IPackage> FilterOnVersion(IEnumerable<IPackage> pkgs, string requiredVersion, string minimumVersion, string maximumVersion, bool minInclusive = true, bool maxInclusive = true)
        {
            if (!String.IsNullOrWhiteSpace(requiredVersion))
            {
                pkgs = pkgs.Where(each => each.Version == new SemanticVersion(requiredVersion));
            }
            else
            {
                if (!String.IsNullOrWhiteSpace(minimumVersion))
                {
                    // if minInclusive, then use >= else use >
                    if (minInclusive)
                    {
                        pkgs = pkgs.Where(each => each.Version >= new SemanticVersion(minimumVersion));
                    }
                    else
                    {
                        pkgs = pkgs.Where(each => each.Version > new SemanticVersion(minimumVersion));
                    }
                }

                if (!String.IsNullOrWhiteSpace(maximumVersion))
                {
                    // if maxInclusive, then use < else use <=
                    if (maxInclusive)
                    {
                        pkgs = pkgs.Where(each => each.Version <= new SemanticVersion(maximumVersion));
                    }
                    else
                    {
                        pkgs = pkgs.Where(each => each.Version < new SemanticVersion(maximumVersion));
                    }
                }
            }

            return pkgs;
        }

        public static IEnumerable<IPackage> FilterOnName(IEnumerable<IPackage> pkgs, string searchTerm, bool useWildCard)
        {
            if (useWildCard)
            {
                // Applying the wildcard pattern matching
                const SMA.WildcardOptions wildcardOptions = SMA.WildcardOptions.CultureInvariant | SMA.WildcardOptions.IgnoreCase;
                var wildcardPattern = new SMA.WildcardPattern(searchTerm, wildcardOptions);

                return pkgs.Where(p => wildcardPattern.IsMatch(p.Id));
            }
            else
            {
                return pkgs.Where(each => each.Id.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) > -1);
            }
        }

        public static bool IsValidByName(PackageEntryInfo packageEntry, NuGetSearchContext searchContext)
        {
            NuGetSearchTerm originalPsTerm = searchContext.SearchTerms == null ?
                null : searchContext.SearchTerms.Where(st => st.Term == NuGetSearchTerm.NuGetSearchTermType.OriginalPSPattern).FirstOrDefault();
            bool valid = true;
            if (originalPsTerm != null)
            {
                if (!String.IsNullOrWhiteSpace(originalPsTerm.Text) && SMA.WildcardPattern.ContainsWildcardCharacters(originalPsTerm.Text))
                {
                    // Applying the wildcard pattern matching
                    const SMA.WildcardOptions wildcardOptions = SMA.WildcardOptions.CultureInvariant | SMA.WildcardOptions.IgnoreCase;
                    var wildcardPattern = new SMA.WildcardPattern(originalPsTerm.Text, wildcardOptions);

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
