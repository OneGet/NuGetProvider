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
            return Find(findContext, new RequestWrapper(request));
        }

        /// <summary>
        /// Find packages when the registration URL is already known.
        /// </summary>
        /// <param name="registrationUrl"></param>
        /// <param name="request"></param>
        /// <returns></returns>
        internal IEnumerable<PackageBase> Find(string registrationUrl, NuGetSearchContext context, RequestWrapper request, bool finalAttempt)
        {
            request.Debug(Messages.DebugInfoCallMethod3, "NuGetPackageFeed3", "Find", registrationUrl);
            List<PackageBase> packages = null;
            PackageBase cachedPackage;
            if (!registrationUrl.Contains("index.json") && ConcurrentInMemoryCache.Instance.TryGet<PackageBase>(registrationUrl, out cachedPackage))
            {
                if (cachedPackage == null)
                {
                    return packages;
                }
                else
                {
                    packages = new List<PackageBase>() { cachedPackage };
                    return packages;
                }
            }

            Stream s = NuGetClient.DownloadDataToStream(registrationUrl, request, ignoreNullResponse: true, tries: 1);
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
                        // In addition, when DeepMetadataBypass is enabled, we MUST use the registration index to get package info
                        // If a call to -Name -RequiredVersion is done, DeepMetadataBypass will never be enabled (for now)
                        // If we wanted, we could enable this by checking if !isRegistrationType && context.EnableDeepMetadataBypass, then call into Find with the registration index URL
                        if (!context.AllVersions && packageSemanticVersions != null && !context.EnableDeepMetadataBypass)
                        {
                            foreach (SemanticVersion packageVersion in packageSemanticVersions)
                            {
                                NuGetSearchResult result = this.ResourcesCollection.PackagesFeed.Find(new NuGetSearchContext()
                                {
                                    PackageInfo = context.PackageInfo,
                                    RequiredVersion = packageVersion,
                                    EnableDeepMetadataBypass = context.EnableDeepMetadataBypass
                                }, request);
                                PackageBase package = result.Result == null ? null : result.Result.FirstOrDefault() as PackageBase;
                                if (package != null)
                                {
                                    packages.Add(package);
                                }
                            }
                        }
                        else
                        {
                            // Going to collect versions from the registration index in here
                            // Map of package version -> either PackageBase (if context.EnableDeepMetadataBypass) or catalog URL
                            Dictionary<SemanticVersion, object> catalogObjects = new Dictionary<SemanticVersion, object>();
                            // If the version list hasn't been built yet, we can build it from the registration page instead of using FilesFeed
                            bool buildPackageInfoVersions = context.PackageInfo.AllVersions.Count == 0;
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
                                    SemanticVersion version = new SemanticVersion(packageEntry.catalogentry.version);
                                    if (buildPackageInfoVersions)
                                    {
                                        context.PackageInfo.AddVersion(version);
                                    }
                                        if (context.EnableDeepMetadataBypass)
                                        {
                                            // Bypass retrieving "deep" (but required) metadata like packageHash
                                            PackageBase pb = this.ResourcesCollection.PackageConverter.Make(packageEntry.catalogentry, context.PackageInfo);
                                            if (pb != null)
                                            {
                                                catalogObjects[version] = pb;
                                            }
                                        }
                                        else
                                        {
                                            catalogObjects[version] = this.ResourcesCollection.CatalogUrlConverter.Make(packageEntry);
                                        }
                                }
                            }

                            packageSemanticVersions = FilterVersionsByRequirements(context, context.PackageInfo);
                            foreach (SemanticVersion version in packageSemanticVersions)
                            {
                                if (!catalogObjects.ContainsKey(version))
                                {
                                    continue;
                                }

                                if (context.EnableDeepMetadataBypass)
                                {
                                    packages.Add((PackageBase)catalogObjects[version]);
                                } else
                                {
                                    PackageBase pb = GetPackageFromCatalogUrl((string)catalogObjects[version], request, packageSemanticVersions, context);
                                    if (pb != null)
                                    {
                                        packages.Add(pb);
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        PackageBase pb = ConcurrentInMemoryCache.Instance.GetOrAdd<PackageBase>(registrationUrl, () =>
                        {
                            return GetPackageFromCatalogUrl(this.ResourcesCollection.CatalogUrlConverter.Make(root), request, packageSemanticVersions, context);
                        });
                        if (pb != null)
                        {
                            packages.Add(pb);
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
            } else if (finalAttempt)
            {
                // This is the last retry of this URL. It's definitely not a good one.
                ConcurrentInMemoryCache.Instance.GetOrAdd<PackageBase>(registrationUrl, () => null);
            }

            return packages;
        }

        private PackageBase GetPackageFromCatalogUrl(string catalogUrl, RequestWrapper request, HashSet<SemanticVersion> packageSemanticVersions, NuGetSearchContext context)
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
                        return pb;
                    }
                }
            }
            else
            {
                request.Warning(Messages.CouldNotGetResponseFromQuery, catalogUrl);
            }

            return null;
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

        private IEnumerable<IPackage> FindImpl(NuGetSearchContext findContext, RequestWrapper request)
        {
            request.Debug(Messages.DebugInfoCallMethod3, "NuGetPackageFeed3", "FindImpl", findContext.PackageInfo.Id);
            try
            {
                return base.Execute<IEnumerable<IPackage>>((baseUrl) => GetPackagesForBaseUrl(baseUrl, findContext, request));
            }
            finally
            {
                request.Debug(Messages.DebugInfoReturnCall, "NuGetPackageFeed3", "FindImpl");
            }
        }

        private IEnumerable<IPackage> GetPackagesForBaseUrl(string baseUrl, NuGetSearchContext findContext, RequestWrapper request)
        {
            bool urlHit = false;
            int attempts = 3;
            while (!urlHit && attempts-- > 0)
            {
                foreach (string candidateUrl in GetCandidateUrls(findContext.PackageInfo.Id, findContext.RequiredVersion, baseUrl))
                {
                    IEnumerable<IPackage> packages = Find(candidateUrl, findContext, request, attempts == 0);
                    if (packages != null)
                    {
                        urlHit = true;
                        foreach (IPackage package in packages)
                        {
                            yield return package;
                        }

                        break;
                    }
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
                }, request);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public NuGetSearchResult Find(NuGetSearchContext findContext, RequestWrapper request)
        {
            request.Debug(Messages.DebugInfoCallMethod3, "NuGetPackageFeed3", "Find", findContext.PackageInfo.Id);
            if (System.Management.Automation.WildcardPattern.ContainsWildcardCharacters(findContext.PackageInfo.Id))
            {
                // Short circuit when there's wildcards - this will never work
                return findContext.MakeResult(new List<IPackage>());
            }

            NuGetSearchResult result = findContext.MakeResult(FindImpl(findContext, request), versionPostFilterRequired: false);
            request.Debug(Messages.DebugInfoReturnCall, "NuGetPackageFeed3", "Find");
            return result;
        }
    }
}
