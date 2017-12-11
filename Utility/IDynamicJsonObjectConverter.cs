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
    /// Interface to convert a dynamic JSON object to a strongly-typed object. 
    /// </summary>
    /// <typeparam name="T">Type of object to create.</typeparam>
    /// <typeparam name="A">Args type</typeparam>
    public interface IDynamicJsonObjectConverter<T, A> : INuGetResource
    {
        T Make(dynamic jsonObject);
        T Make(dynamic jsonObject, A args);
        T Make(dynamic jsonObject, T existingObject);
    }
}
