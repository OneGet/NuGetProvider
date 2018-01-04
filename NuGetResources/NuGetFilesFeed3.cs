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
    using Microsoft.PackageManagement.NuGetProvider.Resources;
    using Microsoft.PackageManagement.NuGetProvider.Utility;
    using Microsoft.PackageManagement.Provider.Utility;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;

    /// <summary>
    /// Implements the install/download functions for NuGet v3.
    /// </summary>
    public class NuGetFilesFeed3 : INuGetFilesFeed
    {
        /// <summary>
        /// Base URL like: https://api.nuget.org/v3-flatcontainer/
        /// </summary>
        private string baseUrl;

        public INuGetResourceCollection ResourcesCollection { get; set; }

        public NuGetFilesFeed3(string baseUrl)
        {
            this.baseUrl = baseUrl;
        }

        public bool DownloadPackage(PublicObjectView packageView, string destination, NuGetRequest request)
        {
            return DownloadPackage(packageView, destination, new RequestWrapper(request));
        }

        public bool InstallPackage(PublicObjectView packageView, NuGetRequest request)
        {
            return InstallPackage(packageView, new RequestWrapper(request));
        }

        public bool IsAvailable(RequestWrapper request) => true;

        public string MakeDownloadUri(PackageBase package)
        {
            string returnUri;
            if (!String.IsNullOrWhiteSpace(package.ContentSrcUrl))
            {
                returnUri = package.ContentSrcUrl;
            } else
            {
                // While nuget.org's index.json says: "https://api.nuget.org/v3-flatcontainer/{id-lower}/{id-lower}.{version-lower}.nupkg"
                // It's actually: "https://api.nuget.org/v3-flatcontainer/{id-lower}/{version-lower}/{id-lower}.{version-lower}.nupkg"
                // For NuGet and MyGet
                returnUri = String.Format(CultureInfo.InvariantCulture, Constants.NuGetDownloadUriTemplate, this.baseUrl, package.Id.ToLowerInvariant(), package.Version.ToLowerInvariant(), this.baseUrl.EndsWith("/") ? String.Empty : "/");
            }

            return returnUri;
        }

        public PackageEntryInfo GetVersionInfo(PackageEntryInfo packageInfo, RequestWrapper request)
        {
            try
            {
                request.Debug(Messages.DebugInfoCallMethod, "NuGetFilesFeed3", "GetVersionInfo");
                if (packageInfo == null)
                {
                    throw new ArgumentNullException("packageInfo");
                }
                if (request == null)
                {
                    throw new ArgumentNullException("request");
                }
                string query = String.Format(CultureInfo.InvariantCulture, Constants.VersionIndexTemplate, this.baseUrl, this.baseUrl.EndsWith("/") ? String.Empty : "/", packageInfo.Id.ToLowerInvariant());
                Stream queryResponse = NuGetClient.DownloadDataToStream(query, request, ignoreNullResponse: true);
                if (queryResponse != null)
                {
                    dynamic root = DynamicJsonParser.Parse(new StreamReader(queryResponse).ReadToEnd());
                    if (root.HasProperty("versions"))
                    {
                        foreach (string v in root.versions)
                        {
                            packageInfo.AddVersion(new SemanticVersion(v));
                        }
                    }
                    else
                    {
                        request.Debug(Messages.VersionIndexDownloadFailed, packageInfo.Id);
                    }
                }
                else
                {
                    request.Debug(Messages.VersionIndexDownloadFailed, packageInfo.Id);
                }
            }
            finally
            {
                request.Debug(Messages.DebugInfoReturnCall, "NuGetFilesFeed3", "GetVersionInfo");
            }

            return packageInfo;
        }

        private string MakeDownloadUri(PackageItem package)
        {
            PackageBase pb = package.Package as PackageBase;
            string httpquery = package.Package.ContentSrcUrl;
            if (String.IsNullOrEmpty(httpquery) && pb != null)
            {
                httpquery = this.MakeDownloadUri(pb);
            }

            return httpquery;
        }

        private bool InstallSinglePackage(PackageItem pkgItem, NuGetRequest request, ProgressTracker progressTracker)
        {
            bool packageWasInstalled = false;
            PackageItem packageToBeInstalled;
            request.Debug(Messages.DebugInfoCallMethod, "NuGetFilesFeed3", "InstallSinglePackage");
            try
            {
                if (pkgItem == null || pkgItem.PackageSource == null || pkgItem.PackageSource.Repository == null)
                {
                    return false;
                }

                // If the source location exists as a directory then we try to get the file location and provide to the packagelocal
                if (Directory.Exists(pkgItem.PackageSource.Location))
                {
                    var fileLocation = pkgItem.PackageSource.Repository.FindPackage(new NuGetSearchContext()
                    {
                        PackageInfo = new PackageEntryInfo(pkgItem.Id),
                        RequiredVersion = new Provider.Utility.SemanticVersion(pkgItem.Version)
                    }, request).FullFilePath;
                    packageToBeInstalled = NuGetClient.InstallPackageLocal(pkgItem.Id, pkgItem.Version, request, pkgItem.PackageSource, fileLocation, progressTracker);
                }
                else
                {
                    string httpquery = MakeDownloadUri(pkgItem);

                    // wait for the result from installpackage
                    packageToBeInstalled = NuGetClient.InstallPackage(pkgItem.Id, pkgItem.Version, request, pkgItem.PackageSource,
                        httpquery, pkgItem.Package.PackageHash, pkgItem.Package.PackageHashAlgorithm, progressTracker);
                }

                // Package is installed successfully
                if (packageToBeInstalled != null)
                {
                    // if this is a http repository, return metadata from online
                    if (!pkgItem.PackageSource.Repository.IsFile)
                    {
                        request.YieldPackage(pkgItem, packageToBeInstalled.PackageSource.Name, packageToBeInstalled.FullPath);
                    }
                    else
                    {
                        request.YieldPackage(packageToBeInstalled, packageToBeInstalled.PackageSource.Name, packageToBeInstalled.FullPath);
                    }


                    packageWasInstalled = true;
                }
                
                return packageWasInstalled;
            }
            finally
            {
                request.Debug(Messages.DebugInfoReturnCall, "NuGetFilesFeed3", "InstallSinglePackage");
            }
        }

        private bool DownloadSinglePackage(PackageItem pkgItem, NuGetRequest request, string destLocation, ProgressTracker progressTracker)
        {
            try
            {
                request.Debug(Messages.DebugInfoCallMethod, "NuGetFilesFeed3", "DownloadSinglePackage");
                if (string.IsNullOrWhiteSpace(pkgItem.PackageFilename) || pkgItem.PackageSource == null || pkgItem.PackageSource.Location == null
                || (pkgItem.PackageSource.IsSourceAFile && pkgItem.Package == null))
                {
                    request.WriteError(ErrorCategory.ObjectNotFound, pkgItem.Id, Constants.Messages.UnableToResolvePackage, pkgItem.Id);
                    return false;
                }

                // this is if the user says -force
                bool force = request.GetOptionValue("Force") != null;

                // combine the path and the file name
                destLocation = Path.Combine(destLocation, pkgItem.PackageFilename);

                // if the file already exists
                if (File.Exists(destLocation))
                {
                    // if no force, just return
                    if (!force)
                    {
                        request.Verbose(Constants.Messages.SkippedDownloadedPackage, pkgItem.Id);
                        request.YieldPackage(pkgItem, pkgItem.PackageSource.Name);
                        return true;
                    }

                    // here we know it is forced, so delete
                    FileUtility.DeleteFile(destLocation, isThrow: false);

                    // if after we try delete, it is still there, tells the user we can't perform the action
                    if (File.Exists(destLocation))
                    {
                        request.WriteError(ErrorCategory.ResourceUnavailable, destLocation, Constants.Messages.UnableToOverwriteExistingFile, destLocation);
                        return false;
                    }
                }

                bool downloadSuccessful = false;

                try
                {
                    // if no repository, we can't do anything
                    if (pkgItem.PackageSource.Repository == null)
                    {
                        return false;
                    }

                    if (pkgItem.PackageSource.Repository.IsFile)
                    {
                        using (var input = File.OpenRead(pkgItem.Package.FullFilePath))
                        {
                            using (var output = new FileStream(destLocation, FileMode.Create, FileAccess.Write, FileShare.Read))
                            {
                                input.CopyTo(output);
                                downloadSuccessful = true;
                            }
                        }
                    }
                    else
                    {
                        string httpquery = MakeDownloadUri(pkgItem);
                        if (!String.IsNullOrEmpty(httpquery))
                        {
                            downloadSuccessful = NuGetClient.DownloadPackage(pkgItem.Id, pkgItem.Version, destLocation, httpquery, request, pkgItem.PackageSource, progressTracker);
                        }
                        else
                        {
                            downloadSuccessful = false;
                            request.Warning(Messages.FailedToCreateDownloadUri, pkgItem.Id, pkgItem.Version);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ex.Dump(request);
                    return false;
                }

                if (downloadSuccessful)
                {
                    request.Verbose(Resources.Messages.SuccessfullyDownloaded, pkgItem.Id);
                    // provide the directory we save to to yieldpackage
                    request.YieldPackage(pkgItem, pkgItem.PackageSource.Name, Path.GetDirectoryName(destLocation));
                }

                return downloadSuccessful;
            } finally
            {
                request.Debug(Messages.DebugInfoReturnCall, "NuGetFilesFeed3", "DownloadSinglePackage");
            }
        }

        public bool DownloadPackage(PublicObjectView packageView, string destination, RequestWrapper request)
        {
            try
            {
                request.Debug(Messages.DebugInfoCallMethod3, "NuGetFilesFeed3", "DownloadPackage", destination);
                PackageItem package = packageView.GetValue<PackageItem>();
                // TODO: For now this has to require NuGetRequest, due to its usage of stuff like request.GetOptionValue and request.YieldPackage
                return NuGetClient.InstallOrDownloadPackageHelper(package, request.Request, Constants.Install,
                        (packageItem, progressTracker) => this.DownloadSinglePackage(packageItem, request.Request, destination, progressTracker));
            }
            finally
            {
                request.Debug(Messages.DebugInfoReturnCall, "NuGetFilesFeed3", "DownloadPackage");
            }
        }

        public bool InstallPackage(PublicObjectView packageView, RequestWrapper request)
        {
            try
            {
                request.Debug(Messages.DebugInfoCallMethod, "NuGetFilesFeed3", "InstallPackage");
                PackageItem package = packageView.GetValue<PackageItem>();
                request.Debug(Messages.DebugInfoCallMethod3, "NuGetFilesFeed3", "InstallPackage", package.FastPath);
                return NuGetClient.InstallOrDownloadPackageHelper(package, request.Request, Constants.Install,
                        (packageItem, progressTracker) => this.InstallSinglePackage(packageItem, request.Request, progressTracker));
            }
            finally
            {
                request.Debug(Messages.DebugInfoReturnCall, "NuGetFilesFeed3", "InstallPackage");
            }
        }
    }
}
