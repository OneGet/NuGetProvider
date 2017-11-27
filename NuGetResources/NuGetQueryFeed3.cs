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
    using System.Globalization;
    using System.Linq;
    using System.Net.Http;
    using System.Text;

    /// <summary>
    /// Implements NuGet v3 query feed.
    /// </summary>
    public class NuGetQueryFeed3 : NuGetRemoteFeedBase, INuGetQueryFeed
    {
        private static FilterEntryByName FilterByName = new FilterEntryByName();

        public NuGetQueryFeed3(NuGetServiceInfo primaryServiceEndpoint)
        {
            this.Endpoints.Add(primaryServiceEndpoint);
        }

        public override bool IsAvailable(RequestWrapper request)
        {
            foreach (NuGetServiceInfo endpoint in this.Endpoints)
            {
                if (IsSingleEndpointAvailable(endpoint, request))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsSingleEndpointAvailable(NuGetServiceInfo endpoint, RequestWrapper request)
        {
            HttpClient client = request.GetClient();
            HttpResponseMessage response = PathUtility.GetHttpResponse(client, endpoint.Url, (() => request.IsCanceled()),
                ((msg, num) => request.Verbose(Resources.Messages.RetryingDownload, msg, num)), (msg) => request.Verbose(msg), (msg) => request.Debug(msg));
            if (response != null)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public NuGetSearchResult Search(NuGetSearchContext searchContext, NuGetRequest nugetRequest)
        {
            return Search(searchContext, new RequestWrapper(nugetRequest));
        }

        public NuGetSearchResult Search(NuGetSearchContext searchContext, RequestWrapper request)
        {
            // This is a search scenario, so it should be safe to skip some metadata for the sake of performance
            searchContext.EnableDeepMetadataBypass = true;
            return base.Execute<NuGetSearchResult>((baseUrl) =>
            {
                // For now we'll just get all versions and return the latest
                HttpQueryBuilder qb = new HttpQueryBuilder();
                // Once searchTermQb encodes the searchTerm, don't encode the ":" part of the resulting string
                qb.Add(Constants.QueryQueryParam, this.ResourcesCollection.GetSearchQueryDelegate(searchContext.SearchTerms), separator: "=", encode: false).Add(Constants.TakeQueryParam, Constants.SearchPageCount)
                    .Add(Constants.SemVerLevelQueryParam, Constants.SemVerLevel2);
                if (searchContext.AllowPrerelease)
                {
                    qb.Add(Constants.PrereleaseQueryParam, "true");
                }

                NuGetSearchTerm searchTerm = searchContext.SearchTerms.Where(st => st.Term == NuGetSearchTerm.NuGetSearchTermType.SearchTerm).FirstOrDefault();
                IEnumerable<IPackage> packages = SearchPackagesWithBackup(baseUrl, qb, request, searchContext, searchTerm);

                return searchContext.MakeResult(packages, versionPostFilterRequired: true, namePostFilterRequired: false, containsPostFilterRequired: false);
            });
        }

        private IEnumerable<IPackage> SearchPackagesWithBackup(string baseUrl, HttpQueryBuilder qb, RequestWrapper request, NuGetSearchContext searchContext, NuGetSearchTerm searchTerm)
        {
            // First execute the actual search
            HashSet<string> foundPackageIds = new HashSet<string>();
            return NuGetWebUtility.GetResults<dynamic, PackageBase>(request, (dynamic root) =>
            {
                long res = -1;
                if (root.HasProperty("totalhits"))
                {
                    res = root.totalhits;
                    request.Debug(Resources.Messages.TotalPackagesDiscovered, res);
                }
                else
                {
                    request.Warning(Resources.Messages.JsonSchemaMismatch, "totalhits");
                    request.Debug(Resources.Messages.JsonObjectDump, DynamicJsonParser.Serialize(root));
                }

                return res;
            }, (dynamic root) => GetPackageCollectionsForSearchResult(root, searchContext, searchTerm, foundPackageIds, request), (long packagesToSkip) =>
            {
                if (packagesToSkip > 0)
                {
                    HttpQueryBuilder currentQb = qb.CloneAdd(Constants.SkipQueryParam, packagesToSkip.ToString());
                    return currentQb.AddQueryString(baseUrl);
                }

                return qb.AddQueryString(baseUrl);
            }, (string content) =>
            {
                return DynamicJsonParser.Parse(content);
            }, Constants.SearchPageCountInt);
        }

        private IEnumerable<IEnumerable<PackageBase>> GetPackageCollectionsForSearchResult(dynamic searchResult, NuGetSearchContext searchContext, NuGetSearchTerm searchTerm, HashSet<string> foundPackageIds, RequestWrapper request)
        {
            if (searchResult.HasProperty("data"))
            {
                foreach (dynamic packageEntry in searchResult.data)
                {
                    foundPackageIds.Add(packageEntry.id);
                    yield return GetPackagesForPackageEntry(packageEntry, searchContext, request);
                }
            }
        }

        private IEnumerable<PackageBase> GetPackagesForPackageEntry(dynamic packageEntry, NuGetSearchContext searchContext, RequestWrapper request)
        {
            PackageEntryInfo packageEntryInfo = new PackageEntryInfo(packageEntry.id);
            
            if (!FilterByName.IsValid(packageEntryInfo, searchContext))
            {
                yield break;
            }
            
            // This will help us take packageEntryInfo.LatestVersion and packageEntryInfo.AbsoluteLatestVersion and get the matching package easily
            // We're not setting isLatestVersion here so we don't have to deal with "is it latest or absolute latest"
            Dictionary<SemanticVersion, PackageBase> versionToPackageTable = new Dictionary<SemanticVersion, PackageBase>();
            NuGetSearchContext individualPackageSearchContext = new NuGetSearchContext()
            {
                PackageInfo = packageEntryInfo,
                AllVersions = searchContext.AllVersions,
                AllowPrerelease = searchContext.AllowPrerelease,
                RequiredVersion = searchContext.RequiredVersion,
                MinimumVersion = searchContext.MinimumVersion,
                MaximumVersion = searchContext.MaximumVersion,
                EnableDeepMetadataBypass = searchContext.EnableDeepMetadataBypass
            };
            bool latestVersionRequired = !searchContext.AllVersions && searchContext.RequiredVersion == null && searchContext.MinimumVersion == null && searchContext.MaximumVersion == null;
            if (searchContext.EnableDeepMetadataBypass)
            {
                if (latestVersionRequired)
                {
                    // Use the search result page to get the metadata for the latest version
                    SemanticVersion individualPackageVersion = new SemanticVersion(packageEntry.version);
                    packageEntryInfo.AddVersion(individualPackageVersion);
                    PackageBase pb = this.ResourcesCollection.PackageConverter.Make(packageEntry);
                    if (pb != null)
                    {
                        yield return pb;
                    }
                }
                else
                {
                    // Go to the registration index of this package first. This allows us to bypass "deep" (but required) metadata in certain cases.
                    NuGetPackageFeed3 packageFeed3 = (NuGetPackageFeed3)this.ResourcesCollection.PackagesFeed;
                    NuGetSearchResult result = packageFeed3.Find(individualPackageSearchContext, request);
                    foreach (PackageBase pb in result.Result.Cast<PackageBase>())
                    {
                        yield return pb;
                    }
                }
            }
            else
            {
                // Either we want a specific version or we want all metadata for any packages
                foreach (dynamic packageVersionEntry in packageEntry.versions)
                {
                    if (packageEntry.version.Equals(packageVersionEntry.version) || searchContext.AllVersions)
                    {
                        if (packageVersionEntry.Metadata.HasProperty("id"))
                        {
                            // Collect all versions from the search results so we can manually set isLatestVersion and isAbsoluteLatestVersion later
                            SemanticVersion individualPackageVersion = new SemanticVersion(packageVersionEntry.version);
                            packageEntryInfo.AddVersion(individualPackageVersion);

                            // Skip prerelease versions if AllowPrereleaseVersions is not specified
                            if (!String.IsNullOrEmpty(individualPackageVersion.SpecialVersion) && !searchContext.AllowPrerelease)
                            {
                                continue;
                            }

                            long? versionDownloadCount = null;
                            if (packageVersionEntry.HasProperty("downloads"))
                            {
                                versionDownloadCount = packageVersionEntry.downloads;
                            }

                            string registrationUrl = packageVersionEntry.Metadata.id;
                            // This should be PackageFeed3
                            // There should be a better way to reuse this function
                            NuGetPackageFeed3 packageFeed3 = (NuGetPackageFeed3)this.ResourcesCollection.PackagesFeed;
                            PackageBase packageVersionPackage = packageFeed3.Find(registrationUrl, individualPackageSearchContext, request, true).FirstOrDefault();
                            if (packageVersionPackage != null)
                            {
                                if (versionDownloadCount.HasValue)
                                {
                                    packageVersionPackage.VersionDownloadCount = versionDownloadCount.Value;
                                }

                                // Reset these so we haven't collected all versions yet, so this is wrong
                                packageVersionPackage.IsLatestVersion = false;
                                packageVersionPackage.IsAbsoluteLatestVersion = false;

                                versionToPackageTable[individualPackageVersion] = packageVersionPackage;
                            }
                        }
                    }
                }

                // Now manually set the latest versions
                if (packageEntryInfo.LatestVersion != null && versionToPackageTable.ContainsKey(packageEntryInfo.LatestVersion))
                {
                    versionToPackageTable[packageEntryInfo.LatestVersion].IsLatestVersion = true;
                }

                if (packageEntryInfo.AbsoluteLatestVersion != null && versionToPackageTable.ContainsKey(packageEntryInfo.AbsoluteLatestVersion))
                {
                    versionToPackageTable[packageEntryInfo.AbsoluteLatestVersion].IsAbsoluteLatestVersion = true;
                }

                // I think this is the best we can do for enumeration (reads all versions of a package before yielding anything)
                foreach (PackageBase package in versionToPackageTable.Values)
                {
                    yield return package;
                }
            }
        }

        private static string LongestCommonSubstring(params string[] s)
        {
            int[] tableLengths = new int[s.Length];
            for (int i = 0; i < s.Length; i++)
            {
                tableLengths[i] = s[i].Length;
            }

            Array t = Array.CreateInstance(typeof(int), tableLengths);

            string substring = String.Empty;
            int longest = 0;
            foreach (int[] indices in GetIndexCombinations(0, s))
            {
                bool match = true;
                for (int j = 0; j < indices.Length - 1; j++)
                {
                    if (s[j][indices[j]] != s[j + 1][indices[j + 1]])
                    {
                        match = false;
                        break; ;
                    }
                }

                if (!match)
                {
                    continue;
                }
                if (indices.Any(i => i == 0))
                {
                    t.SetValue(1, indices);
                }
                else
                {
                    t.SetValue((int)t.GetValue(indices.Select(i => i - 1).ToArray()) + 1, indices);
                }

                if ((int)t.GetValue(indices) > longest)
                {
                    longest = (int)t.GetValue(indices);
                    substring = s[0].Substring(indices[0] - longest + 1, longest);
                }
            }

            return substring;
        }

        private static IEnumerable<int[]> GetIndexCombinations(int startingIndex, string[] s)
        {
            for (int i = 0; i < s[startingIndex].Length; i++)
            {
                if (startingIndex + 1 < s.Length)
                {
                    foreach (int[] comb in GetIndexCombinations(startingIndex + 1, s))
                    {
                        comb[startingIndex] = i;
                        yield return comb;
                    }
                }
                else
                {
                    int[] comb = new int[s.Length];
                    comb[startingIndex] = i;
                    yield return comb;
                }
            }
        }
    }
}
