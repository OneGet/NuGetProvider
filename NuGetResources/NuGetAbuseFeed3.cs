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

using Microsoft.PackageManagement.NuGetProvider.Resources;

namespace Microsoft.PackageManagement.NuGetProvider
{
    /// <summary>
    /// Abuse URI builder for NuGet v3.
    /// </summary>
    public class NuGetAbuseFeed3 : INuGetAbuseFeed
    {
        /// <summary>
        /// A URI template like: https://www.nuget.org/packages/{id}/{version}/ReportAbuse
        /// </summary>
        private string uriTemplate;

        public INuGetResourceCollection ResourcesCollection { get; set; }

        /// <summary>
        /// Creates a v3 resource using the abuse URI template specified in the index.json of NuGet v3. Expected to contain "{id}" and "{version}"
        /// </summary>
        public NuGetAbuseFeed3(string uriTemplate)
        {
            this.uriTemplate = uriTemplate;
        }

        public bool IsAvailable(RequestWrapper request) => true;

        /// <summary>
        /// Creates the report abuse URI by replacing {id} with packageId and {version} with packageVersion.
        /// </summary>
        public string MakeAbuseUri(string packageId, string packageVersion)
        {
            return this.uriTemplate.Replace(Constants.PackageIdTemplateParameter, packageId.ToLowerInvariant()).Replace(Constants.PackageVersionTemplateParameter, packageVersion.ToLowerInvariant());
        }
    }
}
