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

    /// <summary>
    /// Helps tie INuGetResources and their INuGetResourceCollection property to this collection.
    /// </summary>
    public class NuGetResourceCollectionBase : INuGetResourceCollection
    {
        private INuGetPackageFeed packagesFeed = null;
        private INuGetQueryFeed queryFeed = null;
        private INuGetFilesFeed filesFeed = null;
        private INuGetAbuseFeed abuseFeed = null;
        private INuGetGalleryFeed galleryFeed = null;
        private INuGetAutoCompleteFeed autoCompleteFeed = null;
        private IDynamicJsonObjectConverter<string, object> catalogUrlConverter = null;
        private IDynamicJsonObjectConverter<PackageBase, PackageEntryInfo> packageConverter = null;
        private IDynamicJsonObjectConverter<PackageDependency, object> packageDependencyConverter = null;
        private IDynamicJsonObjectConverter<PackageDependencySet, object> packageDependencySetConverter = null;

        public INuGetPackageFeed PackagesFeed
        {
            get
            {
                return packagesFeed;
            }
            protected set
            {
                packagesFeed = value;
                SetParentCollection(packagesFeed);
            }
        }

        public INuGetQueryFeed QueryFeed
        {
            get
            {
                return queryFeed;
            }
            protected set
            {
                queryFeed = value;
                SetParentCollection(queryFeed);
            }
        }

        public INuGetFilesFeed FilesFeed
        {
            get
            {
                return filesFeed;
            }
            protected set
            {
                filesFeed = value;
                SetParentCollection(filesFeed);
            }
        }

        public INuGetAbuseFeed AbuseFeed
        {
            get
            {
                return abuseFeed;
            }
            protected set
            {
                abuseFeed = value;
                SetParentCollection(abuseFeed);
            }
        }

        public INuGetGalleryFeed GalleryFeed
        {
            get
            {
                return galleryFeed;
            }
            protected set
            {
                galleryFeed = value;
                SetParentCollection(galleryFeed);
            }
        }

        public INuGetAutoCompleteFeed AutoCompleteFeed

        {
            get
            {
                return autoCompleteFeed;
            }
            protected set
            {
                autoCompleteFeed = value;
                SetParentCollection(autoCompleteFeed);
            }
        }

        public IDynamicJsonObjectConverter<string, object> CatalogUrlConverter
        {
            get
            {
                return catalogUrlConverter;
            }
            protected set
            {
                catalogUrlConverter = value;
                SetParentCollection(catalogUrlConverter);
            }
        }

        public IDynamicJsonObjectConverter<PackageBase, PackageEntryInfo> PackageConverter
        {
            get
            {
                return packageConverter;
            }
            protected set
            {
                packageConverter = value;
                SetParentCollection(packageConverter);
            }
        }

        public IDynamicJsonObjectConverter<PackageDependency, object> PackageDependencyConverter
        {
            get
            {
                return packageDependencyConverter;
            }
            protected set
            {
                packageDependencyConverter = value;
                SetParentCollection(packageDependencyConverter);
            }
        }

        public IDynamicJsonObjectConverter<PackageDependencySet, object> PackageDependencySetConverter
        {
            get
            {
                return packageDependencySetConverter;
            }
            protected set
            {
                packageDependencySetConverter = value;
                SetParentCollection(packageDependencySetConverter);
            }
        }

        public Func<IEnumerable<NuGetSearchTerm>, string> GetSearchQueryDelegate { get; protected set; }
        
        private void SetParentCollection(INuGetResource resource)
        {
            if (resource != null)
            {
                resource.ResourcesCollection = this;
            }
        }
    }
}
