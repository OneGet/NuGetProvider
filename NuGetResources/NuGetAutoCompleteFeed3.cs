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
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Management.Automation;

    /// <summary>
    /// Implements the client for the AutoComplete service for NuGet v3.
    /// </summary>
    public class NuGetAutoCompleteFeed3 : NuGetRemoteFeedBase, INuGetAutoCompleteFeed
    {
        public NuGetAutoCompleteFeed3(NuGetServiceInfo primaryServiceEndpoint)
        {
            this.Endpoints.Add(primaryServiceEndpoint);
        }

        public IEnumerable<string> Autocomplete(NuGetSearchTerm autocompleteSearchTerm, NuGetRequest request)
        {
            return Autocomplete(autocompleteSearchTerm, new RequestWrapper(request), request.AllowPrereleaseVersions.Value);
        }

        public override bool IsAvailable(RequestWrapper request)
        {
            foreach (NuGetServiceInfo endpoint in this.Endpoints)
            {
                Stream s = NuGetClient.DownloadDataToStream(endpoint.Url, request);
                if (s != null)
                {
                    return true;
                }
            }

            return false;
        }

        public IEnumerable<string> Autocomplete(NuGetSearchTerm autocompleteSearchTerm, RequestWrapper request, bool allowPrerelease)
        {
            return Autocomplete(autocompleteSearchTerm, request, allowPrerelease, acceptedPattern: null);
        }

        public IEnumerable<string> Autocomplete(NuGetSearchTerm autocompleteSearchTerm, RequestWrapper request, bool allowPrerelease, WildcardPattern acceptedPattern)
        {
            try
            {
                request.Debug(Messages.DebugInfoCallMethod3, "NuGetAutoCompleteFeed3", "Autocomplete", autocompleteSearchTerm.ToString());
                return base.Execute<IEnumerable<string>>((baseUrl) =>
                {
                    HttpQueryBuilder qb = new HttpQueryBuilder();
                    if (autocompleteSearchTerm.Term == NuGetSearchTerm.NuGetSearchTermType.Id)
                    {
                        qb.Add(Constants.PackageIdQueryParam, autocompleteSearchTerm.Text);
                    }
                    else if (autocompleteSearchTerm.Term == NuGetSearchTerm.NuGetSearchTermType.AutoComplete)
                    {
                        qb.Add(Constants.QueryQueryParam, autocompleteSearchTerm.Text);
                    }

                    if (allowPrerelease)
                    {
                        qb.Add(Constants.PrereleaseQueryParam, "true");
                    }

                    qb.Add(Constants.TakeQueryParam, Constants.SearchPageCount)
                        .Add(Constants.SemVerLevelQueryParam, Constants.SemVerLevel2);

                    return NuGetWebUtility.GetResults<dynamic, string>(request, (dynamic root) =>
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
                    }, (dynamic root) => GetAutoCompleteResults(root, autocompleteSearchTerm, acceptedPattern), (long resultsToSkip) =>
                    {
                        if (resultsToSkip > 0)
                        {
                            HttpQueryBuilder currentQb = qb.CloneAdd(Constants.SkipQueryParam, resultsToSkip.ToString());
                            return currentQb.AddQueryString(baseUrl);
                        }

                        return qb.AddQueryString(baseUrl);
                    }, (string content) =>
                    {
                        return DynamicJsonParser.Parse(content);
                    }, Constants.SearchPageCountInt);
                });
            }
            finally
            {
                request.Debug(Messages.DebugInfoReturnCall, "NuGetAutoCompleteFeed3", "Autocomplete");
            }
        }

        private IEnumerable<IEnumerable<string>> GetAutoCompleteResults(dynamic root, NuGetSearchTerm autocompleteSearchTerm, WildcardPattern acceptedPattern)
        {
            foreach (string rValue in root.data)
            {
                // "AutoComplete" for NuGet is slightly different than the PS definition
                // We only want the matches that match the input accepted pattern or start with the search term if no accepted pattern is used
                if (autocompleteSearchTerm.Term == NuGetSearchTerm.NuGetSearchTermType.AutoComplete && 
                    ((acceptedPattern == null && !rValue.StartsWith(autocompleteSearchTerm.Text, StringComparison.OrdinalIgnoreCase)) ||
                        acceptedPattern.IsMatch(rValue)))
                {
                    continue;
                }

                yield return YieldResultAsEnumerable(rValue);
            }
        }

        private IEnumerable<string> YieldResultAsEnumerable(string result)
        {
            yield return result;
        }

        
    }
}
