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
    /// Contains information about a package as a whole entity (i.e. not versioned). For example, 'awssdk'.
    /// </summary>
    public class PackageEntryInfo
    {
        private HashSet<string> allVersionsSet = new HashSet<string>();
        private object addVersionLock = new object();

        public string Id { get; private set; }

        public IList<SemanticVersion> AllVersions { get; private set; }

        public SemanticVersion LatestVersion { get; private set; }

        public SemanticVersion AbsoluteLatestVersion { get; private set; }

        public PackageEntryInfo(string packageId)
        {
            this.Id = packageId;
            this.AllVersions = new List<SemanticVersion>();
        }

        public PackageEntryInfo AddVersion(SemanticVersion version)
        {
            string versionStr = version.ToString();
            if (!allVersionsSet.Contains(versionStr))
            {
                lock (addVersionLock)
                {
                    if (!allVersionsSet.Contains(versionStr))
                    {
                        this.AllVersions.Add(version);
                        if (String.IsNullOrWhiteSpace(version.SpecialVersion))
                        {
                            if (LatestVersion == null || LatestVersion < version)
                            {
                                LatestVersion = version;
                            }
                        }

                        if (AbsoluteLatestVersion == null || AbsoluteLatestVersion < version)
                        {
                            AbsoluteLatestVersion = version;
                        }

                        allVersionsSet.Add(versionStr);
                    }
                }
            }

            return this;
        }
    }
}
