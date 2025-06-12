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
    /// <summary>
    /// Implements the gallery feed for nuget.org
    /// </summary>
    public class NuGetGalleryFeedOrg : INuGetGalleryFeed
    {
        private string galleryTemplateUri;
        public NuGetGalleryFeedOrg()
        {
            // For nuget.org, use the template: https://www.nuget.org/packages/{id-lower}/{version-lower}
            this.galleryTemplateUri = Constants.NuGetGalleryUriTemplate;
        }

        public INuGetResourceCollection ResourcesCollection { get; set; }

        public bool IsAvailable(RequestWrapper request) => true;

        public string MakeGalleryUri(string packageId, string packageVersion)
        {
            return this.galleryTemplateUri.Replace(Constants.PackageIdLowerTemplateParameter, packageId.ToLowerInvariant()).Replace(Constants.PackageVersionLowerTemplateParameter, packageVersion.ToLowerInvariant());
        }
    }
}
