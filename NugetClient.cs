namespace Microsoft.PackageManagement.NuGetProvider 
{
    using Resources;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Net.Http;
    using System.Threading.Tasks;
    using System.Linq;
    using System.Xml.Linq;
    using System.IO.Compression;
    using System.Security.Cryptography;

    /// <summary>
    /// Utility to handle the Find, Install, Uninstall-Package etc operations.
    /// </summary>
    internal static class NuGetClient
    {
        /// <summary>
        /// Find the package via the given uri query.
        /// </summary>
        /// <param name="query">A full Uri. A sample Uri looks like "http://www.nuget.org/api/v2/FindPackagesById()?id='Jquery'" </param>
        /// <param name="request">An object passed in from the PackageManagement that contains functions that can be used to interact with its Provider</param> 
        /// <returns>Package objects</returns>
        internal static IEnumerable<PackageBase> FindPackage(string query, Request request) {
            request.Debug(Messages.DebugInfoCallMethod, "NuGetClient", "FindPackage");

            return HttpClientPackageRepository.SendRequest(query, request);
        }

        /// <summary>
        /// Download a package that matches the given version and name and install it on the local system.
        /// </summary>
        /// <param name="packageName">Package name</param>
        /// <param name="version">Package version</param>
        /// <param name="request">An object passed in from the PackageManagement platform that contains APIs that can be used to interact with it </param>  
        /// <param name="source">Package source</param>
        /// <param name="queryUrl">Full uri</param>
        /// <param name="packageHash">the hash of the package</param>
        /// <param name="packageHashAlgorithm">the hash algorithm of the package</param>
        /// <returns>PackageItem object</returns>
        internal static PackageItem InstallPackage(
            string packageName,
            string version,
            NuGetRequest request,
            PackageSource source,
            string queryUrl,
            string packageHash,
            string packageHashAlgorithm
            ) 
        {
            request.Debug(Messages.DebugInfoCallMethod, "NuGetClient", "InstallPackage");

            //If the destination folder does not exists, create it
            string destinationPath = request.Destination;
            request.Verbose(string.Format(CultureInfo.InvariantCulture, "InstallPackage' - name='{0}', version='{1}',destination='{2}'", packageName, version, destinationPath));
 
            if (!Directory.Exists(destinationPath)) {
                Directory.CreateDirectory(destinationPath);
            }

            //Create a folder under the destination path to hold the package
            string installDir = FileUtility.MakePackageDirectoryName(request.ExcludeVersion.Value, destinationPath, packageName, version);

            if (!Directory.Exists(installDir)) {
                Directory.CreateDirectory(installDir);
            }

            //Get the package file name based on the version and id
            string fileName = FileUtility.MakePackageFileName(request.ExcludeVersion.Value, packageName, version);

            string installFullPath = Path.Combine(installDir, fileName);

            //download to fetch the package
            DownloadPackage(packageName, version, installFullPath, queryUrl, request);

            // check that we have the file
            if (!File.Exists(installFullPath))
            {
                request.WriteError(ErrorCategory.ResourceUnavailable, installFullPath, Constants.Messages.PackageFailedInstall, packageName);
                return null;
            }

            // checks that there is hash. Otherwise we don't carry out the install
            if (string.IsNullOrWhiteSpace(packageHash) || string.IsNullOrWhiteSpace(packageHashAlgorithm))
            {
                request.WriteError(ErrorCategory.SecurityError, packageName, Constants.Messages.HashNotFound, packageName);
                // delete the file downloaded. VIRUS!!!
                FileUtility.DeleteFile(installFullPath, isThrow: false);
                return null;
            }

            // Verify the hash
            using (FileStream stream = File.OpenRead(installFullPath))
            {
                HashAlgorithm hashAlgorithm = null;

                switch (packageHash.ToLowerInvariant())
                {
                    case "sha256":
                        hashAlgorithm = SHA256.Create();
                        break;

                    case "md5":
                        hashAlgorithm = MD5.Create();
                        break;

                    case "sha512":
                    // Flows to default case

                    // default to sha512 algorithm
                    default:
                        hashAlgorithm = SHA512.Create();
                        break;
                }

                if (hashAlgorithm == null)
                {
                    request.Warning(Constants.Messages.HashNotSupported);
                    // delete the file downloaded. VIRUS!!!
                    FileUtility.DeleteFile(installFullPath, isThrow: false);
                    return null;
                }

                // compute the hash
                byte[] computedHash = hashAlgorithm.ComputeHash(stream);

                // convert the original hash we got from the feed
                byte[] downloadedHash = Convert.FromBase64String(packageHash);

                // check if they are equal
                if (Enumerable.SequenceEqual(computedHash, downloadedHash))
                {
                    //parse the package
                    var pkgItem = InstallPackageLocal(packageName, version, request, source, installFullPath);
                    return pkgItem;
                }

                // delete the file downloaded. VIRUS!!!
                FileUtility.DeleteFile(installFullPath, isThrow: false);
                request.WriteError(ErrorCategory.SecurityError, packageName, Constants.Messages.HashNotMatch, packageName);
            }

            return null;
        }

        /// <summary>
        /// Install a single package without checking for dependencies
        /// </summary>
        /// <param name="pkgItem"></param>
        /// <param name="request"></param>
        /// <returns></returns>
        private static bool InstallSinglePackage(PackageItem pkgItem, NuGetRequest request)
        {
            PackageItem packageToBeInstalled;

            // If the source location exists as a directory then we try to get the file location and provide to the packagelocal
            if (Directory.Exists(pkgItem.PackageSource.Location))
            {
                var fileLocation = pkgItem.PackageSource.Repository.FindPackage(pkgItem.Id, new SemanticVersion(pkgItem.Version), request).FullFilePath;
                packageToBeInstalled = NuGetClient.InstallPackageLocal(pkgItem.Id, pkgItem.Version, request, pkgItem.PackageSource, fileLocation);
            }
            else
            {
                // wait for the result from installpackage
                packageToBeInstalled = NuGetClient.InstallPackage(pkgItem.Id, pkgItem.Version, request, pkgItem.PackageSource, pkgItem.PackageSource.Repository.Source, pkgItem.Package.PackageHash, pkgItem.Package.PackageHashAlgorithm);
            }

            // Package is installed successfully
            if (packageToBeInstalled != null)
            {
                // if this is a http repository, return metadata from online
                if (!pkgItem.PackageSource.Repository.IsFile)
                {
                    request.YieldPackage(pkgItem, packageToBeInstalled.PackageSource.Name);
                }
                else
                {
                    request.YieldPackage(packageToBeInstalled, packageToBeInstalled.PackageSource.Name);
                }

                request.Debug(Messages.DebugInfoReturnCall, "NuGetClient", "InstallSinglePackage");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Install a single package. Also install any of its dependency if they are available (the dependency will be installed first).
        /// For dependencies, we will only get those that are not installed.
        /// </summary>
        /// <param name="pkgItem"></param>
        /// <param name="request"></param>
        /// <returns></returns>
        internal static bool InstallPackageHelper(PackageItem pkgItem, NuGetRequest request)
        {
            // pkgItem.Sources is the source that the user input. The request will try this source.
            request.OriginalSources = pkgItem.Sources;

            int progressId = 0;

            bool hasDependencyLoop = false;
            // Get the dependencies that are not already installed
            var dependencies = NuGetClient.GetPackageDependenciesToInstall(request, pkgItem, ref hasDependencyLoop);
             
            // If there is a dependency loop. Warn the user and don't install the package
            if (hasDependencyLoop)
            {
                // package itself didn't install. Report error
                request.WriteError(ErrorCategory.DeadlockDetected, pkgItem.Id, Constants.Messages.DependencyLoopDetected, pkgItem.Id);
                return false;
            }
       
            int n = 0;
            int numberOfDependencies = dependencies.Count();

            // Start progress
            progressId = request.StartProgress(0, string.Format(CultureInfo.InvariantCulture, Messages.InstallingPackage, pkgItem.Id));

            try
            {
                // check that this package has dependency and the user didn't want to skip dependencies
                if (numberOfDependencies > 0 && !request.GetOptionValue("skipdependencies").IsTrue())
                {
                    // let's install dependencies
                    foreach (var dep in dependencies)
                    {
                        request.Progress(progressId, (n * 100 / (numberOfDependencies + 1)) + 1, string.Format(CultureInfo.InvariantCulture, Messages.InstallingDependencyPackage, dep.Id));
                        // Check that we successfully installed the dependency
                        if (!InstallSinglePackage(dep, request))
                        {
                            request.WriteError(ErrorCategory.InvalidResult, dep.Id, Constants.Messages.DependentPackageFailedInstall, dep.Id);
                            return false;
                        }
                        n++;
                        request.Progress(progressId, (n * 100 / (numberOfDependencies + 1)), string.Format(CultureInfo.InvariantCulture, Messages.InstalledDependencyPackage, dep.Id));
                    }
                }

                // Now let's install the main package
                if (InstallSinglePackage(pkgItem, request))
                {
                    return true;
                }
            }
            finally
            {
                // Report that we have completed installing the package and its dependency this does not mean there are no errors.
                // Just that it's completed.
                request.CompleteProgress(progressId, false);
            }
            
            // package itself didn't install. Report error
            request.WriteError(ErrorCategory.InvalidResult, pkgItem.Id, Constants.Messages.PackageFailedInstall, pkgItem.Id);

            return false;
        }

        /// <summary>
        /// Get the package dependencies that we need to installed. hasDependencyLoop is set to true if dependencyloop is detected.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="packageItem"></param>
        /// <param name="hasDependencyLoop"></param>
        /// <returns></returns>
        internal static IEnumerable<PackageItem> GetPackageDependenciesToInstall(NuGetRequest request, PackageItem packageItem, ref bool hasDependencyLoop)
        {
            request.Debug(Messages.DebugInfoCallMethod, "NuGetClient", "GetPackageDependencies");

            // No dependency
            if (packageItem.Package.DependencySetList == null)
            {
                request.Debug(Messages.DebugInfoReturnCall, "NuGetClient", "GetPackageDependencies");
                return Enumerable.Empty<PackageItem>();
            }

            // Returns list of dependency to be installed in the correct order that we should install them
            List<PackageItem> dependencyToBeInstalled = new List<PackageItem>();

            HashSet<PackageItem> permanentlyMarked = new HashSet<PackageItem>(new PackageItemComparer());
            HashSet<PackageItem> temporarilyMarked = new HashSet<PackageItem>(new PackageItemComparer());

            // checks that there are no dependency loop 
            hasDependencyLoop = !DepthFirstVisit(packageItem, temporarilyMarked, permanentlyMarked, dependencyToBeInstalled, request);

            if (!hasDependencyLoop)
            {
                request.Debug(Messages.DebugInfoReturnCall, "NuGetClient", "GetPackageDependencies");
                // remove the last item of the list because that is the package itself
                dependencyToBeInstalled.RemoveAt(dependencyToBeInstalled.Count - 1);
                return dependencyToBeInstalled;
            }

            // there are dependency loop. 
            request.Debug(Messages.DebugInfoReturnCall, "NuGetClient", "GetPackageDependencies");
            return Enumerable.Empty<PackageItem>();
        }

        /// <summary>
        /// Do a dfs visit. returns false if a cycle is encountered. Add the packageItem to the list at the end of each visit
        /// </summary>
        /// <param name="packageItem"></param>
        /// <param name="dependencyToBeInstalled"></param>
        /// <param name="permanentlyMarked"></param>
        /// <param name="temporarilyMarked"></param>
        /// <param name="request"></param>
        /// <returns></returns>
        internal static bool DepthFirstVisit(PackageItem packageItem, HashSet<PackageItem> temporarilyMarked, HashSet<PackageItem> permanentlyMarked, List<PackageItem> dependencyToBeInstalled, NuGetRequest request)
        {
            // dependency loop detected because the element is temporarily marked
            if (temporarilyMarked.Contains(packageItem))
            {
                return false;
            }

            // this is permanently marked. So we don't have to visit it.
            // This is to resolve a case where we have: A->B->C and A->C. Then we need this when we visit C again from either B or A.
            if (permanentlyMarked.Contains(packageItem))
            {
                return true;
            }

            // Mark this node temporarily so we can detect cycle.
            temporarilyMarked.Add(packageItem);

            // Visit the dependency
            foreach (var dependency in GetPackageDependenciesHelper(packageItem, request))
            {
                if (!DepthFirstVisit(dependency, temporarilyMarked, permanentlyMarked, dependencyToBeInstalled, request))
                {
                    // if dfs returns false then we have encountered a loop
                    return false;
                }
                // otherwise visit the next dependency
            }

            // Add the package to the list so we can install later
            dependencyToBeInstalled.Add(packageItem);

            // Done with this node so mark it permanently
            permanentlyMarked.Add(packageItem);

            // Unmark it temporarily
            temporarilyMarked.Remove(packageItem);

            return true;
        }

        /// <summary>
        /// Returns the package dependencies of packageItem. We only return the dependencies that are not installed in the destination folder of request
        /// </summary>
        /// <param name="packageItem"></param>
        /// <param name="request"></param>
        private static IEnumerable<PackageItem> GetPackageDependenciesHelper(PackageItem packageItem, NuGetRequest request)
        {
            if (packageItem.Package.DependencySetList == null)
            {
                yield break;
            }

            foreach (var depSet in packageItem.Package.DependencySetList)
            {
                if (depSet.Dependencies == null)
                {
                    continue;
                }

                foreach (var dep in depSet.Dependencies)
                {
                    // Get the min dependencies version
                    string minVersion = dep.DependencyVersion.MinVersion.ToStringSafe();

                    // Get the max dependencies version
                    string maxVersion = dep.DependencyVersion.MaxVersion.ToStringSafe();

                    // check whether it is already installed at the destination
                    if (request.GetInstalledPackages(dep.Id, null, minVersion, maxVersion, minInclusive: dep.DependencyVersion.IsMinInclusive, maxInclusive: dep.DependencyVersion.IsMaxInclusive, terminateFirstFound: true))
                    {
                        request.Verbose(String.Format(CultureInfo.CurrentCulture, Messages.AlreadyInstalled, dep.Id));
                        // already have a dependency so move on
                        continue;
                    }

                    // get all the packages that match this dependency
                    var dependentPackageItem = request.GetPackageById(dep.Id, request, minimumVersion: minVersion, maximumVersion: maxVersion, minInclusive: dep.DependencyVersion.IsMinInclusive, maxInclusive: dep.DependencyVersion.IsMaxInclusive).ToArray();

                    if (dependentPackageItem.Length == 0)
                    {
                        request.WriteError(ErrorCategory.ObjectNotFound, dep.Id, Constants.Messages.DependencyResolutionError);

                        break;
                    }

                    // Get the first listed version of the dependencies if possible (islatestversion is false for unlisted)
                    var latestItem = dependentPackageItem.FirstOrDefault(item => item.Package != null && item.Package.IsLatestVersion);

                    // If not possible, just get the first item
                    if (latestItem == null)
                    {
                        latestItem = dependentPackageItem[0];
                    }

                    // Return the first package that needs to be installed. 
                    yield return latestItem;
                }
            }
        }

        /// <summary>
        /// Download a package from a file repository that matches the given version and name and install it on the local system.
        /// </summary>
        /// <param name="packageName">Package name</param>
        /// <param name="version">Package version</param>
        /// <param name="request">An object passed in from the PackageManagement platform that contains APIs that can be used to interact with it </param>  
        /// <param name="source">Package source</param>
        /// <param name="sourceFilePath">File source path pointing to the package to be installed</param>
        /// <returns>PackageItem object</returns>
        internal static PackageItem InstallPackageLocal(
            string packageName, 
            string version,
            NuGetRequest request, 
            PackageSource source,             
            string sourceFilePath 
            )
        {
            request.Debug(Messages.DebugInfoCallMethod, "NuGetClient", "InstallPackageLocal");

            string tempSourceFilePath = null;
            string tempSourceDirectory = null;      

            try 
            {
                string destinationFilePath = request.Destination;
                request.Verbose(string.Format(CultureInfo.InvariantCulture, "InstallPackageLocal' - name='{0}', version='{1}',destination='{2}'", packageName, version, destinationFilePath));
                request.Debug(sourceFilePath);

                if (string.IsNullOrWhiteSpace(sourceFilePath))
                {
                    throw new ArgumentNullException(sourceFilePath);
                }

                if (!File.Exists(sourceFilePath)) {
                    throw new FileNotFoundException(sourceFilePath);
                }

                //Create the destination directory if it does not exist
                if (!Directory.Exists(destinationFilePath)) {
                    Directory.CreateDirectory(destinationFilePath);
                }

                //Make a temp folder in the user appdata temp directory 
                tempSourceFilePath = FileUtility.GetTempFileFullPath(fileExtension: NuGetConstant.PackageExtension);

                //Copy over the source file from  the folder repository to the temp folder
                File.Copy(sourceFilePath, tempSourceFilePath, true);

                //Unzip it
                tempSourceDirectory = PackageUtility.DecompressFile(tempSourceFilePath);

                //Get a packge directory under the destination path to store the package
                string installedFolder = FileUtility.MakePackageDirectoryName(request.ExcludeVersion.Value, destinationFilePath, packageName, version);

                //File folder format of the NuGet packages looks like the following after installed:
                //Jquery.2.0.1
                //  - JQuery.2.0.1.nupkg
                //  - contents and other stuff

                //Copy the unzipped files to under the package installed folder
                FileUtility.CopyDirectory(tempSourceDirectory, installedFolder, true);

                 //Read the package manifest and return the package object
                string nuspec = Path.Combine(installedFolder, packageName) + NuGetConstant.ManifestExtension;

                PackageBase package = PackageUtility.ProcessNuspec(nuspec);

                var pkgItem = new PackageItem {
                    Package = package,
                    PackageSource = source,
                    FastPath = request.MakeFastPath(source, package.Id, package.Version),
                    FullPath = installedFolder
                };

                // Delete the nuspec file
                //Get a package file path
                var nuspecFilePath = Path.Combine(installedFolder, packageName + NuGetConstant.ManifestExtension);

                if (File.Exists(nuspecFilePath))
                {
                    FileUtility.DeleteFile(nuspecFilePath, false);
                }

                request.Debug(Messages.DebugInfoReturnCall, "NuGetClient", "InstallPackageLocal");

                return pkgItem;

            } catch (Exception ex) {
                request.Warning(Constants.Messages.PackageFailedInstall, ex.Message);
                ex.Dump(request);
                throw;
            } finally {

                FileUtility.DeleteFile(tempSourceFilePath, isThrow:false);
                FileUtility.DeleteDirectory(tempSourceDirectory, recursive: true, isThrow: false);
            }
        }

        /// <summary>
        /// Perform package uninstallation.
        /// </summary>
        /// <param name="request">Object given by the PackageManagement platform</param>
        /// <param name="pkg">PackageItem object</param>
        internal static void UninstallPackage(NuGetRequest request, PackageItem pkg)
        {
            request.Debug(Messages.DebugInfoCallMethod, "NuGetClient", "UninstallPackage");

            if (pkg == null)
            {
                throw new ArgumentNullException(paramName: "pkg");
            }

            var dir = pkg.InstalledDirectory;

            if (String.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                return;
            }

            FileUtility.DeleteDirectory(pkg.InstalledDirectory, recursive:true, isThrow:false);

            //Inform a user which package is deleted via the packageManagement platform
            request.Verbose(Messages.UninstalledPackage, "NuGetClient", pkg.Id);

            request.YieldPackage(pkg, pkg.Id);
        }

        /// <summary>
        /// Download a NuGet package.
        /// </summary>
        /// <param name="packageName">Package name</param>
        /// <param name="version">Package version</param>
        /// <param name="destination">Destination location to store the downloaded package</param>
        /// <param name="queryUrl">Uri to query the package</param>
        /// <param name="request">An object passed in from the PackageManagement platform that contains APIs that can be used to interact with it </param>   
        internal static void DownloadPackage(string packageName, string version, string destination, string queryUrl, NuGetRequest request) 
        {
            try {
                request.Verbose(string.Format(CultureInfo.InvariantCulture, "DownloadPackage' - name='{0}', version='{1}',destination='{2}', uri='{3}'", packageName, version, destination, queryUrl));

                if (new Uri(queryUrl).IsFile) {
                    throw new ArgumentException(Constants.Messages.UriSchemeNotSupported, queryUrl);
                }

                //V2 download package protocol:
                //sample url: http://www.NuGet.org/api/v2/package/jQuery/2.1.3

                string append = String.Format(CultureInfo.InvariantCulture, "/package/{0}/{1}", packageName, version);
                string httpquery = PathUtility.UriCombine(queryUrl, append);

                long result = 0;

                // Do not need to validate here again because the job is done by the httprepository that supplies the queryurl
                //Downloading the package
                //request.Verbose(httpquery);
                result = DownloadDataToFileAsync(destination, httpquery, request).Result;                   

                if (result == 0 || !File.Exists(destination))
                {
                    request.Verbose(Messages.FailedDownloadPackage, packageName, httpquery);
                    request.Warning(Constants.Messages.SourceLocationNotValid, queryUrl);
                } else {
                    request.Verbose(Messages.CompletedDownload, packageName);
                }
            } catch (Exception ex) {
                request.Warning(Constants.Messages.PackageFailedInstall, ex.Message);
                ex.Dump(request);
                throw;
            }
        }

        /// <summary>
        /// Returns a httpclient with the headers set.
        /// </summary>
        /// <returns></returns>
        private static HttpClient GetHttpClient(Request request) {
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/atom+xml,application/xml");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Charset", "UTF-8");
            // Request for gzip and deflate encoding to make the response lighter.
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip,deflate");
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "NuGet");

            if (request is NuGetRequest)
            {
                var NuGetRequest = request as NuGetRequest;
                if (NuGetRequest != null && NuGetRequest.Headers != null)
                {
                    foreach (var header in NuGetRequest.Headers.Value)
                    {
                        // header is in the format "A=B" because OneGet doesn't support Dictionary parameters
                        if (!String.IsNullOrEmpty(header))
                        {
                            var headerSplit = header.Split(new string[] { "=" }, 2, StringSplitOptions.RemoveEmptyEntries);

                            // ignore wrong entries
                            if (headerSplit.Count() == 2)
                            {
                                client.DefaultRequestHeaders.TryAddWithoutValidation(headerSplit[0], headerSplit[1]);
                            }
                            else
                            {
                                request.Warning(Messages.HeaderIgnored, header);
                            }
                        }
                    }
                }
            }

            return client;
        }

        /// <summary>
        /// Returns the appropriate stream depending on the encoding
        /// </summary>
        /// <param name="response"></param>
        /// <returns></returns>
        private static Stream GetStreamBasedOnEncoding(HttpResponseMessage response)
        {
            Stream result = response.Content.ReadAsStreamAsync().Result;
            // Gzip encoding so returns gzip stream
            if (response.Content.Headers.ContentEncoding.Contains("gzip"))
            {
                return new GZipStream(result, CompressionMode.Decompress);
            }
            // Deflate encoding so returns deflate stream
            else if (response.Content.Headers.ContentEncoding.Contains("deflate"))
            {
                return new DeflateStream(result, CompressionMode.Decompress);
            }

            return result;
        }

        /// <summary>
        /// Download data from remote via uri query.
        /// </summary>
        /// <param name="query">Uri query</param>
        /// <param name="request">An object passed in from the PackageManagement platform that contains APIs that can be used to interact with it </param>   
        /// <returns></returns>
        internal static Stream DownloadDataToStream(string query, Request request)
        {
            request.Verbose(Messages.DownloadingPackage, query);

            var client = GetHttpClient(request);

            var response = PathUtility.GetHttpResponse(client, query);

            // Check that response was successful or throw exception
            if (response == null || !response.IsSuccessStatusCode)
            {
                request.WriteError(ErrorCategory.OperationStopped, query, Constants.Messages.CouldNotGetResponseFromQuery, query);
                return null;
            }

            // Read response and write out a stream
            var stream = GetStreamBasedOnEncoding(response);

            request.Verbose(Messages.CompletedDownload, query);

            return stream;
        }

        /// <summary>
        /// Send an initial request to download data from the server.
        /// From the initial request, we may change the host of subsequent calls (if a redirection happens in this initial request)
        /// Also, if the initial request sends us less data than the amount we request, then we do not
        /// need to issue more requests
        /// </summary>
        /// <param name="query"></param>
        /// <param name="startPoint"></param>
        /// <param name="bufferSize"></param>
        /// <param name="request"></param>
        /// <returns></returns>
        internal static Stream InitialDownloadDataToStream(UriBuilder query, int startPoint, int bufferSize, Request request)
        {
            var uri = String.Format(CultureInfo.CurrentCulture, query.Uri.ToString(), startPoint, bufferSize);
            request.Verbose(Messages.DownloadingPackage, uri);

            var client = GetHttpClient(request);

            var response = PathUtility.GetHttpResponse(client, uri);

            // Check that response was successful or write error
            if (response == null || !response.IsSuccessStatusCode)
            {
                request.WriteError(ErrorCategory.OperationStopped, uri, Constants.Messages.CouldNotGetResponseFromQuery, uri);
                return null;
            }

            // Read response and write out a stream
            var stream = GetStreamBasedOnEncoding(response);

            request.Verbose(Messages.CompletedDownload, uri);

            // If the host from the response is different, change the host of the original query
            if (!String.Equals(response.RequestMessage.RequestUri.Host, query.Host, StringComparison.OrdinalIgnoreCase))
            {
                query.Host = response.RequestMessage.RequestUri.Host;
            }

            return stream;
        }

        /// <summary>
        /// Download data from remote via uri query.
        /// </summary>
        /// <param name="fileName">A file to store the downloaded data.</param>
        /// <param name="query">Uri query</param>
        /// <param name="request">An object passed in from the PackageManagement platform that contains APIs that can be used to interact with it </param>   
        /// <returns></returns>
        internal static async Task<long> DownloadDataToFileAsync(string fileName, string query, NuGetRequest request)
        {
            request.Verbose(Messages.DownloadingPackage, query);

            var client = GetHttpClient(request);

            var response = PathUtility.GetHttpResponse(client, query);

            // Check that response was successful or write error
            if (response == null || !response.IsSuccessStatusCode)
            {
                request.WriteError(ErrorCategory.OperationStopped, query, Constants.Messages.CouldNotGetResponseFromQuery, query);
                return 0;
            }

            // Check that response was successful or throw exception
            response.EnsureSuccessStatusCode();

            // Read response asynchronously and write out a file
            // The return value is for the caller to wait for the async operation to complete.
            var fileLength = await response.Content.ReadAsFileAsync(fileName);

            request.Verbose(Messages.CompletedDownload, query);

            return fileLength;
        }
    }
}
