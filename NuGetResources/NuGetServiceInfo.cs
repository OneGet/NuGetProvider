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
    using System.Collections.Generic;

    /// <summary>
    /// Parse and store a NuGet endpoint.
    /// </summary>
    public class NuGetServiceInfo
    {
        /// <summary>
        /// Maps known @type values to preference attributes. New @type values should be added only after testing.
        /// </summary>
        private static readonly Dictionary<string, NuGetServiceInfo> KnownServiceTypeStrings = new Dictionary<string, NuGetServiceInfo>()
        {
            { "searchqueryservice", new NuGetServiceInfo(NuGetServiceType.Query, 0) },
            { "searchqueryservice/3.0.0-beta", new NuGetServiceInfo(NuGetServiceType.Query, 1) },
            { "searchqueryservice/3.0.0-rc", new NuGetServiceInfo(NuGetServiceType.Query, 2) },

            { "searchautocompleteservice", new NuGetServiceInfo(NuGetServiceType.AutoComplete, 0) },
            { "searchautocompleteservice/3.0.0-beta", new NuGetServiceInfo(NuGetServiceType.AutoComplete, 1) },
            { "searchautocompleteservice/3.0.0-rc", new NuGetServiceInfo(NuGetServiceType.AutoComplete, 2) },

            { "registrationsbaseurl", new NuGetServiceInfo(NuGetServiceType.Registrations, 0) },
            { "registrationsbaseurl/versioned", new NuGetServiceInfo(NuGetServiceType.Registrations, 1) }, // not sure if this will change versions or not, since it uses clientversion property, so it's low in priority
            { "registrationsbaseurl/3.0.0-beta", new NuGetServiceInfo(NuGetServiceType.Registrations, 2) },
            { "registrationsbaseurl/3.4.0", new NuGetServiceInfo(NuGetServiceType.Registrations, 3) },
            { "registrationsbaseurl/3.6.0", new NuGetServiceInfo(NuGetServiceType.Registrations, 4) },

            { "packagebaseaddress/3.0.0", new NuGetServiceInfo(NuGetServiceType.Files, 0) },

            { "reportabuseuritemplate/3.0.0-beta", new NuGetServiceInfo(NuGetServiceType.ReportAbuse, 0) },
            { "reportabuseuritemplate/3.0.0-rc", new NuGetServiceInfo(NuGetServiceType.ReportAbuse, 1) },
        };

        /// <summary>
        /// Gets or sets the service URL. Can be templated if that's how it was given to us by the service index.
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// Gets the type of service.
        /// </summary>
        public NuGetServiceType Type { get; private set; }

        /// <summary>
        /// Gets the preference flag. Higher means more preferred.
        /// </summary>
        public int Preference { get; private set; }

        public NuGetServiceInfo(string serviceUrl, NuGetServiceType type, int preference)
        {
            this.Url = serviceUrl;
            this.Type = type;
            this.Preference = preference;
        }

        public NuGetServiceInfo(NuGetServiceType type, int preference) : this(null, type, preference)
        {
        }

        /// <summary>
        /// Extract a NuGet v3 endpoint from the service's entry root in NuGet v3's service index
        /// </summary>
        public static NuGetServiceInfo GetV3Endpoint(dynamic serviceEntryRoot)
        {
            string typeName, serviceUrl;
            typeName = serviceUrl = null;
            if (serviceEntryRoot.Metadata.HasProperty("type"))
            {
                typeName = serviceEntryRoot.Metadata.type.ToLowerInvariant();
            }

            if (serviceEntryRoot.Metadata.HasProperty("id"))
            {
                serviceUrl = serviceEntryRoot.Metadata.id;
            }

            if (KnownServiceTypeStrings.ContainsKey(typeName))
            {
                NuGetServiceInfo templateServiceInfo = KnownServiceTypeStrings[typeName];
                return new NuGetServiceInfo(serviceUrl, templateServiceInfo.Type, templateServiceInfo.Preference);
            }

            return null;
        }
    }
}
