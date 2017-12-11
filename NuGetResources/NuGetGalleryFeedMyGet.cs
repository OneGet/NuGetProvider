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
    using System.Text.RegularExpressions;

    /// <summary>
    /// Implements the gallery template for MyGet.
    /// e.g. https://powershell.myget.org/feed/powershell-core/package/nuget/System.Management.Automation/6.0.0-beta.9
    /// </summary>
    public class NuGetGalleryFeedMyGet : INuGetGalleryFeed
    {
        private string galleryTemplateUri = null;

        public INuGetResourceCollection ResourcesCollection { get; set; }

        public NuGetGalleryFeedMyGet(string myGetUrl)
        {
            // For now, assume myGetUrl is a NuGet v3 feed
            // First get the feed name
            Match m = Constants.MyGetFeedRegex.Match(myGetUrl);
            if (m.Success)
            {
                string feedName = m.Groups[1].Value;
                // Then build the gallery template
                Uri uri = new Uri(myGetUrl);
                this.galleryTemplateUri = String.Format(Constants.MyGetGalleryUriTemplate, uri.Scheme, uri.Host, feedName);
            }
        }

        public bool IsAvailable(RequestWrapper request) => true;

        public string MakeGalleryUri(string packageId, string packageVersion)
        {
            return this.galleryTemplateUri.Replace(Constants.PackageIdLowerTemplateParameter, packageId.ToLowerInvariant()).Replace(Constants.PackageVersionLowerTemplateParameter, packageVersion.ToLowerInvariant());
        }
    }
}
