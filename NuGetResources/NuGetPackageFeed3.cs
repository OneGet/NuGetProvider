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
    using System.IO;
    using System.Linq;

    /// <summary>
    /// Implements NuGet v3 package feed
    /// </summary>
    public class NuGetPackageFeed3 : NuGetRemoteFeedBase, INuGetPackageFeed
    {
        public NuGetPackageFeed3(NuGetServiceInfo primaryServiceEndpoint)
        {
            this.Endpoints.Add(primaryServiceEndpoint);
        }

        public NuGetSearchResult Find(NuGetSearchContext findContext, NuGetRequest request)
        {
            return Find(findContext, new RequestWrapper(request), request.AllowPrereleaseVersions.Value);
        }

        /// <summary>
        /// Find packages when the registration URL is already known.
        /// </summary>
        /// <param name="registrationUrl"></param>
        /// <param name="request"></param>
        /// <returns></returns>
        internal IEnumerable<PackageBase> Find(string registrationUrl, NuGetSearchContext context, RequestWrapper request, bool allowPrerelease)
        {
            request.Debug(Messages.DebugInfoCallMethod3, "NuGetPackageFeed3", "Find", registrationUrl);
            List<PackageBase> packages = null;
            Stream s = NuGetClient.DownloadDataToStream(registrationUrl, request, ignoreNullResponse: true);
            if (s != null)
            {
                packages = new List<PackageBase>();
                // Now we can get the catalog entry
                dynamic root = DynamicJsonParser.Parse(new StreamReader(s).ReadToEnd());
                if (root.Metadata.HasProperty("type"))
                {
                    // First check if this is a Package or PackageRegistration type
                    // Package is from packageId + version
                    // PackageRegistration is from packageId
                    bool isRegistrationType = false;
                    foreach (string t in root.Metadata.type)
                    {
                        if (t.Equals("PackageRegistration", StringComparison.OrdinalIgnoreCase))
                        {
                            isRegistrationType = true;
                            break;
                        }
                    }

                    // List of all catalogs we need to check
                    List<string> catalogUrls = new List<string>();
                    if (context.PackageInfo.AllVersions.Count == 0)
                    {
                        // Only when the version list is restricted is this method usually faster
                        this.ResourcesCollection.FilesFeed.GetVersionInfo(context.PackageInfo, request);
                    }

                    HashSet<SemanticVersion> packageSemanticVersions = null;
                    if (context.PackageInfo.AllVersions.Count > 0)
                    {
                        packageSemanticVersions = FilterVersionsByRequirements(context, context.PackageInfo);
                    }

                    if (isRegistrationType)
                    {
                        // This is a registration index, like: "https://api.nuget.org/v3/registration3/json/index.json"
                        // Get all versions from files service
                        if (!context.AllVersions && packageSemanticVersions != null)
                        {
                            foreach (SemanticVersion packageVersion in packageSemanticVersions)
                            {
                                NuGetSearchResult result = this.ResourcesCollection.PackagesFeed.Find(new NuGetSearchContext()
                                {
                                    PackageInfo = context.PackageInfo,
                                    RequiredVersion = packageVersion
                                }, request, allowPrerelease);
                                PackageBase package = result.Result == null ? null : result.Result.FirstOrDefault() as PackageBase;
                                if (package != null)
                                {
                                    packages.Add(package);
                                }
                            }
                        }
                        else
                        {
                            // Fallback to catalog crawling in these cases:
                            //      - Bypass deep metadata
                            //      - Getting version list failed
                            //      - All versions required
                            foreach (dynamic catalogPage in root.items)
                            {
                                dynamic actualCatalogPage = catalogPage;
                                if (!actualCatalogPage.HasProperty("items"))
                                {
                                    // Sometimes the catalog page on the PackageRegistration entry doesn't have the actual page contents
                                    // In this case, the ID metadata tag points to the full catalog entry
                                    Stream fullCatalogPageResponseStream = NuGetClient.DownloadDataToStream(actualCatalogPage.Metadata.id, request);
                                    if (fullCatalogPageResponseStream != null)
                                    {
                                        actualCatalogPage = DynamicJsonParser.Parse(new StreamReader(fullCatalogPageResponseStream).ReadToEnd());
                                    }
                                }
                                foreach (dynamic packageEntry in actualCatalogPage.items)
                                {
                                    // Check if the package should be retrieved/made
                                    if ((packageSemanticVersions == null || packageSemanticVersions.Contains(new SemanticVersion(packageEntry.catalogentry.version))))
                                    {
                                        if (context.EnableDeepMetadataBypass)
                                        {
                                            // Bypass retrieving "deep" (but required) metadata like packageHash
                                            PackageBase pb = this.ResourcesCollection.PackageConverter.Make(packageEntry.catalogentry, context.PackageInfo);
                                            if (pb != null)
                                            {
                                                packages.Add(pb);
                                            }
                                        }
                                        else
                                        {
                                            catalogUrls.Add(this.ResourcesCollection.CatalogUrlConverter.Make(packageEntry));
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        catalogUrls.Add(this.ResourcesCollection.CatalogUrlConverter.Make(root));
                    }

                    foreach (string catalogUrl in catalogUrls)
                    {
                        Stream catalogResponseStream = NuGetClient.DownloadDataToStream(catalogUrl, request);
                        if (catalogResponseStream != null)
                        {
                            string content = new StreamReader(catalogResponseStream).ReadToEnd();
                            dynamic catalogContent = DynamicJsonParser.Parse(content);
                            if ((packageSemanticVersions == null || packageSemanticVersions.Contains(new SemanticVersion(catalogContent.version))))
                            {
                                PackageBase pb = this.ResourcesCollection.PackageConverter.Make(DynamicJsonParser.Parse(content), context.PackageInfo);
                                if (pb != null)
                                {
                                    packages.Add(pb);
                                }
                            }
                        }
                        else
                        {
                            request.Warning(Messages.CouldNotGetResponseFromQuery, catalogUrl);
                        }
                    }
                }
                else
                {
                    request.Warning(Messages.JsonSchemaMismatch, "type");
                    request.Debug(Messages.JsonObjectDump, DynamicJsonParser.Serialize(root));
                }

                if (context.RequiredVersion == null && context.MinimumVersion == null && context.MaximumVersion == null && packages != null)
                {
                    PackageBase absoluteLatestPackage = packages.Where(p => p.IsPrerelease).OrderByDescending(pb => ((IPackage)pb).Version).FirstOrDefault();
                    if (absoluteLatestPackage != null)
                    {
                        absoluteLatestPackage.IsAbsoluteLatestVersion = true;
                    }

                    PackageBase latestPackage = packages.Where(p => !p.IsPrerelease).OrderByDescending(pb => ((IPackage)pb).Version).FirstOrDefault();
                    if (latestPackage != null)
                    {
                        latestPackage.IsLatestVersion = true;
                    }
                }
            }

            return packages;
        }

        private HashSet<SemanticVersion> FilterVersionsByRequirements(NuGetSearchContext findContext, PackageEntryInfo packageInfo)
        {
            HashSet<SemanticVersion> set = new HashSet<SemanticVersion>();
            if (findContext.MinimumVersion == null && findContext.MaximumVersion == null && findContext.RequiredVersion == null && !findContext.AllVersions)
            {
                if (findContext.AllowPrerelease)
                {
                    set.Add(packageInfo.AbsoluteLatestVersion);
                }
                else
                {
                    set.Add(packageInfo.LatestVersion);
                }
            }
            else
            {
                SemanticVersion latestVersion = null;
                foreach (SemanticVersion v in packageInfo.AllVersions)
                {
                    if ((findContext.MinimumVersion != null && findContext.MinimumVersion > v) || 
                        (findContext.MaximumVersion != null && findContext.MaximumVersion < v) || 
                        (findContext.RequiredVersion != null && findContext.RequiredVersion != v))
                    {
                        continue;
                    }

                    bool isAllowed = findContext.AllowPrerelease || String.IsNullOrWhiteSpace(v.SpecialVersion);
                    if (findContext.AllVersions && isAllowed)
                    {
                        set.Add(v);
                    }
                    else if (isAllowed)
                    {
                        if (latestVersion == null || latestVersion < v)
                        {
                            latestVersion = v;
                        }
                    }
                }

                if (latestVersion != null && !findContext.AllVersions)
                {
                    set.Add(latestVersion);
                }
            }

            return set;
        }

        private IEnumerable<IPackage> FindImpl(NuGetSearchContext findContext, RequestWrapper request, bool allowPrerelease)
        {
            request.Debug(Messages.DebugInfoCallMethod3, "NuGetPackageFeed3", "FindImpl", findContext.PackageInfo.Id);
            try
            {
                return base.Execute<IEnumerable<IPackage>>((baseUrl) => GetPackagesForBaseUrl(baseUrl, findContext, request, allowPrerelease));
            }
            finally
            {
                request.Debug(Messages.DebugInfoReturnCall, "NuGetPackageFeed3", "FindImpl");
            }
        }

        private IEnumerable<IPackage> GetPackagesForBaseUrl(string baseUrl, NuGetSearchContext findContext, RequestWrapper request, bool allowPrerelease)
        {
            if (findContext.PackageInfo.AllVersions.Count == 0)
            {
                this.ResourcesCollection.FilesFeed.GetVersionInfo(findContext.PackageInfo, request);
            }

            foreach (string candidateUrl in GetCandidateUrls(findContext.PackageInfo.Id, findContext.RequiredVersion, baseUrl))
            {
                IEnumerable<IPackage> packages = Find(candidateUrl, findContext, request, allowPrerelease);
                if (packages != null)
                {
                    foreach (IPackage package in packages)
                    {
                        yield return package;
                    }

                    break;
                }
            }
        }

        private IEnumerable<string> GetCandidateUrls(string packageId, SemanticVersion version, string baseUrl)
        {
            if (version != null)
            {
                // Ideally we want to try the version string first given to us by the user, but...
                string[] versions = version.GetComparableVersionStrings().ToArray();
                foreach (string candidateVersion in versions)
                {
                    yield return GetPackageVersionUrl(packageId, candidateVersion, baseUrl);
                }
            }
            else
            {
                yield return String.Format(Constants.NuGetRegistrationUrlTemplatePackage, baseUrl, baseUrl.EndsWith("/") ? String.Empty : "/", packageId.ToLowerInvariant());
            }
        }

        private string GetPackageVersionUrl(string packageId, string version, string baseUrl)
        {
            return String.Format(Constants.NuGetRegistrationUrlTemplatePackageVersion, baseUrl, baseUrl.EndsWith("/") ? String.Empty : "/", packageId.ToLowerInvariant(), version.ToLowerInvariant());
        }

        public override bool IsAvailable(RequestWrapper request)
        {
            try
            {
                // Execute a simple query against the query service. If there's no exception (the response came back successfully), the query service is up
                Find(new NuGetSearchContext()
                {
                    PackageInfo = new PackageEntryInfo(Constants.DummyPackageId)
                }, request, false);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public NuGetSearchResult Find(NuGetSearchContext findContext, RequestWrapper request, bool allowPrerelease)
        {
            request.Debug(Messages.DebugInfoCallMethod3, "NuGetPackageFeed3", "Find", findContext.PackageInfo.Id);
            if (System.Management.Automation.WildcardPattern.ContainsWildcardCharacters(findContext.PackageInfo.Id))
            {
                // Short circuit when there's wildcards - this will never work
                return findContext.MakeResult(new List<IPackage>());
            }

            NuGetSearchResult result = findContext.MakeResult(FindImpl(findContext, request, allowPrerelease), versionPostFilterRequired: false);
            request.Debug(Messages.DebugInfoReturnCall, "NuGetPackageFeed3", "Find");
            return result;
        }
    }
}
