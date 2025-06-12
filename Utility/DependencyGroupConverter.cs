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
    using System.Collections.Generic;

    /// <summary>
    /// Extracts the PackageDependencySet.
    /// </summary>
    internal class DependencyGroupConverter : IDynamicJsonObjectConverter<PackageDependencySet, object>
    {
        public INuGetResourceCollection ResourcesCollection { get; set; }

        public PackageDependencySet Make(dynamic jsonObject)
        {
            PackageDependencySet pds = new PackageDependencySet();
            pds.Dependencies = new List<PackageDependency>();

            if (jsonObject.HasProperty("targetframework"))
            {
                pds.TargetFramework = jsonObject.targetframework;
            }

            if (jsonObject.HasProperty("dependencies"))
            {
                foreach (dynamic dependencyObject in jsonObject.dependencies)
                {
                    pds.Dependencies.Add(this.ResourcesCollection.PackageDependencyConverter.Make(dependencyObject));
                }
            }

            return pds;
        }

        public PackageDependencySet Make(dynamic jsonObject, PackageDependencySet existingObject)
        {
            if (jsonObject.HasProperty("targetframework"))
            {
                existingObject.TargetFramework = jsonObject.targetframework;
            }

            if (jsonObject.HasProperty("dependencies"))
            {
                foreach (dynamic dependencyObject in jsonObject.dependencies)
                {
                    existingObject.Dependencies.Add(this.ResourcesCollection.PackageDependencyConverter.Make(dependencyObject));
                }
            }

            return existingObject;
        }

        public PackageDependencySet Make(dynamic jsonObject, object args)
        {
            return Make(jsonObject);
        }
    }
}
