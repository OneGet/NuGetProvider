using Microsoft.PackageManagement.Internal.Utility.Platform;

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
    using System.IO.Compression;
    using System.Net;
    using System.Security.Cryptography;
    using System.Threading;
    using Microsoft.PackageManagement.Provider.Utility;
    using Microsoft.PackageManagement.NuGetProvider.Utility;
    using Win32;

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
        internal static IEnumerable<PackageBase> FindPackage(string query, NuGetRequest request) {
            request.Debug(Messages.DebugInfoCallMethod, "NuGetClient", "FindPackage");

            request.Verbose(Messages.SearchingRepository, query, "");

            return NuGetWebUtility.SendRequest(query, request);
        }

        internal static IEnumerable<PackageBase> FindPackage(string query, RequestWrapper request)
        {
            request.Debug(Messages.DebugInfoCallMethod, "NuGetClient", "FindPackage");

            request.Verbose(Messages.SearchingRepository, query, "");

            return NuGetWebUtility.SendRequest(query, request);
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
        /// <param name="progressTracker">progress tracker to help keep track of progressid, start and end of the progress</param>
        /// <returns>PackageItem object</returns>
        internal static PackageItem InstallPackage(
            string packageName,
            string version,
            NuGetRequest request,
            PackageSource source,
            string queryUrl,
            string packageHash,
            string packageHashAlgorithm,
            ProgressTracker progressTracker
            ) 
        {
            request.Debug(Messages.DebugInfoCallMethod, "NuGetClient", "InstallPackage");

            //If the destination folder does not exists, create it
            string destinationPath = request.Destination;
            request.Verbose(string.Format(CultureInfo.InvariantCulture, "InstallPackage' - name='{0}', version='{1}',destination='{2}'", packageName, version, destinationPath));

            string directoryToDeleteWhenFailed = string.Empty;
            bool needToDelete = false;
            string installFullPath = string.Empty;

            try
            {
                if (!Directory.Exists(destinationPath)) {
                    request.CreateDirectoryInternal(destinationPath);
                    // delete the destinationPath later on if we fail to install and if destinationPath did not exist before
                    directoryToDeleteWhenFailed = destinationPath;
                }

                //Create a folder under the destination path to hold the package
                string installDir = FileUtility.MakePackageDirectoryName(request.ExcludeVersion.Value, destinationPath, packageName, version);

                if (!Directory.Exists(installDir)) {
                    request.CreateDirectoryInternal(installDir);

                    // if directoryToDeleteWhenFailed is null then the destinationPath already exists before so we should not delete it
                    if (String.IsNullOrWhiteSpace(directoryToDeleteWhenFailed))
                    {
                        directoryToDeleteWhenFailed = installDir;
                    }
                }

                //Get the package file name based on the version and id
                string fileName = FileUtility.MakePackageFileName(request.ExcludeVersion.Value, packageName, version, NuGetConstant.PackageExtension);

                installFullPath = Path.Combine(installDir, fileName);

                // we assume downloading takes 70% of the progress
                int endProgressDownloading = progressTracker.ConvertPercentToProgress(0.7);

                //download to fetch the package
                DownloadPackage(packageName, version, installFullPath, queryUrl, request, source, new ProgressTracker(progressTracker.ProgressID, progressTracker.StartPercent, endProgressDownloading));

                // check that we have the file
                if (!File.Exists(installFullPath))
                {
                    needToDelete = true;
                    // error message is package failed to be downloaded
                    request.WriteError(ErrorCategory.ResourceUnavailable, installFullPath, Constants.Messages.PackageFailedInstallOrDownload, packageName,
                        CultureInfo.CurrentCulture.TextInfo.ToLower(Constants.Download));
                    return null;
                }

                #region verify hash
                //we don't enable checking for hash here because it seems like nuget provider does not
                //checks that there is hash. Otherwise we don't carry out the install
                
                if (string.IsNullOrWhiteSpace(packageHash))
                {
                    // if no hash (for example, vsts feed, install the package but log verbose message)
                    request.Verbose(string.Format(CultureInfo.CurrentCulture, Resources.Messages.HashNotFound, packageName));
                    //parse the package
                    var pkgItem = InstallPackageLocal(packageName, version, request, source, installFullPath, new ProgressTracker(progressTracker.ProgressID, endProgressDownloading, progressTracker.EndPercent));
                    return pkgItem;
                }

                // Verify the hash
                using (FileStream stream = File.OpenRead(installFullPath))
                {
                    HashAlgorithm hashAlgorithm = null;

                    switch (packageHashAlgorithm == null ? string.Empty : packageHashAlgorithm.ToLowerInvariant())
                    {
                        case "sha256":
#if !CORECLR                          
                            hashAlgorithm = OSInformation.IsFipsEnabled ? (HashAlgorithm)new SHA256CryptoServiceProvider() : SHA256.Create();
#else        
                            hashAlgorithm = SHA256.Create();          
#endif
                            break;

                        case "md5":

                            if (OSInformation.IsFipsEnabled)
                            {
                                //error out as M5 hash algorithms is not supported 
                                request.WriteError(ErrorCategory.InvalidOperation, "hashAlgorithm", Resources.Messages.HashAlgorithmNotSupported, NuGetConstant.ProviderName, packageHashAlgorithm);
                                break;
                            }
                            else
                            {
                                hashAlgorithm = MD5.Create();
                            }

                            break;

                        case "sha512":
                        // Flows to default case

                        // default to sha512 algorithm
                        default:
#if !CORECLR
                            hashAlgorithm = OSInformation.IsFipsEnabled ? (HashAlgorithm)new SHA512CryptoServiceProvider() : SHA256.Create();
#else
                            hashAlgorithm = SHA512.Create();
#endif
                            break;
                    }

                    if (hashAlgorithm == null)
                    {
                        // delete the file downloaded. VIRUS!!!
                        needToDelete = true;
                        request.WriteError(ErrorCategory.SecurityError, packageHashAlgorithm, Constants.Messages.HashNotSupported, packageHashAlgorithm);
                        return null;
                    }

                    // compute the hash
                    byte[] computedHash = hashAlgorithm.ComputeHash(stream);

                    // convert the original hash we got from the feed
                    byte[] downloadedHash = Convert.FromBase64String(packageHash);

                    // if they are not equal, just issue out verbose because there is a current bug in backend
                    // where editing the published module will result in a package with a different hash than the one
                    // provided on the feed
                    if (!Enumerable.SequenceEqual(computedHash, downloadedHash))
                    {
                        // delete the file downloaded. VIRUS!!!
                        request.Verbose(Constants.Messages.HashNotMatch, packageName);
                    }

                    //parse the package
                    var pkgItem = InstallPackageLocal(packageName, version, request, source, installFullPath, new ProgressTracker(progressTracker.ProgressID, endProgressDownloading, progressTracker.EndPercent));
                    return pkgItem;
                }

                #endregion

            }
            catch (Exception ex)
            {
                // the error will be package "packageName" failed to install because : "reason"
                ex.Dump(request);
                request.WriteError(ErrorCategory.InvalidResult, packageName, Resources.Messages.PackageFailedToInstallReason, packageName, ex.Message);
                needToDelete = true;
            }
            finally
            {
                // remove nupkg (installFullPath)
                if (request.IsCalledFromPowerShellGet && File.Exists(installFullPath))
                {
                    FileUtility.DeleteFile(installFullPath, isThrow: false);
                }

                if (needToDelete)
                {
                    // if the directory exists just delete it because it will contains the file as well
                    if (!String.IsNullOrWhiteSpace(directoryToDeleteWhenFailed) && Directory.Exists(directoryToDeleteWhenFailed))
                    {
                        try
                        {
                            FileUtility.DeleteDirectory(directoryToDeleteWhenFailed, true, isThrow: false);
                        }
                        catch { }
                    }

                    // if for some reason, we can't delete the directory or if we don't need to delete the directory
                    // then we have to delete installFullPath
                    if (File.Exists(installFullPath))
                    {
                        FileUtility.DeleteFile(installFullPath, isThrow: false);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Install a single package without checking for dependencies
        /// </summary>
        /// <param name="pkgItem"></param>
        /// <param name="request"></param>
        /// <param name="progressTracker"></param>
        /// <returns></returns>
        internal static bool InstallSinglePackage(PackageItem pkgItem, NuGetRequest request, ProgressTracker progressTracker)
        {
            PackageItem packageToBeInstalled;

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
                    RequiredVersion = new SemanticVersion(pkgItem.Version)
                }, request).FullFilePath;
                packageToBeInstalled = NuGetClient.InstallPackageLocal(pkgItem.Id, pkgItem.Version, request, pkgItem.PackageSource, fileLocation, progressTracker);
            }
            else
            {             
                //V2 download package protocol:
                //sample url: http://www.nuget.org/api/v2/package/jQuery/2.1.3
                string append = String.Format(CultureInfo.InvariantCulture, "/package/{0}/{1}", pkgItem.Id, pkgItem.Version);
                string httpquery = PathUtility.UriCombine(pkgItem.PackageSource.Repository.Source, append);

                // wait for the result from installpackage
                packageToBeInstalled = NuGetClient.InstallPackage(pkgItem.Id, pkgItem.Version, request, pkgItem.PackageSource,
                    string.IsNullOrWhiteSpace(pkgItem.Package.ContentSrcUrl) ? httpquery : pkgItem.Package.ContentSrcUrl,
                    pkgItem.Package.PackageHash, pkgItem.Package.PackageHashAlgorithm, progressTracker);
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

                request.Debug(Messages.DebugInfoReturnCall, "NuGetClient", "InstallSinglePackage");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Download a single package to destination without checking for dependencies
        /// </summary>
        /// <param name="pkgItem"></param>
        /// <param name="progressTracker"></param>
        /// <param name="request"></param>
        /// <param name="destLocation"></param>
        /// <returns></returns>
        internal static bool DownloadSinglePackage(PackageItem pkgItem, NuGetRequest request, string destLocation, ProgressTracker progressTracker)
        {
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
                    //V2 download package protocol:
                    //sample url: http://www.nuget.org/api/v2/package/jQuery/2.1.3
                    string append = String.Format(CultureInfo.InvariantCulture, "/package/{0}/{1}", pkgItem.Id, pkgItem.Version);
                    string httpquery = PathUtility.UriCombine(pkgItem.PackageSource.Repository.Source, append);

                    downloadSuccessful = NuGetClient.DownloadPackage(pkgItem.Id, pkgItem.Version, destLocation,
                        string.IsNullOrWhiteSpace(pkgItem.Package.ContentSrcUrl) ? httpquery : pkgItem.Package.ContentSrcUrl, request, pkgItem.PackageSource, progressTracker);
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
                return true;
            }

            return false;
        }

        /// <summary>
        /// Install a single package. Also install any of its dependency if they are available (the dependency will be installed first).
        /// For dependencies, we will only get those that are not installed.
        /// Operation is either install or download
        /// installOrDownloadFunction is a function that takes in a packageitem and performs either install or download on it
        /// </summary>
        /// <param name="pkgItem"></param>
        /// <param name="request"></param>
        /// <param name="operation"></param>
        /// <param name="installOrDownloadFunction"></param>
        /// <returns></returns>
        internal static bool InstallOrDownloadPackageHelper(PackageItem pkgItem, NuGetRequest request, string operation,
            Func<PackageItem, ProgressTracker, bool> installOrDownloadFunction)
        {
            // pkgItem.Sources is the source that the user input. The request will try this source.
            request.OriginalSources = pkgItem.Sources;

            bool hasDependencyLoop = false;

            int numberOfDependencies = 0;
            IEnumerable<PackageItem> dependencies = new List<PackageItem>();
            
            // skip installing dependencies
            if (!request.SkipDependencies.Value)
            {
                // Get the dependencies that are not already installed
                dependencies = NuGetClient.GetPackageDependenciesToInstall(request, pkgItem, ref hasDependencyLoop).ToArray();

                // If there is a dependency loop. Warn the user and don't install the package
                if (hasDependencyLoop)
                {
                    // package itself didn't install. Report error
                    request.WriteError(ErrorCategory.DeadlockDetected, pkgItem.Id, Constants.Messages.DependencyLoopDetected, pkgItem.Id);
                    return false;
                }

                // request may get canceled if there is a package dependencies missing
                if (request.IsCanceled)
                {
                    return false;
                }

                numberOfDependencies = dependencies.Count();
            }

            int n = 0;

            // Start progress
            ProgressTracker progressTracker = ProgressTracker.StartProgress(null, string.Format(CultureInfo.InvariantCulture, Messages.InstallingOrDownloadingPackage, operation, pkgItem.Id), request);

            try
            {
                // check that this package has dependency and the user didn't want to skip dependencies
                if (numberOfDependencies > 0)
                {
                    // let's install dependencies
                    foreach (var dep in dependencies)
                    {
                        request.Progress(progressTracker.ProgressID, (n * 100 / (numberOfDependencies + 1)), string.Format(CultureInfo.InvariantCulture, Messages.InstallingOrDownloadingDependencyPackage, operation, dep.Id));

                        // start a subprogress bar for the dependent package
                        ProgressTracker subProgressTracker = ProgressTracker.StartProgress(progressTracker, string.Format(CultureInfo.InvariantCulture, Messages.InstallingOrDownloadingPackage, operation, dep.Id), request);
                        try
                        {
                            // Check that we successfully installed the dependency
                            if (!installOrDownloadFunction(dep, subProgressTracker))
                            {
                                request.WriteError(ErrorCategory.InvalidResult, dep.Id, Constants.Messages.DependentPackageFailedInstallOrDownload, dep.Id, CultureInfo.CurrentCulture.TextInfo.ToLower(operation));
                                return false;
                            }
                        }
                        finally
                        {
                            request.CompleteProgress(subProgressTracker.ProgressID, true);
                        }

                        n++;
                        request.Progress(progressTracker.ProgressID, (n * 100 / (numberOfDependencies + 1)), string.Format(CultureInfo.InvariantCulture, Messages.InstalledOrDownloadedDependencyPackage, operation, dep.Id));
                    }
                }

                // Now let's install the main package
                // the start progress should be where we finished installing the dependencies
                if (installOrDownloadFunction(pkgItem, new ProgressTracker(progressTracker.ProgressID, (n * 100 / (numberOfDependencies + 1)), 100)))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                ex.Dump(request);
            }
            finally
            {
                // Report that we have completed installing the package and its dependency this does not mean there are no errors.
                // Just that it's completed.
                request.CompleteProgress(progressTracker.ProgressID, true);
            }
            
            // package itself didn't install. Report error
            request.WriteError(ErrorCategory.InvalidResult, pkgItem.Id, Constants.Messages.PackageFailedInstallOrDownload, pkgItem.Id, CultureInfo.CurrentCulture.TextInfo.ToLower(operation));

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
            if (packageItem.Package == null || packageItem.Package.DependencySetList == null)
            {
                request.Debug(Messages.DebugInfoReturnCall, "NuGetClient", "GetPackageDependencies");
                return Enumerable.Empty<PackageItem>();
            }

            // Returns list of dependency to be installed in the correct order that we should install them
            List<Tuple<PackageItem, DependencyVersion>> dependencyToBeInstalled = new List<Tuple<PackageItem, DependencyVersion>>();

            HashSet<PackageItem> permanentlyMarked = new HashSet<PackageItem>(new PackageItemComparer());
            HashSet<PackageItem> temporarilyMarked = new HashSet<PackageItem>(new PackageItemComparer());

            /*
            Logic for dependency resolution:

            1.Do the normal dependency resolution with a call to DepthFirstVisit (if you’re interested, I used the DFS method in https://en.wikipedia.org/wiki/Topological_sorting)

            2.Collect a list of tuple where the first item is a dependency and the second item is a dependency constraint that this dependency resolved. The dependency constraint will be represented by a version range. For example, < AzureRM.Profile version 1.0.11, [1.0.11]> means the dependency returned is AzureRM.Profile version 1.0.11 and the constraint that it satisfies is its version must be <= 1.0.11 and >= 1.0.11 (I’m using NuGet versioning scheme https://docs.nuget.org/create/versioning)

            3.	Now we will make a dictionary by grouping all the tuple according to the first item’s name, i.e.the dependency’s name.So in the example above, we will have a key called AzureRM.Profile and the value as a list with two values: [1.0.11] and[1.0.11,)

            4.	Now for each key in the dictionary that has more than 1 items in its corresponding value(which means there are more than 1 constraint for this package), we will try to reduce the constraint by:
                a.Sort the list of dependency constraint(version range) by the left value of the version range.For example, if we have [1.0], (0.8, 2.0], (,3.0), [0.9,3.0) then the sorted order is (,3.0), (0.8, 2.0], [0.9, 3.0), [1.0]
                b.Now we iterate through this list of version range and try to find all the intersections.For example, in the example above, the intersection is just[1.0] since this interval intersects all 4 of the version range.

            5.	For each dependency, we will now have a list of reduced constraints (version range). For each of the reduced constraint:
                a.	We check whether we have a version of the dependency that satisfies this version range, if so, then we will add use this version. At the end of this process, we will have a smaller list of versions for the dependency (hopefully just 1).
                b.	If for any reduced constraint, we cannot find a version of the dependency to satisfy it, then we will simply discard the reduced constraints list for this dependency and use what we original get from step 1 instead.

            6.Now we can repeat step 1 again since for each dependency constraint, we will know which version of the dependency to use to satisfy it.
            */

            // checks that there are no dependency loop 
            hasDependencyLoop = !DepthFirstVisit(new Tuple<PackageItem, DependencyVersion>(packageItem, null), temporarilyMarked, permanentlyMarked, dependencyToBeInstalled, new HashSet<string>(), request);

            if (!hasDependencyLoop)
            {
                // this list contains packages that has the same id but different versions
                Dictionary < string, List<DependencyVersion>> duplicatedPackages = new Dictionary<string, List<DependencyVersion>>(StringComparer.OrdinalIgnoreCase);

                // this list will contain the result of duplicated packages after we have tried to reduce the constraints
                Dictionary<string, HashSet<PackageItem>> reducedConstraintDuplicatedPackages = new Dictionary<string, HashSet<PackageItem>>(StringComparer.OrdinalIgnoreCase);

                // populate the list
                foreach (var dep in dependencyToBeInstalled)
                {
                    if (!duplicatedPackages.ContainsKey(dep.Item1.Id))
                    {
                        duplicatedPackages[dep.Item1.Id] = new List<DependencyVersion>();
                        reducedConstraintDuplicatedPackages[dep.Item1.Id] = new HashSet<PackageItem>(new PackageItemComparer());
                    }

                    duplicatedPackages[dep.Item1.Id].Add(dep.Item2);
                    reducedConstraintDuplicatedPackages[dep.Item1.Id].Add(dep.Item1);
                }

                // packages with duplicated ids
                var duplicatedKeys = duplicatedPackages.Keys.Where(key => duplicatedPackages[key].Count > 1).ToList();

                // for each of the duplicated key, we try to reduce the constraint.
                foreach (var duplicatedKey in duplicatedKeys)
                {
                    duplicatedPackages[duplicatedKey] = ReduceConstraints(duplicatedPackages[duplicatedKey]);
                    HashSet<PackageItem> unreducedList = reducedConstraintDuplicatedPackages[duplicatedKey];

                    HashSet<PackageItem> reducedList = new HashSet<PackageItem>();

                    foreach (var reducedConstraint in duplicatedPackages[duplicatedKey])
                    {
                        // look at the reduced constraint and see whether we can satisfy them (get the package with the largest version that can satisfy it)
                        var maxVersion = unreducedList.Where(pkgItem => request.MinAndMaxVersionMatched(new SemanticVersion(pkgItem.Version), reducedConstraint.MinVersion.ToStringSafe(), reducedConstraint.MaxVersion.ToStringSafe(), reducedConstraint.IsMinInclusive, reducedConstraint.IsMaxInclusive))
                            .Aggregate((currentMax, pkgItem) => (currentMax == null || (new SemanticVersion(currentMax.Version) < new SemanticVersion(pkgItem.Version))) ? pkgItem : currentMax);

                        // if we can't satisfy the reduced constraint, just keep the original one
                        // we can do further processing but this will slow down the installation a lot and it's not worth it since this is not a common case
                        if (maxVersion == null)
                        {
                            reducedList = unreducedList;
                            break;
                        }

                        reducedList.Add(maxVersion);
                    }

                    reducedConstraintDuplicatedPackages[duplicatedKey] = reducedList;
                }

                dependencyToBeInstalled = new List<Tuple<PackageItem, DependencyVersion>>();

                permanentlyMarked = new HashSet<PackageItem>(new PackageItemComparer());
                temporarilyMarked = new HashSet<PackageItem>(new PackageItemComparer());

                // now we run the dfs again, this time we don't need to check for the loop but we'll try to use the packages from the reducedlist
                DepthFirstVisit(new Tuple<PackageItem, DependencyVersion>(packageItem, null), temporarilyMarked, permanentlyMarked, dependencyToBeInstalled, new HashSet<string>(), request, reducedConstraintDuplicatedPackages);

                request.Debug(Messages.DebugInfoReturnCall, "NuGetClient", "GetPackageDependencies");
                // remove the last item of the list because that is the package itself
                dependencyToBeInstalled.RemoveAt(dependencyToBeInstalled.Count - 1);
                return dependencyToBeInstalled.Select(pkgTuple => pkgTuple.Item1);
            }

            // there are dependency loop. 
            request.Debug(Messages.DebugInfoReturnCall, "NuGetClient", "GetPackageDependencies");
            return Enumerable.Empty<PackageItem>();
        }

        private static List<DependencyVersion> ReduceConstraints(List<DependencyVersion> constraints)
        {
            if (constraints == null || constraints.Count <= 1)
            {
                return constraints;
            }

            // sort by min version
            constraints.Sort(new DependencyVersionComparerBasedOnMinVersion());

            // now reduce the constraints
            List<DependencyVersion> results = new List<DependencyVersion>();

            // constraint so far
            var constraintSoFar = constraints[0];

            for (int i = 1; i < constraints.Count; i += 1)
            {
                var current = constraints[i];

                // case where the current does not have null min version
                if (current.MinVersion != null)
                {
                    // check for the nonoverlapping case
                    if (constraintSoFar.MaxVersion != null)
                    {
                        // here constraintsofar max version is not null so we can check for overlap
                        if (current.MinVersion > constraintSoFar.MaxVersion)
                        {
                            // no overlap, add constraint so far to results and make the current one the constraint so far
                            results.Add(constraintSoFar);
                            constraintSoFar = current;

                            if (i == constraints.Count - 1)
                            {
                                // if we are already at the end, just return the constraintssofar
                                results.Add(constraintSoFar);
                            }

                            continue;
                        }
                        else if (current.MinVersion == constraintSoFar.MaxVersion)
                        {
                            // if constraintsofar is not maxinclusive, then they do not overlap
                            // if constraintsofar is maxinclusive and current is not mininclusive, then do not overlap too
                            if (!constraintSoFar.IsMaxInclusive || (constraintSoFar.IsMaxInclusive && !current.IsMinInclusive))
                            {
                                results.Add(constraintSoFar);
                                constraintSoFar = current;
                            }
                            else if (current.IsMinInclusive)
                            {
                                // constraint so far is max inclusive here and current is minclusive
                                // the overlap is the maxversion
                                constraintSoFar.MinVersion = current.MinVersion;
                                constraintSoFar.IsMinInclusive = true;
                                constraintSoFar.IsMaxInclusive = true;
                            }

                            if (i == constraints.Count - 1)
                            {
                                // if we are already at the end, just return the constraintssofar
                                results.Add(constraintSoFar);
                            }

                            continue;
                        }

                        // otherwise they must overlap, we will handle these cases below
                    }
                }

                if (constraintSoFar.MinVersion == null)
                {
                    // only need to worry about the case where min version of current is not null because if it is null then we don't need to set that of constraint so far
                    if (current.MinVersion != null)
                    {
                        // the nonverlapping case is already handled so we can just set this without worrying whether
                        // current.minversion is greater than constraintsofar.maxversion
                        constraintSoFar.MinVersion = current.MinVersion;
                        constraintSoFar.IsMinInclusive = current.IsMinInclusive;
                    }                    
                }
                else if (current.MinVersion == constraintSoFar.MinVersion)
                {
                    // here constraintsofar minversion is not null so min version of current cannot be null

                    // if constraint so far is something like [1.0] and current is not min inclusive then we may have to maintain this constraint and create a new constraint (since they do not overlap)
                    if (constraintSoFar.IsMinInclusive && constraintSoFar.MaxVersion == constraintSoFar.MinVersion && constraintSoFar.IsMaxInclusive && !current.IsMinInclusive)
                    {
                        // in this case, constraint so far and the current one do not overlap so create a new one.
                        results.Add(constraintSoFar);
                        constraintSoFar = current;

                        if (i == constraints.Count - 1)
                        {
                            // if we are already at the end, just return the constraintssofar
                            results.Add(constraintSoFar);
                        }

                        continue;
                    }

                    // if current is not min inclusive then the constraint so far has to be not min inclusive 
                    if (!current.IsMinInclusive)
                    {
                        // no need to update minvalue because we already know
                        constraintSoFar.IsMinInclusive = false;
                    }
                }
                else
                {
                    // here both min version of current and constraint so far is not null
                    // we already checked for non overlapping case above so we can assume they will overlap here
                    // ie, current.MinVersion < constraintSoFar.MaxVersion
                  
                    constraintSoFar.MinVersion = current.MinVersion;
                    constraintSoFar.IsMinInclusive = current.IsMinInclusive;                    
                }

                #region setMax
                // now set the max value of constraint so far to whichever is smaller, current or constraintsofar
                if (constraintSoFar.MaxVersion == null)
                {
                    constraintSoFar.MaxVersion = current.MaxVersion;
                    constraintSoFar.IsMaxInclusive = current.IsMaxInclusive;

                    if (i == constraints.Count - 1)
                    {
                        // if we are already at the end, just return the constraintssofar
                        results.Add(constraintSoFar);
                    }

                    continue;
                }

                // if current maxversion is not null then constraintsofar has smaller version (or at least the same
                // or if current max is smaller then this is already contained within
                if (current.MaxVersion == null || current.MaxVersion < constraintSoFar.MaxVersion)
                {
                    if (i == constraints.Count - 1)
                    {
                        // if we are already at the end, just return the constraintssofar
                        results.Add(constraintSoFar);
                    }

                    // just continue since constraintsofar has smaller version
                    continue;
                }

                if (current.MaxVersion == constraintSoFar.MaxVersion)
                {
                    // if 1 of them is not maxinclusive than the overlap cannot have max inclusive
                    constraintSoFar.IsMaxInclusive = (!constraintSoFar.IsMaxInclusive) || (!current.IsMaxInclusive);
                }
                else
                {
                    // current.MaxVersion > constraintSoFar.MaxVersion
                    // set max of constraint so far to current max
                    constraintSoFar.MaxVersion = current.MaxVersion;
                    constraintSoFar.IsMaxInclusive = current.IsMaxInclusive;
                }
                #endregion

                if (i == constraints.Count - 1)
                {
                    // if we are already at the end, just return the constraintssofar
                    results.Add(constraintSoFar);
                }
            }

            return results;
        }

        /// <summary>
        /// Do a dfs visit. returns false if a cycle is encountered. Add the packageItem to the list at the end of each visit
        /// </summary>
        /// <param name="packageItem"></param>
        /// <param name="dependencyToBeInstalled"></param>
        /// <param name="permanentlyMarked"></param>
        /// <param name="temporarilyMarked"></param>
        /// <param name="dependenciesProcessed"></param>
        /// <param name="reducedConstraintDuplicatedPackages"></param>
        /// <param name="request"></param>
        /// <returns></returns>
        internal static bool DepthFirstVisit(Tuple<PackageItem, DependencyVersion> packageItem, HashSet<PackageItem> temporarilyMarked, HashSet<PackageItem> permanentlyMarked, List<Tuple<PackageItem, DependencyVersion>> dependencyToBeInstalled,
            HashSet<string> dependenciesProcessed, NuGetRequest request, Dictionary<string, HashSet<PackageItem>> reducedConstraintDuplicatedPackages=null)
        {
            // dependency loop detected because the element is temporarily marked
            if (temporarilyMarked.Contains(packageItem.Item1))
            {
                return false;
            }

            // this is permanently marked. So we don't have to visit it.
            // This is to resolve a case where we have: A->B->C and A->C. Then we need this when we visit C again from either B or A.
            if (permanentlyMarked.Contains(packageItem.Item1))
            {
                return true;
            }

            // Mark this node temporarily so we can detect cycle.
            temporarilyMarked.Add(packageItem.Item1);

            // Visit the dependency
            foreach (var dependency in GetPackageDependenciesHelper(packageItem.Item1, dependenciesProcessed, request, reducedConstraintDuplicatedPackages))
            {
                if (!DepthFirstVisit(dependency, temporarilyMarked, permanentlyMarked, dependencyToBeInstalled, dependenciesProcessed, request, reducedConstraintDuplicatedPackages))
                {
                    // if dfs returns false then we have encountered a loop
                    return false;
                }
                // otherwise visit the next dependency
            }

            // Add the package to the list so we can install later
            dependencyToBeInstalled.Add(packageItem);

            // Done with this node so mark it permanently
            permanentlyMarked.Add(packageItem.Item1);

            // Unmark it temporarily
            temporarilyMarked.Remove(packageItem.Item1);

            return true;
        }

        /// <summary>
        /// Returns the package dependencies of packageItem. We only return the dependencies that are not installed in the destination folder of request
        /// </summary>
        /// <param name="packageItem"></param>
        /// <param name="depedenciesToProcessed"></param>
        /// <param name="reducedConstraintDuplicatedPackages"></param>
        /// <param name="request"></param>
        private static IEnumerable<Tuple<PackageItem, DependencyVersion>> GetPackageDependenciesHelper(PackageItem packageItem, HashSet<string> depedenciesToProcessed,
            NuGetRequest request, Dictionary<string, HashSet<PackageItem>> reducedConstraintDuplicatedPackages = null)
        {
            if (packageItem.Package.DependencySetList == null)
            {
                yield break;
            }

            bool force = request.GetOptionValue("Force") != null;
            foreach (var depSet in packageItem.Package.DependencySetList)
            {
                if (depSet.Dependencies == null)
                {
                    continue;
                }

                foreach (var dep in depSet.Dependencies)
                {
                    var depKey = string.Format(CultureInfo.InvariantCulture, "{0}!#!{1}", dep.Id, dep.DependencyVersion.ToStringSafe());

                    if (depedenciesToProcessed.Contains(depKey))
                    {
                        continue;
                    }

                    // Get the min dependencies version
                    string minVersion = dep.DependencyVersion.MinVersion.ToStringSafe();

                    // Get the max dependencies version
                    string maxVersion = dep.DependencyVersion.MaxVersion.ToStringSafe();


                    if (reducedConstraintDuplicatedPackages != null && reducedConstraintDuplicatedPackages.ContainsKey(dep.Id))
                    {
                        // this is already processed before
                        depedenciesToProcessed.Add(depKey);

                        HashSet<PackageItem> reducedList = reducedConstraintDuplicatedPackages[dep.Id];

                        if (reducedList.Count == 1)
                        {
                            yield return new Tuple<PackageItem, DependencyVersion>(reducedList.First(), dep.DependencyVersion);
                            continue;
                        }

                        // we already do processing so we can just pick the one that satisfies the constraint
                        yield return new Tuple<PackageItem, DependencyVersion>(reducedList.First(pkgItem => request.MinAndMaxVersionMatched(new SemanticVersion(pkgItem.Version), minVersion, maxVersion, dep.DependencyVersion.IsMinInclusive, dep.DependencyVersion.IsMaxInclusive)), dep.DependencyVersion);
                    }

                    if (!force)
                    {
                        bool installed = false;

                        var installedPackages = request.InstalledPackages.Value;

                        if (request.InstalledPackages.Value.Count() > 0)
                        {
                            // check the installedpackages options passed in
                            foreach (var installedPackage in request.InstalledPackages.Value)
                            {
                                // if name not match, move on to the next entry
                                if (!string.Equals(installedPackage.Id, dep.Id, StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                // if no version and if name matches, skip
                                if (string.IsNullOrWhiteSpace(installedPackage.Version))
                                {
                                    // skip this dependency
                                    installed = true;
                                    break;
                                }

                                SemanticVersion packageVersion = new SemanticVersion(installedPackage.Version);

                                // checks min and max
                                if (request.MinAndMaxVersionMatched(packageVersion, minVersion, maxVersion, dep.DependencyVersion.IsMinInclusive, dep.DependencyVersion.IsMaxInclusive))
                                {
                                    // skip this dependency
                                    installed = true;
                                    break;
                                }
                            }
                        }
                        // check whether package is installed at destination. only used this option if installedpackages not passed in
                        else if (request.GetInstalledPackages(dep.Id, null, minVersion, maxVersion, minInclusive: dep.DependencyVersion.IsMinInclusive, maxInclusive: dep.DependencyVersion.IsMaxInclusive, terminateFirstFound: true))
                        {
                            installed = true;
                        }

                        if (installed)
                        {
                            // already processed this so don't need to do this next time
                            depedenciesToProcessed.Add(dep.Id);
                            request.Verbose(String.Format(CultureInfo.CurrentCulture, Messages.AlreadyInstalled, dep.Id));
                            // already have a dependency so move on
                            continue;
                        }
                    }

                    // get all the packages that match this dependency
                    var dependentPackageItem = request.GetPackageById(dep.Id, request, minimumVersion: minVersion, maximumVersion: maxVersion, minInclusive: dep.DependencyVersion.IsMinInclusive, maxInclusive: dep.DependencyVersion.IsMaxInclusive, isDependency: true).ToArray();

                    if (dependentPackageItem.Length == 0)
                    {
                        request.WriteError(ErrorCategory.ObjectNotFound, dep.Id, Constants.Messages.UnableToFindDependencyPackage, dep.Id);

                        break;
                    }

                    yield return new Tuple<PackageItem, DependencyVersion>(dependentPackageItem.OrderByDescending(each => each.Version).FirstOrDefault(), dep.DependencyVersion);

                    depedenciesToProcessed.Add(depKey);
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
        /// <param name="progressTracker">progress tracker to help keep track of progressid, start and end of the progress</param>
        /// <returns>PackageItem object</returns>
        internal static PackageItem InstallPackageLocal(
            string packageName, 
            string version,
            NuGetRequest request, 
            PackageSource source,             
            string sourceFilePath,
            ProgressTracker progressTracker
            )
        {
            request.Debug(Messages.DebugInfoCallMethod, "NuGetClient", "InstallPackageLocal");

            string tempSourceFilePath = null;
            string tempSourceDirectory = null;

            string directoryToDeleteWhenFailed = String.Empty;
            bool needToDelete = false;

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
                    request.CreateDirectoryInternal(destinationFilePath);
                    directoryToDeleteWhenFailed = destinationFilePath;
                }

                //Make a temp folder in the user appdata temp directory 
                tempSourceFilePath = FileUtility.GetTempFileFullPath(fileExtension: NuGetConstant.PackageExtension);

                //Copy over the source file from  the folder repository to the temp folder
                File.Copy(sourceFilePath, tempSourceFilePath, true);

                request.Progress(progressTracker.ProgressID, progressTracker.StartPercent, string.Format(CultureInfo.CurrentCulture, Messages.Unzipping));
                //Unzip it
                tempSourceDirectory = PackageUtility.DecompressFile(tempSourceFilePath);

                //Get a package directory under the destination path to store the package
                string installedFolder = FileUtility.MakePackageDirectoryName(request.ExcludeVersion.Value, destinationFilePath, packageName, version);

                // if we did not set the directory before, then the destinationFilePath already exists, so we should not delete it
                if (string.IsNullOrWhiteSpace(directoryToDeleteWhenFailed))
                {
                    directoryToDeleteWhenFailed = installedFolder;
                }

                //File folder format of the Nuget packages looks like the following after installed:
                //Jquery.2.0.1
                //  - JQuery.2.0.1.nupkg
                //  - contents and other stuff

                // unzipping should take most of the time (assuming 70%)

                request.Progress(progressTracker.ProgressID, progressTracker.ConvertPercentToProgress(0.7), string.Format(CultureInfo.CurrentCulture, Messages.CopyUnzippedFiles, installedFolder));

                //Copy the unzipped files to under the package installed folder
                FileUtility.CopyDirectory(tempSourceDirectory, installedFolder, true);
                
                // copying should take another 15%
                // copy the nupkg file if it's not in
                var nupkgFilePath = Path.Combine(installedFolder, FileUtility.MakePackageFileName(request.ExcludeVersion.Value, packageName, version, NuGetConstant.PackageExtension));

                // only copy if this is not called from powershellget
                if (!request.IsCalledFromPowerShellGet && !File.Exists(nupkgFilePath))
                {
                    File.Copy(sourceFilePath, nupkgFilePath);
                }

                request.Progress(progressTracker.ProgressID, progressTracker.ConvertPercentToProgress(0.85), string.Format(CultureInfo.CurrentCulture, Messages.ReadingManifest));

                 //Read the package manifest and return the package object
                var nuspecFileName = packageName + NuGetConstant.ManifestExtension;
                var nuspec = FileUtility.GetFiles(installedFolder, "*.*", recursive: false)
                    .FirstOrDefault(each => Path.GetFileName(each).EqualsIgnoreCase(nuspecFileName));

                PackageBase package = PackageUtility.ProcessNuspec(nuspec);

                var pkgItem = new PackageItem {
                    Package = package,
                    PackageSource = source,
                    FastPath = request.MakeFastPath(source, package.Id, package.Version),
                    FullPath = installedFolder
                };

                // Delete the nuspec file
                if (!string.IsNullOrWhiteSpace(nuspec) && File.Exists(nuspec))
                {
                    FileUtility.DeleteFile(nuspec, false);
                }

                request.Debug(Messages.DebugInfoReturnCall, "NuGetClient", "InstallPackageLocal");

                request.Progress(progressTracker.ProgressID, progressTracker.EndPercent, string.Format(CultureInfo.CurrentCulture, Messages.FinishInstalling, packageName));

                return pkgItem;

            } catch (Exception ex) {
                needToDelete = true;
                // the error will be package "packageName" failed to install because : "reason"
                ex.Dump(request);
                request.WriteError(ErrorCategory.InvalidResult, packageName, Resources.Messages.PackageFailedToInstallReason, packageName, ex.Message);
                throw;
            } finally {
                if (needToDelete && Directory.Exists(directoryToDeleteWhenFailed))
                {
                    FileUtility.DeleteDirectory(directoryToDeleteWhenFailed, true, isThrow: false);
                }

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
        /// Download a nuget package.
        /// </summary>
        /// <param name="packageName">Package name</param>
        /// <param name="version">Package version</param>
        /// <param name="destination">Destination location to store the downloaded package</param>
        /// <param name="queryUrl">Uri to query the package</param>
        /// <param name="request">An object passed in from the PackageManagement platform that contains APIs that can be used to interact with it </param>   
        /// <param name="pkgSource">source to download the package</param>
        /// <param name="progressTracker">Utility class to help track progress</param>
        /// 
        internal static bool DownloadPackage(string packageName, string version, string destination, string queryUrl, NuGetRequest request, PackageSource pkgSource, ProgressTracker progressTracker) 
        {
            try {                
                request.Verbose(string.Format(CultureInfo.InvariantCulture, "DownloadPackage' - name='{0}', version='{1}',destination='{2}', uri='{3}'", packageName, version, destination, queryUrl));

                if (new Uri(queryUrl).IsFile) {
                    throw new ArgumentException(Constants.Messages.UriSchemeNotSupported, queryUrl);
                }

                long result = 0;

                // Do not need to validate here again because the job is done by the httprepository that supplies the queryurl
                //Downloading the package
                //request.Verbose(httpquery);
                result = DownloadDataToFileAsync(destination, queryUrl, request, PathUtility.GetNetworkCredential(request.CredentialUsername, request.CredentialPassword), progressTracker).Result;                   

                if (result == 0 || !File.Exists(destination))
                {
                    request.Verbose(Messages.FailedDownloadPackage, packageName, queryUrl);
                    request.Warning(Constants.Messages.SourceLocationNotValid, queryUrl);
                    return false;
                } else {
                    request.Verbose(Messages.CompletedDownload, packageName);
                    return true;
                }
            } catch (Exception ex) {
                ex.Dump(request);
                request.Warning(Constants.Messages.PackageFailedInstallOrDownload, packageName, CultureInfo.CurrentCulture.TextInfo.ToLower(Constants.Download));
                throw;
            }
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
        internal static Stream DownloadDataToStream(string query, RequestWrapper request, bool ignoreNullResponse = false, int tries = 3)
        {
            request.Debug(Messages.DownloadingPackage, query);

            var client = request.GetClientWithHeaders();

            var response = PathUtility.GetHttpResponse(client, query, (()=> request.IsCanceled()),
                ((msg, num) => request.Verbose(Resources.Messages.RetryingDownload, msg, num)), (msg)=> request.Verbose(msg), (msg)=> request.Debug(msg), remainingTry: tries);

            // Check that response was successful or throw exception
            if (response == null || !response.IsSuccessStatusCode)
            {
                if (response != null && (response.StatusCode == HttpStatusCode.Unauthorized) && request.Request.CredentialUsername.IsNullOrEmpty())
                {
                    // If response returns unsuccessful status code, try again using credentials retrieved from credential provider
                    // First call to the credential provider is to get credentials, but if those credentials fail,
                    // we call the cred provider again to ask the user for new credentials, and then search try to validate uri again using new creds
                    var credentials = request.Request.GetCredsFromCredProvider(query, request.Request, false);
                    var newClient = PathUtility.GetHttpClientHelper(credentials.UserName, credentials.SecurePassword, request.Proxy);

                    response = PathUtility.GetHttpResponse(newClient, query, (() => request.Request.IsCanceled),
                        ((msg, num) => request.Verbose(Resources.Messages.RetryingDownload, msg, num)), (msg) => request.Verbose(msg), (msg) => request.Debug(msg));
                    query = response.RequestMessage.RequestUri.AbsoluteUri;

                    request.Request.SetHttpClient(newClient);

                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        // Calling the credential provider for a second time, using -IsRetry
                        credentials = request.Request.GetCredsFromCredProvider(query, request.Request, true);
                        newClient = PathUtility.GetHttpClientHelper(credentials.UserName, credentials.SecurePassword, request.Proxy);

                        response = PathUtility.GetHttpResponse(newClient, query, (() => request.Request.IsCanceled),
                            ((msg, num) => request.Verbose(Resources.Messages.RetryingDownload, msg, num)), (msg) => request.Verbose(msg), (msg) => request.Debug(msg));
                        query = response.RequestMessage.RequestUri.AbsoluteUri;

                        request.Request.SetHttpClient(newClient);
                    }
                }
                if (response == null || !response.IsSuccessStatusCode)
                {
                    request.Debug(Resources.Messages.CouldNotGetResponseFromQuery, query);
                }

                return null;
            }

            // Read response and write out a stream
            var stream = GetStreamBasedOnEncoding(response);

            request.Debug(Messages.CompletedDownload, query);

            return stream;
        }

        /// <summary>
        /// Download data from remote via uri query.
        /// </summary>
        /// <param name="query">Uri query</param>
        /// <param name="request">An object passed in from the PackageManagement platform that contains APIs that can be used to interact with it </param>
        /// <returns></returns>
        internal static Stream DownloadDataToStream(string query, NuGetRequest request, bool ignoreNullResponse = false)
        {
            return DownloadDataToStream(query, new RequestWrapper(request), ignoreNullResponse);
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
        internal static Stream InitialDownloadDataToStream(UriBuilder query, int startPoint, int bufferSize, NuGetRequest request)
        {
            return InitialDownloadDataToStream(query, startPoint, bufferSize, new RequestWrapper(request));
        }

        internal static Stream InitialDownloadDataToStream(UriBuilder query, int startPoint, int bufferSize, RequestWrapper request)
        {
            var uri = String.Format(CultureInfo.CurrentCulture, query.Uri.ToString(), startPoint, bufferSize);
            request.Debug(Messages.DownloadingPackage, uri);

            var client = request.GetClientWithHeaders();

            var response = PathUtility.GetHttpResponse(client, uri, (() => request.IsCanceled()),
               ((msg, num) => request.Verbose(Resources.Messages.RetryingDownload, msg, num)), (msg) => request.Verbose(msg), (msg) => request.Debug(msg));

            // Check that response was successful or write error
            if (response == null || !response.IsSuccessStatusCode && request.Request.CredentialUsername.IsNullOrEmpty())
            {
                // If response returns unsuccessful status code, try again using credentials retrieved from credential provider
                // First call to the credential provider is to get credentials, but if those credentials fail,
                // we call the cred provider again to ask the user for new credentials, and then search try to validate uri again using new creds
                var credentials = request.Request.GetCredsFromCredProvider(query.ToString(), request.Request, false);
                client = PathUtility.GetHttpClientHelper(credentials.UserName, credentials.SecurePassword, request.Proxy);

                response = PathUtility.GetHttpResponse(client, query.ToString(), (() => request.Request.IsCanceled),
                    ((msg, num) => request.Verbose(Resources.Messages.RetryingDownload, msg, num)), (msg) => request.Verbose(msg), (msg) => request.Debug(msg));
                var queryStr = response.RequestMessage.RequestUri.AbsoluteUri;

                request.Request.SetHttpClient(client);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    // Calling the credential provider for a second time, using -IsRetry
                    credentials = request.Request.GetCredsFromCredProvider(queryStr, request.Request, true);
                    client = PathUtility.GetHttpClientHelper(credentials.UserName, credentials.SecurePassword, request.Proxy);

                    response = PathUtility.GetHttpResponse(client, queryStr, (() => request.Request.IsCanceled),
                        ((msg, num) => request.Verbose(Resources.Messages.RetryingDownload, msg, num)), (msg) => request.Verbose(msg), (msg) => request.Debug(msg));
                    queryStr = response.RequestMessage.RequestUri.AbsoluteUri;

                    request.Request.SetHttpClient(client);
                }

                if (response == null || !response.IsSuccessStatusCode)
                {
                    request.Debug(Resources.Messages.CouldNotGetResponseFromQuery, uri);
                    return null;
                }
            }

            // Read response and write out a stream
            var stream = GetStreamBasedOnEncoding(response);

            request.Debug(Messages.CompletedDownload, uri);

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
        /// <param name="networkCredential">Credential to pass along to get httpclient</param>
        /// <param name="progressTracker">Utility class to help track progress</param>
        /// <returns></returns>
        internal static async Task<long> DownloadDataToFileAsync(string fileName, string query, NuGetRequest request,
            NetworkCredential networkCredential, ProgressTracker progressTracker)
        {
            request.Verbose(Messages.DownloadingPackage, query);

            var httpClient = request.Client;

            // try downloading for 3 times
            int remainingTry = 3;
            long totalDownloaded = 0;
            long totalBytesToReceive = 0;
            bool cleanUp = false;
            CancellationTokenSource cts = new CancellationTokenSource(); ;
            Stream input = null;
            Timer timer = null;
            FileStream output = null;
            object lockObject = new object();

            // function to perform cleanup
            Action cleanUpAction = () => {
                lock (lockObject)
                {
                    // if clean up is done before, don't need to do again
                    if (!cleanUp)
                    {
                        try
                        {
                            // dispose timer
                            if (timer != null)
                            {
                                timer.Change(Timeout.Infinite, Timeout.Infinite);
                                timer.Dispose();
                            }

                            // dispose cts token
                            if (cts != null)
                            {
                                cts.Cancel();
                                cts.Dispose();
                            }
                        }
                        catch { }

                        try
                        {
                            // dispose input and output stream
                            if (input != null)
                            {
                                input.Dispose();
                            }

                            // it is important that we dispose of the output here, otherwise we may not be able to delete the file
                            if (output != null)
                            {
                                output.Dispose();
                            }

                            // if the download didn't complete, log verbose message
                            if (totalBytesToReceive != totalDownloaded)
                            {
                                request.Verbose(string.Format(Resources.Messages.IncompleteDownload, totalDownloaded, totalBytesToReceive));
                            }

                            // if we couldn't download anything
                            if (totalDownloaded == 0 && File.Exists(fileName))
                            {
                                File.Delete(fileName);
                            }
                        }
                        catch { }

                        cleanUp = true;
                    }
                }
            };

            while (remainingTry > 0)
            {
                // if user cancel the request, no need to do anything
                if (request.IsCanceled)
                {
                    break;
                }

                input = null;
                output = null;
                totalDownloaded = 0;

                try
                {
                    // decrease try by 1
                    remainingTry -= 1;

                    // create new timer and cancellation token source
                    lock (lockObject)
                    {
                        // check every second to see whether request is cancelled
                        timer = new Timer(_ =>
                        {
                            if (request.IsCanceled)
                            {
                                cleanUpAction();
                            }
                        }, null, 500, 1000);

                        cts = new CancellationTokenSource();

                        cleanUp = false;
                    }

                    var response = await httpClient.GetAsync(query, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                    if (response.Content != null && response.Content.Headers != null)
                    {
                        totalBytesToReceive = response.Content.Headers.ContentLength ?? 0;
                        // the total amount of bytes we need to download in megabytes
                        double totalBytesToReceiveMB = (totalBytesToReceive / 1024f) / 1024f;

                        // Read response asynchronously and write out a file
                        // The return value is for the caller to wait for the async operation to complete.
                        input = await response.Content.ReadAsStreamAsync();

                        // buffer size of 64 KB, this seems to be preferable buffer size, not too small and not too big
                        byte[] bytes = new byte[1024 * 64];
                        output = File.Open(fileName, FileMode.OpenOrCreate);

                        int current = 0;
                        double lastPercent = 0;

                        // here we read content that we got from the http response stream into the bytes array
                        current = await input.ReadAsync(bytes, 0, bytes.Length, cts.Token);

                        // report initial progress
                        request.Progress(progressTracker.ProgressID, progressTracker.StartPercent,
                            string.Format(CultureInfo.CurrentCulture, Resources.Messages.DownloadingProgress, 0, (totalBytesToReceive / 1024f) / 1024f));

                        while (current > 0)
                        {
                            totalDownloaded += current;

                            // here we write the bytes array content into the file
                            await output.WriteAsync(bytes, 0, current, cts.Token);

                            double percent = totalDownloaded * 1.0 / totalBytesToReceive;

                            // don't want to report too often (slow down performance)
                            if (percent > lastPercent + 0.1)
                            {
                                lastPercent = percent;
                                // percent between startProgress and endProgress
                                var progressPercent = progressTracker.ConvertPercentToProgress(percent);

                                // report the progress
                                request.Progress(progressTracker.ProgressID, (int)progressPercent,
                                    string.Format(CultureInfo.CurrentCulture, Resources.Messages.DownloadingProgress, (totalDownloaded / 1024f) / 1024f, totalBytesToReceiveMB));
                            }

                            // here we read content that we got from the http response stream into the bytes array
                            current = await input.ReadAsync(bytes, 0, bytes.Length, cts.Token);
                        }

                        // check that we download everything
                        if (totalDownloaded == totalBytesToReceive)
                        {
                            // report that we finished with the download
                            request.Progress(progressTracker.ProgressID, progressTracker.EndPercent,
                                string.Format(CultureInfo.CurrentCulture, Resources.Messages.DownloadingProgress, totalBytesToReceiveMB, totalBytesToReceiveMB));

                            request.Verbose(Messages.CompletedDownload, query);

                            break;
                        }

                        // otherwise, we have to retry again
                    }

                    // if request is canceled, don't retry
                    if (request.IsCanceled)
                    {
                        break;
                    }

                    request.Verbose(Resources.Messages.RetryingDownload, query, remainingTry);
                }
                catch (Exception ex)
                {
                    request.Verbose(ex.Message);
                    request.Debug(ex.StackTrace);
                    // if there is exception, we will retry too
                    request.Verbose(Resources.Messages.RetryingDownload, query, remainingTry);
                }
                finally
                {
                    cleanUpAction();
                }
            }

            return totalDownloaded;
        }
    }
}
