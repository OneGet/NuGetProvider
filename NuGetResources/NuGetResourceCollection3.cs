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

    /// <summary>
    /// Collection of NuGet v3 resources.
    /// </summary>
    public class NuGetResourceCollection3 : NuGetResourceCollectionBase
    {
        private NuGetResourceCollection3() { }

        public INuGetResourceCollection ResourcesCollection { get; set; }

        public static NuGetResourceCollection3 Make(dynamic root, string baseUrl, RequestWrapper request)
        {
            request.Debug(Messages.DebugInfoCallMethod3, "NuGetResourceCollection3", "Make", baseUrl);
            NuGetResourceCollection3 collection = new NuGetResourceCollection3();
            Dictionary<NuGetServiceType, NuGetServiceInfo> currentServiceMap = new Dictionary<NuGetServiceType, NuGetServiceInfo>();
            foreach (dynamic resourceElement in root.resources)
            {
                NuGetServiceInfo serviceInfo = NuGetServiceInfo.GetV3Endpoint(resourceElement);
                if (serviceInfo == null)
                {
                    continue;
                }
                
                bool serviceUsed = currentServiceMap.ContainsKey(serviceInfo.Type) && currentServiceMap[serviceInfo.Type].Preference <= serviceInfo.Preference;
                bool serviceSupplement = currentServiceMap.ContainsKey(serviceInfo.Type) && currentServiceMap[serviceInfo.Type].Preference == serviceInfo.Preference;
                switch (serviceInfo.Type)
                {
                    case NuGetServiceType.AutoComplete:
                        // No feed yet OR no version (lowest possible stable version) OR greater version
                        if (serviceUsed || collection.AutoCompleteFeed == null)
                        {
                            serviceUsed = true;
                            if (serviceSupplement)
                            {
                                ((NuGetAutoCompleteFeed3)collection.AutoCompleteFeed).Endpoints.Add(serviceInfo);
                            }
                            else
                            {
                                collection.AutoCompleteFeed = new NuGetAutoCompleteFeed3(serviceInfo);
                            }
                        }

                        break;
                    case NuGetServiceType.Registrations:
                        if (serviceUsed || collection.PackagesFeed == null)
                        {
                            serviceUsed = true;
                            if (serviceSupplement)
                            {
                                ((NuGetPackageFeed3)collection.PackagesFeed).Endpoints.Add(serviceInfo);
                            }
                            else
                            {
                                collection.PackagesFeed = new NuGetPackageFeed3(serviceInfo);
                            }
                        }

                        break;
                    case NuGetServiceType.Query:
                        if (serviceUsed || collection.QueryFeed == null)
                        {
                            serviceUsed = true;
                            if (serviceSupplement)
                            {
                                ((NuGetQueryFeed3)collection.QueryFeed).Endpoints.Add(serviceInfo);
                            }
                            else
                            {
                                collection.QueryFeed = new NuGetQueryFeed3(serviceInfo);
                            }
                        }

                        break;
                    case NuGetServiceType.Files:
                        if (serviceUsed || collection.FilesFeed == null)
                        {
                            serviceUsed = true;
                            collection.FilesFeed = new NuGetFilesFeed3(serviceInfo.Url);
                        }

                        break;
                    case NuGetServiceType.ReportAbuse:
                        if (serviceUsed || collection.AbuseFeed == null)
                        {
                            serviceUsed = true;
                            collection.AbuseFeed = new NuGetAbuseFeed3(serviceInfo.Url);
                        }

                        break;
                }

                if (serviceUsed)
                {
                    request.Debug(Messages.NuGetEndpointDiscovered, serviceInfo.Type, serviceInfo.Url);
                    if (!serviceSupplement)
                    {
                        currentServiceMap[serviceInfo.Type] = serviceInfo;
                    }
                }
            }

            collection.GetSearchQueryDelegate = (searchTerms) =>
            {
                string searchString = String.Empty;
                // TODO: encode search terms?
                foreach (NuGetSearchTerm searchTerm in searchTerms)
                {
                    switch (searchTerm.Term)
                    {
                        case NuGetSearchTerm.NuGetSearchTermType.SearchTerm:
                            searchString += searchTerm.Text;
                            break;
                        case NuGetSearchTerm.NuGetSearchTermType.Tag:
                            HttpQueryBuilder tQb = new HttpQueryBuilder().Add(Constants.TagQueryParam, searchTerm.Text, separator: ":", encode: false);
                            searchString += " " + tQb.ToQueryString();
                            break;
                        case NuGetSearchTerm.NuGetSearchTermType.Contains:
                            HttpQueryBuilder cQb = new HttpQueryBuilder().Add(Constants.DescriptionQueryParam, searchTerm.Text, separator: ":", encode: false);
                            searchString += " " + cQb.ToQueryString();
                            break;
                        default:
                            break;
                    }
                }

                return searchString.Trim();
            };

            collection.PackageConverter = new PackageBaseConverter();
            collection.PackageDependencyConverter = new PackageDependencyConverter();
            collection.PackageDependencySetConverter = new DependencyGroupConverter();
            collection.CatalogUrlConverter = new CatalogUrlConverter();
            Uri uri = new Uri(baseUrl);
            if (uri.Host.Contains(Constants.NuGetOrgHost))
            {
                collection.GalleryFeed = new NuGetGalleryFeedOrg();
            } else if (uri.Host.Contains(Constants.MyGetOrgHost))
            {
                collection.GalleryFeed = new NuGetGalleryFeedMyGet(baseUrl);
            }

            request.Debug(Messages.DebugInfoReturnCall, "NuGetResourceCollection3", "Make");
            return collection;
        }
    }
}
