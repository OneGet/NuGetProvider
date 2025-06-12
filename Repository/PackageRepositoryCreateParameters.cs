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

    /// <summary>
    /// Parameters to create IPackageRepository
    /// </summary>
    public class PackageRepositoryCreateParameters
    {
        private object validationLock = new object();

        /// <summary>
        /// Gets or sets PackageSource location
        /// </summary>
        public string Location { get; set; }

        /// <summary>
        /// Gets or sets the flag determining if the location is valid. Null if the location has not been tested.
        /// </summary>
        public bool? LocationValid { get; set; }

        /// <summary>
        /// Gets or sets the NuGet request object associated with creation.
        /// </summary>
        public NuGetRequest Request { get; set; }
        public PackageRepositoryCreateParameters(string location, NuGetRequest request) : this(location, request, null) { }
        

        public PackageRepositoryCreateParameters(string location, NuGetRequest request, bool? locationValid)
        {
            this.Location = location;
            this.Request = request;
            this.LocationValid = locationValid;
        }

        /// <summary>
        /// Gets if the Location URI is valid.
        /// </summary>
        /// <returns>True if Location is valid and sets Location to the validated URI; false otherwise.</returns>
        public bool ValidateLocation()
        {
            if (!this.LocationValid.HasValue)
            {
                lock (validationLock)
                {
                    if (!this.LocationValid.HasValue)
                    {
                        Uri locationUri;
                        if (Uri.TryCreate(this.Location, UriKind.Absolute, out locationUri))
                        {
                            Uri validatedUri = NuGetPathUtility.ValidateUri(locationUri, this.Request);
                            if (validatedUri != null)
                            {
                                this.Location = validatedUri.AbsoluteUri;
                                this.LocationValid = true;
                            }
                            else
                            {
                                this.LocationValid = false;
                            }
                        }
                    }
                }
            }

            return this.LocationValid.Value;
        }
    }
}
