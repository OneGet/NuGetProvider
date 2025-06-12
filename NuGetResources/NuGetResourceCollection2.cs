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
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Collection of NuGet v2 resources.
    /// </summary>
    public class NuGetResourceCollection2 : NuGetResourceCollectionBase
    {
        private NuGetResourceCollection2() { }

        public INuGetResourceCollection ResourcesCollection { get; set; }

        public static NuGetResourceCollection2 Make(string baseUrl)
        {
            NuGetResourceCollection2 collection = new NuGetResourceCollection2();
            if (baseUrl != null)
            {
                collection.PackagesFeed = new NuGetPackageFeed2(baseUrl);
                collection.QueryFeed = new NuGetQueryFeed2(baseUrl);
                collection.FilesFeed = new NuGetFilesFeed2();
            }

            collection.GetSearchQueryDelegate = (terms) =>
            {
                string searchString = String.Empty;
                IEnumerable<NuGetSearchTerm> tagTerms = terms.Where(st => st.Term == NuGetSearchTerm.NuGetSearchTermType.Tag);
                NuGetSearchTerm searchTerm = terms.Where(st => st.Term == NuGetSearchTerm.NuGetSearchTermType.SearchTerm).FirstOrDefault();
                if (searchTerm != null)
                {
                    searchString = searchTerm.Text;
                }

                searchString = terms.Where(st => st.Term == NuGetSearchTerm.NuGetSearchTermType.Tag)
                                          .Aggregate(searchString, (current, tag) => current + " tag:" + tag.Text);

                return searchString;
            };

            return collection;
        }
    }
}
