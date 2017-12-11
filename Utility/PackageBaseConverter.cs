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
    using System.Collections;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    
    /// <summary>
    /// Converter that creates PackageBase objects from dynamic JSON object.
    /// </summary>
    internal class PackageBaseConverter : IDynamicJsonObjectConverter<PackageBase, PackageEntryInfo>
    {
        public INuGetResourceCollection ResourcesCollection { get; set; }

        public PackageBase Make(dynamic jsonObject)
        {
            PackageBase pb = new PackageBase();
            pb.DependencySetList = new List<PackageDependencySet>();
            return Make(jsonObject, pb);
        }

        public PackageBase Make(dynamic jsonObject, PackageBase pb)
        {
            // In some cases, these types are pre-parsed by something
            // In others, they're in raw string form, so these vars are used as dummy targets for parsing
            Version versionResult;
            DateTime dtResult;
            Uri uriResult;

            if (jsonObject.HasProperty("listed") && !((bool)jsonObject.listed))
            {
                return null;
            }

            if (jsonObject.HasProperty("id"))
            {
                pb.Id = jsonObject.id;
            }

            if (jsonObject.HasProperty("version"))
            {
                pb.Version = jsonObject.version;
            }

            if (jsonObject.HasProperty("published") && jsonObject.published != null)
            {
                if (jsonObject.published is DateTime)
                {
                    pb.Published = jsonObject.published;
                }
                else
                {
                    if (DateTime.TryParse(jsonObject.published, out dtResult))
                    {
                        pb.Published = dtResult;
                    }
                }
            }

            if (jsonObject.HasProperty("minclientversion") && jsonObject.minclientversion != null)
            {
                if (jsonObject.minclientversion is Version)
                {
                    pb.MinClientVersion = jsonObject.minclientversion;
                }
                else if (!String.IsNullOrWhiteSpace(jsonObject.minclientversion))
                {
                    if (Version.TryParse(jsonObject.minclientversion, out versionResult))
                    {
                        pb.MinClientVersion = versionResult;
                    }
                }
            }

            if (jsonObject.HasProperty("authors"))
            {
                if (jsonObject.authors is string)
                {
                    pb.Authors = jsonObject.authors;
                }
                else if (jsonObject.authors is ArrayList)
                {
                    StringBuilder authorsSb = new StringBuilder();
                    bool firstAuthor = true;
                    foreach (string author in jsonObject.authors)
                    {
                        authorsSb.AppendFormat("{0}{1}", firstAuthor ? String.Empty : ",", author);
                    }

                    pb.Authors = authorsSb.ToString();
                }
            }

            if (jsonObject.HasProperty("licenseurl") && jsonObject.licenseurl != null)
            {
                if (jsonObject.licenseurl is Uri)
                {
                    pb.LicenseUrl = jsonObject.licenseurl;
                }
                else if (!String.IsNullOrWhiteSpace(jsonObject.licenseurl))
                {
                    if (Uri.TryCreate(jsonObject.licenseurl, UriKind.Absolute, out uriResult))
                    {
                        pb.LicenseUrl = uriResult;
                    }
                }
            }

            if (jsonObject.HasProperty("projecturl") && jsonObject.projecturl != null)
            {
                if (jsonObject.projecturl is Uri)
                {
                    pb.ProjectUrl = jsonObject.projecturl;
                }
                else if (!String.IsNullOrWhiteSpace(jsonObject.projecturl))
                {
                    if (Uri.TryCreate(jsonObject.projecturl, UriKind.Absolute, out uriResult))
                    {
                        pb.ProjectUrl = uriResult;
                    }
                }
            }

            if (jsonObject.HasProperty("iconurl") && jsonObject.iconurl != null)
            {
                if (jsonObject.iconurl is Uri)
                {
                    pb.IconUrl = jsonObject.iconurl;
                }
                else if (!String.IsNullOrWhiteSpace(jsonObject.iconurl))
                {
                    if (Uri.TryCreate(jsonObject.iconurl, UriKind.Absolute, out uriResult))
                    {
                        pb.IconUrl = uriResult;
                    }
                }
            }

            if (jsonObject.HasProperty("requirelicenseacceptance"))
            {
                pb.RequireLicenseAcceptance = jsonObject.requirelicenseacceptance;
            }

            if (jsonObject.HasProperty("description"))
            {
                pb.Description = jsonObject.description;
            }

            if (jsonObject.HasProperty("releasenotes"))
            {
                pb.ReleaseNotes = jsonObject.releasenotes;
            }

            if (jsonObject.HasProperty("copyright"))
            {
                pb.Copyright = jsonObject.copyright;
            }

            if (jsonObject.HasProperty("title"))
            {
                pb.Title = jsonObject.title;
            }

            if (jsonObject.HasProperty("created") && jsonObject.created != null)
            {
                if (jsonObject.created is DateTime)
                {
                    pb.Created = jsonObject.created;
                }
                else
                {
                    if (DateTime.TryParse(jsonObject.created, out dtResult))
                    {
                        pb.Created = dtResult;
                    }
                }
            }

            if (jsonObject.HasProperty("lastedited") && jsonObject.lastedited != null)
            {
                if (jsonObject.lastedited is DateTime)
                {
                    pb.LastEdited = jsonObject.lastedited;
                }
                else
                {
                    if (DateTime.TryParse(jsonObject.lastedited, out dtResult))
                    {
                        pb.LastEdited = dtResult;
                    }
                }
            }

            if (jsonObject.HasProperty("packagehash"))
            {
                pb.PackageHash = jsonObject.packagehash;
            }

            if (jsonObject.HasProperty("packagehashalgorithm"))
            {
                pb.PackageHashAlgorithm = jsonObject.packagehashalgorithm;
            }

            if (jsonObject.HasProperty("packagesize"))
            {
                pb.PackageSize = jsonObject.packagesize;
            }

            if (jsonObject.HasProperty("dependencygroups"))
            {
                foreach (dynamic dependencyGroup in jsonObject.dependencygroups)
                {
                    pb.DependencySetList.Add(this.ResourcesCollection.PackageDependencySetConverter.Make(dependencyGroup));
                }
            }

            

            if (jsonObject.HasProperty("tags"))
            {
                if (jsonObject.tags is ArrayList)
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (string tag in jsonObject.tags)
                    {
                        sb.AppendFormat(CultureInfo.InvariantCulture, "{0}, ", tag);
                    }

                    pb.Tags = sb.ToString();
                    if (pb.Tags.Length > 0)
                    {
                        // If any tags, cut off the last ", "
                        pb.Tags = pb.Tags.Substring(0, pb.Tags.Length - 2);
                    }
                }
            }

            if (jsonObject.HasProperty("language"))
            {
                pb.Language = jsonObject.language;
            }

            if (jsonObject.HasProperty("summary"))
            {
                pb.Summary = jsonObject.summary;
            }

            if (jsonObject.HasProperty("totaldownloads"))
            {
                pb.DownloadCount = jsonObject.totaldownloads;
            }

            if (jsonObject.HasProperty("isprerelease"))
            {
                pb.IsPrerelease = jsonObject.isprerelease;
            }

            if (this.ResourcesCollection.AbuseFeed != null)
            {
                // TODO: Is it right to assign LicenseReportUrl this as well?
                Uri reportUrl = new Uri(this.ResourcesCollection.AbuseFeed.MakeAbuseUri(pb.Id, pb.Version));
                pb.ReportAbuseUrl = reportUrl;
            }

            if (this.ResourcesCollection.FilesFeed != null)
            {
                pb.ContentSrcUrl = this.ResourcesCollection.FilesFeed.MakeDownloadUri(pb);
            }

            if (this.ResourcesCollection.GalleryFeed != null)
            {
                pb.GalleryDetailsUrl = new Uri(this.ResourcesCollection.GalleryFeed.MakeGalleryUri(pb.Id, pb.Version));
            }

            if (jsonObject.HasProperty("catalogentry"))
            {
                // Sometimes some of the package's properties are moved to the catalogEntry object
                Make(jsonObject.catalogentry, pb);
            }

            return pb;
        }

        public PackageBase Make(dynamic jsonObject, PackageEntryInfo args)
        {
            PackageBase pb = Make(jsonObject);
            if (pb != null)
            {
                //For now, let args.LatestVersion == null and args.AbsoluteLatestVersion == null be undefined behavior as if it happens we have way bigger problems
                pb.IsLatestVersion = args.LatestVersion != null && ((IPackage)pb).Version == args.LatestVersion;
                pb.IsAbsoluteLatestVersion = args.AbsoluteLatestVersion != null && ((IPackage)pb).Version == args.AbsoluteLatestVersion;
            }

            return pb;
        }
    }
}
