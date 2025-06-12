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
    using Microsoft.PackageManagement.Provider.Utility;
    using System;

    /// <summary>
    /// Extracts a PackageDependency.
    /// </summary>
    internal class PackageDependencyConverter : IDynamicJsonObjectConverter<PackageDependency, object>
    {
        public INuGetResourceCollection ResourcesCollection { get; set; }

        public PackageDependency Make(dynamic jsonObject)
        {
            PackageDependency pd = new PackageDependency();
            if (jsonObject.HasProperty("id"))
            {
                pd.Id = jsonObject.id;
            }

            if (jsonObject.HasProperty("range"))
            {
                pd.DependencyVersion = DependencyVersion.ParseDependencyVersion(jsonObject.range);
            }

            return pd;
        }

        /// <summary>
        /// Not currently needed. Implement when required.
        /// </summary>
        public PackageDependency Make(dynamic jsonObject, PackageDependency existingObject)
        {
            throw new NotImplementedException();
        }

        public PackageDependency Make(dynamic jsonObject, object args)
        {
            return Make(jsonObject);
        }
    }
}
