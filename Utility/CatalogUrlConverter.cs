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
    /// Extracts the catalog entry URI from the given object.
    /// </summary>
    internal class CatalogUrlConverter : IDynamicJsonObjectConverter<string, object>
    {
        public INuGetResourceCollection ResourcesCollection { get; set; }

        public string Make(dynamic jsonObject)
        {
            if (jsonObject.catalogentry is string)
            {
                return jsonObject.catalogentry;
            }
            else
            {
                return jsonObject.catalogentry.Metadata.id;
            }
        }

        public string Make(dynamic jsonObject, string existingObject)
        {
            return Make(jsonObject);
        }

        public string Make(dynamic jsonObject, object args)
        {
            return Make(jsonObject);
        }
    }
}
