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
    /// Contract for a generic NuGet resource.
    /// </summary>
    public interface INuGetResource
    {
        /// <summary>
        /// Gets the resources collection of which this resource is a part.
        /// </summary>
        INuGetResourceCollection ResourcesCollection { get; set; }
    }
}
