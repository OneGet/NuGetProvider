﻿namespace Microsoft.PackageManagement.NuGetProvider 
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Management.Automation;
    using System.Text.RegularExpressions;
    using System.Xml;
    using System.Xml.Linq;
    using System.Collections.Concurrent;
    using Resources;

    /// <summary> 
    /// This class drives the Request class that is an interface exposed from the PackageManagement Platform to the provider to use.
    /// </summary>
    public abstract class NuGetRequest : Request {
        private static readonly Regex _regexFastPath = new Regex(@"\$(?<source>[\w,\+,\/,=]*)\\(?<id>[\w,\+,\/,=]*)\\(?<version>[\w,\+,\/,=]*)\\(?<sources>[\w,\+,\/,=]*)");
        private string _configurationFileLocation;
        private XDocument _config;

        internal readonly Lazy<bool> AllowPrereleaseVersions;
        internal readonly Lazy<bool> AllVersions;
        internal readonly Lazy<string> Contains;
        internal readonly Lazy<bool> ExcludeVersion;
        internal readonly Lazy<string[]> Headers;
        internal readonly Lazy<string[]> SearchFilter;

        private static IDictionary<string, PackageSource> _registeredPackageSources;
        private static IDictionary<string, PackageSource> _checkedUnregisteredPackageSources = new ConcurrentDictionary<string, PackageSource>();

        internal Lazy<bool> SkipValidate;  //??? Seems to be a design choice. Why let a user to decide?
        internal Lazy<bool> SkipDependencies;     
        //internal ImplictLazy<bool> ContinueOnFailure;
        //internal ImplictLazy<bool> FindByCanonicalId;


        internal const string DefaultConfig = @"<?xml version=""1.0""?>
<configuration>
  <packageSources>
  </packageSources>
</configuration>";

        /// <summary>
        /// Ctor required by the PackageManagement Platform
        /// </summary>
        protected NuGetRequest() {
            Contains = new Lazy<string>(() => GetOptionValue("Contains"));
            ExcludeVersion = new Lazy<bool>(() => GetOptionValue("ExcludeVersion").IsTrue());
            AllowPrereleaseVersions = new Lazy<bool>(() => GetOptionValue("AllowPrereleaseVersions").IsTrue());
            AllVersions = new Lazy<bool>(() => GetOptionValue("AllVersions").IsTrue());

            SkipValidate = new Lazy<bool>(() => GetOptionValue("SkipValidate").IsTrue());

            SkipDependencies = new Lazy<bool>(() => GetOptionValue("SkipDependencies").IsTrue());
            //ContinueOnFailure = new ImplictLazy<bool>(() => GetOptionValue("ContinueOnFailure").IsTrue());           
            //FindByCanonicalId = new ImplictLazy<bool>(() => GetOptionValue("FindByCanonicalId").IsTrue());

            Headers = new Lazy<string[]>(() => (GetOptionValues("Headers") ?? new string[0]).ToArray());

            // Filter format: ["Tag=A", "Tag=B", "DscResource=C"]
            SearchFilter = new Lazy<string[]>(() => (GetOptionValues("SearchFilter").Union(GetOptionValues("FilterOnTag").Select(el => "Tag=" + el)) ?? new string[0]).ToArray());
        }

        /// <summary>
        /// Package sources
        /// </summary>
        internal string[] OriginalSources {get; set;}
       
        /// <summary>
        /// Package destination path
        /// </summary>
        internal string Destination {
            get {
                return Path.GetFullPath(GetOptionValue("Destination"));
            }
        }

        /// <summary>
        /// Get the PackageItem object from the fast path
        /// </summary>
        /// <param name="fastPath"></param>
        /// <returns></returns>
        internal PackageItem GetPackageByFastpath(string fastPath) {
            Debug(Resources.Messages.DebugInfoCallMethod3, "NuGetRequest", "GetPackageByFastpath", fastPath);

            string sourceLocation;
            string id;
            string version;
            string[] sources;

            if (TryParseFastPath(fastPath, out sourceLocation, out id, out version, out sources)) {
                var source = ResolvePackageSource(sourceLocation);

                if (source.IsSourceAFile) {
                    return GetPackageByFilePath(sourceLocation);
                }

                // Have to find package again to get possible dependencies
                var pkg = source.Repository.FindPackage(id, new SemanticVersion(version), this);

                // only finds the pkg if it is a file. so we don't return it here
                // otherwise we make another download request
                return new PackageItem {
                    FastPath = fastPath,
                    Package = pkg,
                    PackageSource = source,
                    Sources = sources
                };
            }

            return null;
        }

        /// <summary>
        /// Get a package object from the package manifest file
        /// </summary>
        /// <param name="filePath">package manifest file path</param>
        /// <param name="packageName">package Id or Name</param>
        /// <returns></returns>
        internal PackageItem GetPackageByFilePath(string filePath, string packageName) 
        {
            Debug(Resources.Messages.DebugInfoCallMethod3, "GetPackageByFilePath", filePath, packageName);

            PackageBase package = null;
            try {

                if (PathUtility.IsManifest(filePath)) {
                    //.nuspec 
                    package = PackageUtility.ProcessNuspec(filePath);

                } else if (PathUtility.IsPackageFile(filePath)) {
                    //.nupkg or .zip
                    //The file name may contains version.  ex: jQuery.2.1.1.nupkg
                    package = PackageUtility.DecompressFile(filePath, packageName);

                } else {
                    Warning(Resources.Messages.InvalidFileExtension, filePath);
                }

            } catch (Exception ex) {
                ex.Dump(this);
            }

            if (package == null) {
                return null;
            }

            var source = ResolvePackageSource(filePath);

            return new PackageItem {
                FastPath = MakeFastPath(source, package.Id, package.Version),
                PackageSource = source,
                Package = package,
                IsPackageFile = true,
                FullPath = filePath,
            };
        }

        /// <summary>
        /// Get a package object from the package manifest file
        /// </summary>
        /// <param name="filePath">package manifest file path</param>
        /// <returns></returns>
        internal PackageItem GetPackageByFilePath(string filePath) {
            Debug(Resources.Messages.DebugInfoCallMethod3, "NuGetRequest", "GetPackageByFilePath", filePath);
            var packageName = Path.GetFileNameWithoutExtension(filePath);

            var pkgItem = GetPackageByFilePath(filePath, packageName);

            return pkgItem;
        }

        /// <summary>
        /// Unregister the package source
        /// </summary>
        /// <param name="id">package source id or name</param>
        internal void RemovePackageSource(string id) {
            Debug(Resources.Messages.DebugInfoCallMethod3, "NuGetRequest", "RemovePackageSource", id);
            var config = Config;
            if (config == null) {
                return;
            }

            try {

                XElement configuration = config.ElementsNoNamespace("configuration").FirstOrDefault();
                if (configuration == null)
                {
                    return;
                }

                XElement packageSources = configuration.ElementsNoNamespace("packageSources").FirstOrDefault();
                if (packageSources == null)
                {
                    return;
                }

                var nodes = packageSources.Elements("add");
                if (nodes == null) {
                    return;
                }

                foreach (XElement node in nodes) {

                    if (node.Attribute("key") != null && String.Equals(node.Attribute("key").Value, id, StringComparison.OrdinalIgnoreCase)) {
                        // remove itself
                        node.Remove();
                        Config = config;
                        Verbose(Resources.Messages.RemovedPackageSource, id);
                        break;
                    }

                }

                if (_registeredPackageSources.ContainsKey(id))
                {
                    _registeredPackageSources.Remove(id);
                }

                //var source = config.SelectNodes("/configuration/packageSources/add").Cast<XmlNode>().FirstOrDefault(node => String.Equals(node.Attributes["key"].Value, id, StringComparison.CurrentCultureIgnoreCase));
                
                //if (source != null)
                //{
                //    source.ParentNode.RemoveChild(source);
                //    Config = config;
                //    Verbose(Resources.Messages.RemovedPackageSource, id);
                //}
            } catch (Exception ex) {
                ex.Dump(this);
            }
        }

        /// <summary>
        /// Register the package source
        /// </summary>
        /// <param name="name">package source name</param>
        /// <param name="location">package source location</param>
        /// <param name="isTrusted">is the source trusted</param>
        /// <param name="isValidated">need validate before storing the information to config file</param>
        internal void AddPackageSource(string name, string location, bool isTrusted, bool isValidated) {

            Debug(Resources.Messages.DebugInfoCallMethod, "NuGetRequest", string.Format(CultureInfo.InvariantCulture, "AddPackageSource - name= {0}, location={1}", name, location));
            try {
                // here the source is already validated by the caller
                var config = Config;
                if (config == null) {
                    return;
                }

                XElement source = null;
                XElement packageSources = null;
                // Check whether there is an existing node with the same name
                var configuration = config.ElementsNoNamespace("configuration").FirstOrDefault();
                if (configuration != null)
                {
                    packageSources = configuration.ElementsNoNamespace("packageSources").FirstOrDefault();
                    if (packageSources != null)
                    {
                        source = packageSources.Elements("add").FirstOrDefault(node =>
                            node.Attribute("key") != null && String.Equals(node.Attribute("key").Value, name, StringComparison.OrdinalIgnoreCase));
                    }
                }
                else
                {
                    // create configuration node if it does not exist
                    configuration = new XElement("configuration");
                    // add that to the config
                    config.Add(configuration);
                }

                // There is no existing node with the same name. So we have to create one.
                if (source == null)
                {
                    // if packagesources is null we have to create that too
                    if (packageSources == null)
                    {
                        // create packagesources node
                        packageSources = new XElement("packageSources");
                        // add that to the config
                        configuration.Add(packageSources);
                    }

                    // Create new source
                    source = new XElement("add");
                    // Add that to packagesource
                    packageSources.Add(source);
                }

                // Now set the source node properties
                source.SetAttributeValue("key", name);
                source.SetAttributeValue("value", location);
                if (isValidated)
                {
                    source.SetAttributeValue("validated", true.ToString());
                }
                if (isTrusted)
                {
                    source.SetAttributeValue("trusted", true.ToString());
                }

                // Write back to the config file
                Config = config;

                // Add or set the source node from the dictionary depends on whether it was there
                if (_registeredPackageSources.ContainsKey(name))
                {
                    var packageSource = _registeredPackageSources[name];
                    packageSource.Name = name;
                    packageSource.Location = location;
                    packageSource.Trusted = isTrusted;
                    packageSource.IsRegistered = true;
                    packageSource.IsValidated = isValidated;
                }
                else
                {
                    _registeredPackageSources.Add(name, new PackageSource
                    {
                        Name = name,
                        Location = location,
                        Trusted = isTrusted,
                        IsRegistered = true,
                        IsValidated = isValidated,
                    });
                }

            } catch (Exception ex) {
                ex.Dump(this);
            }
        }

        /// <summary>
        /// Check if the package source location is valid
        /// </summary>
        /// <param name="location"></param>
        /// <returns></returns>
        internal bool ValidateSourceLocation(string location) {

            Debug(Resources.Messages.DebugInfoCallMethod3, "NuGetRequest", "ValidateSourceLocation", location);
            //Handling http: or file: cases
            if (Uri.IsWellFormedUriString(location, UriKind.Absolute)) {
                return PathUtility.ValidateSourceUri(SupportedSchemes, new Uri(location));
            }
            try {
                //UNC or local file
                if (Directory.Exists(location) || File.Exists(location)) {
                    return true;
                }
            } catch {
            }
            return false;
        }

        /// <summary>
        /// Supported package source schemes by this provider
        /// </summary>
        internal static IEnumerable<string> SupportedSchemes {
            get {
                return NuGetProvider.Features[Constants.Features.SupportedSchemes];
            }
        }

        /// <summary>
        /// Return the package source object
        /// </summary>
        /// <param name="name">The package source name to search for</param>
        /// <returns>package source object</returns>
        internal PackageSource FindRegisteredSource(string name) {

            Debug(Resources.Messages.DebugInfoCallMethod3, "NuGetRequest", "FindRegisteredSource", name);
            var srcs = RegisteredPackageSources;

            if (srcs == null) {
                return null;
            }
            if (srcs.ContainsKey(name)) {
                return srcs[name];
            }

            var src = srcs.Values.FirstOrDefault(each => LocationCloseEnoughMatch(name, each.Location));
            return src;
        }

        /// <summary>
        /// Communicate to the PackageManagement platform about the package info
        /// </summary>
        /// <param name="packageReferences"></param>
        /// <param name="searchKey"></param>
        /// <returns></returns>
        internal bool YieldPackages(IEnumerable<PackageItem> packageReferences, string searchKey) {
            var foundPackage = false;
            if (packageReferences == null) {
                return false;
            }

            Debug(Resources.Messages.Iterating, searchKey);

            IEnumerable<PackageItem>  packageItems = packageReferences;

            int count = 0;

            foreach (var pkg in packageItems) {
                foundPackage = true;
                try {
                    if (!YieldPackage(pkg, searchKey)) {
                        break;
                    }

                    count++;
                } catch (Exception e) {
                    e.Dump(this);
                    return false;
                }
            }

            Verbose(Resources.Messages.TotalPackageYield, count, searchKey);
            Debug(Resources.Messages.CompletedIterating, searchKey);

            return foundPackage;
        }

        /// <summary>
        /// Communicate to the PackageManagement platform about the package info
        /// </summary>
        /// <param name="pkg"></param>
        /// <param name="searchKey"></param>
        /// <returns></returns>
        internal bool YieldPackage(PackageItem pkg, string searchKey) 
        {
            try 
            {
                if (YieldSoftwareIdentity(pkg.FastPath, pkg.Package.Id, pkg.Package.Version.ToString(), "semver", pkg.Package.Summary, pkg.PackageSource.Name, searchKey, pkg.FullPath, pkg.PackageFilename) != null) 
                {
                    if (pkg.Package.DependencySetList != null)
                    {
                        //iterate thru the dependencies and add them to the software identity.
                        foreach (PackageDependencySet depSet in pkg.Package.DependencySetList)
                        {
                            foreach (var dep in depSet.Dependencies)
                            {
                                AddDependency(NuGetConstant.ProviderName, dep.Id, dep.DependencyVersion.ToStringSafe(), null, null);
                            }
                        }
                    }

                    if (AddMetadata(pkg.FastPath, "copyright", pkg.Package.Copyright) == null) {
                        return false;
                    }
                    if (AddMetadata(pkg.FastPath, "description", pkg.Package.Description) == null) {
                        return false;
                    }
                    if (AddMetadata(pkg.FastPath, "language", pkg.Package.Language) == null) {
                        return false;
                    }
                    if (AddMetadata(pkg.FastPath, "releaseNotes", pkg.Package.ReleaseNotes) == null) {
                        return false;
                    }
                    if (pkg.Package.Published != null) {
                        if (AddMetadata(pkg.FastPath, "published", pkg.Package.Published.ToString()) == null) {
                            return false;
                        }
                    }
                    if (AddMetadata(pkg.FastPath, "tags", pkg.Package.Tags) == null) {
                        return false;
                    }
                    if (AddMetadata(pkg.FastPath, "title", pkg.Package.Title) == null) {
                        return false;
                    }
                    if (AddMetadata(pkg.FastPath, "developmentDependency", pkg.Package.DevelopmentDependency.ToString()) == null) {
                        return false;
                    }
                    if (AddMetadata(pkg.FastPath, "FromTrustedSource", pkg.PackageSource.Trusted.ToString()) == null) {
                        return false;
                    }
                    if (pkg.Package.LicenseUrl != null && !String.IsNullOrWhiteSpace(pkg.Package.LicenseUrl.ToString()))
                    {
                        if (AddLink(pkg.Package.LicenseUrl, "license", null, null, null, null, null) == null) {
                            return false;
                        }
                    }
                    if (pkg.Package.ProjectUrl != null && !String.IsNullOrWhiteSpace(pkg.Package.ProjectUrl.ToString()))
                    {
                        if (AddLink(pkg.Package.ProjectUrl, "project", null, null, null, null, null) == null) {
                            return false;
                        }
                    }
                    if (pkg.Package.ReportAbuseUrl != null && !String.IsNullOrWhiteSpace(pkg.Package.ReportAbuseUrl.ToString()))
                    {
                        if (AddLink(pkg.Package.ReportAbuseUrl, "abuse", null, null, null, null, null) == null) {
                            return false;
                        }
                    }
                    if (pkg.Package.IconUrl != null && !String.IsNullOrWhiteSpace(pkg.Package.IconUrl.ToString()))
                    {
                        if (AddLink(pkg.Package.IconUrl, "icon", null, null, null, null, null) == null) {
                            return false;
                        }
                    }
                    if (pkg.Package.Authors.Any(author => AddEntity(author.Trim(), author.Trim(), "author", null) == null)) {
                        return false;
                    }

                    if (pkg.Package.Owners.Any(owner => AddEntity(owner.Trim(), owner.Trim(), "owner", null) == null)) {
                        return false;
                    }

                    var pkgBase = pkg.Package as PackageBase;
                    if (pkgBase != null)
                    {
                        if (pkgBase.AdditionalProperties != null)
                        {
                            foreach (var property in pkgBase.AdditionalProperties)
                            {
                                if (AddMetadata(pkg.FastPath, property.Key, property.Value) == null)
                                {
                                    return false;
                                }
                            }
                        }
                    }                
                } else {
                    return false;
                }
            } catch (Exception e) {
                e.Dump(this);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Search in the destination of the request to checks whether the package name is installed.
        /// Returns true if at least 1 package is installed
        /// </summary>
        /// <param name="name"></param>
        /// <param name="requiredVersion"></param>
        /// <param name="minimumVersion"></param>
        /// <param name="maximumVersion"></param>
        /// <param name="minInclusive"></param>
        /// <param name="maxInclusive"></param>
        /// <param name="terminateFirstFound"></param>
        /// <returns></returns>
        internal bool GetInstalledPackages(string name, string requiredVersion, string minimumVersion, string maximumVersion, bool minInclusive = true, bool maxInclusive = true, bool terminateFirstFound = false)
        {
            try
            {
                bool found = false;
                // if directory does not exist then just return false
                if (!Directory.Exists(Destination))
                {
                    return found;
                }

                // look in the destination directory for directories that contain *.nupkg & .nuspec files.
                var subdirs = Directory.EnumerateDirectories(Destination);

                foreach (var subdir in subdirs)
                {
                    //reset the flag when we begin a folder
                    var isDup = false;

                    var nupkgs = Directory.EnumerateFiles(subdir, "*.nupkg", SearchOption.TopDirectoryOnly).Union(
                        Directory.EnumerateFiles(subdir, "*.nuspec", SearchOption.TopDirectoryOnly));

                    foreach (var pkgFile in nupkgs)
                    {

                        if (isDup)
                        {
                            continue;
                        }

                        //As the package name has to be in the installed file path, check if it is true             
                        if (!String.IsNullOrWhiteSpace(name) && !NuGetProvider.IsNameMatch(name, pkgFile))
                        {
                            //not the right package
                            continue;
                        }

                        //unpack the package
                        var existFileName = Path.GetFileNameWithoutExtension(pkgFile);

                        var pkgItem = GetPackageByFilePath(pkgFile, existFileName);

                        if (pkgItem != null && pkgItem.IsInstalled)
                        {
                            //A user does not provide any package name in the commandeline, return them all
                            if (string.IsNullOrWhiteSpace(name))
                            {
                                isDup = true;
                                if (!YieldPackage(pkgItem, existFileName))
                                {
                                    return found;
                                }
                            }
                            else
                            {

                                //check if the version matches
                                if (!string.IsNullOrWhiteSpace(requiredVersion))
                                {
                                    if (pkgItem.Package.Version != new SemanticVersion(requiredVersion))
                                    {
                                        continue;
                                    }
                                }
                                else
                                {
                                    if (!string.IsNullOrWhiteSpace(minimumVersion))
                                    {
                                        // Check whether we are checking for min inclusive version
                                        if (minInclusive)
                                        {
                                            // version too small, not what we are looking for
                                            if (pkgItem.Package.Version < new SemanticVersion(minimumVersion))
                                            {
                                                continue;
                                            }
                                        }
                                        else
                                        {
                                            if (pkgItem.Package.Version <= new SemanticVersion(minimumVersion))
                                            {
                                                continue;
                                            }
                                        }
                                    }
                                    if (!string.IsNullOrWhiteSpace(maximumVersion))
                                    {
                                        // Check whether we are checking for max inclusive version
                                        if (maxInclusive)
                                        {
                                            // version too big, not what we are looking for
                                            if (pkgItem.Package.Version > new SemanticVersion(maximumVersion))
                                            {
                                                continue;
                                            }
                                        }
                                        else
                                        {
                                            if (pkgItem.Package.Version >= new SemanticVersion(maximumVersion))
                                            {
                                                continue;
                                            }
                                        }
                                    }

                                    if (terminateFirstFound)
                                    {
                                        // if we are searching for a name and terminatefirstfound is set to true, then returns here
                                        return true;
                                    }
                                }

                                //found the match 
                                isDup = true;
                                YieldPackage(pkgItem, existFileName);
                                found = true;

                            } //end of else

                        } //end of if (pkgItem...)
                    } //end of foreach
                }//end of foreach subfolder

                return found;
            }
            catch (Exception ex)
            {
                ex.Dump(this);
                return false;
            }
        }

        /// <summary>
        /// Get the package based on given package id
        /// </summary>
        /// <param name="name">Package id or name</param>
        /// <param name="requiredVersion"></param>
        /// <param name="minimumVersion"></param>
        /// <param name="maximumVersion"></param>
        /// <param name="maxInclusive"></param>
        /// <param name="minInclusive"></param>
        /// <param name="request"></param>
        /// <returns></returns>
        internal IEnumerable<PackageItem> GetPackageById(string name, Request request, string requiredVersion = null,
            string minimumVersion = null, string maximumVersion = null, bool minInclusive = true, bool maxInclusive = true) {
            if (String.IsNullOrWhiteSpace(name))
            {
                return Enumerable.Empty<PackageItem>();
            }

            return SelectedSources.AsParallel().WithMergeOptions(ParallelMergeOptions.NotBuffered).SelectMany(source => GetPackageById(source, name, request, requiredVersion, minimumVersion, maximumVersion, minInclusive, maxInclusive));
        }

        /// <summary>
        /// Encrypting a path containing package source location, id, version and source
        /// </summary>
        /// <param name="source">package source</param>
        /// <param name="id">package id</param>
        /// <param name="version">package version</param>
        /// <returns></returns>
        internal string MakeFastPath(PackageSource source, string id, string version) {
            return String.Format(CultureInfo.InvariantCulture, @"${0}\{1}\{2}\{3}", source.Serialized, id.ToBase64(), version.ToBase64(), (Sources ?? new string[0]).Select(each => each.ToBase64()).SafeAggregate((current, each) => current + "|" + each));
        }

        /// <summary>
        /// Return all registered package sources
        /// </summary>
        internal IEnumerable<PackageSource> SelectedSources {
            get {
                if (IsCanceled) {
                    yield break;
                }

                //get sources from user's input
                var sources = (OriginalSources ?? Enumerable.Empty<string>()).Union(PackageSources ?? Enumerable.Empty<string>()).ToArray();
                //get sources from config file that registered earlier 
                var pkgSources = RegisteredPackageSources;

                Verbose(Resources.Messages.RegisteredSources, pkgSources.Count, NuGetConstant.ProviderName);
                
                //If a user does not provider -source, we use the registered ones
                if (sources.Length == 0) {
                    // return them all.
                    foreach (var src in pkgSources.Values) {
                        Debug(src.Name);
                        yield return src;
                    }
                    yield break;
                }

                // otherwise, return package sources that match the items given.
                foreach (var src in sources)
                {
                    // Check whether we've already processed this item before
                    if (_checkedUnregisteredPackageSources.ContainsKey(src))
                    {
                        yield return _checkedUnregisteredPackageSources[src];
                        continue;
                    }

                    // check to see if we have a source with either that name
                    // or that URI first.
                    if (pkgSources.ContainsKey(src))
                    {
                        Debug(Resources.Messages.FoundRegisteredSource, src, NuGetConstant.ProviderName);
                        _checkedUnregisteredPackageSources.Add(src, pkgSources[src]);
                        yield return pkgSources[src];
                        continue;
                    }

                    var srcLoc = src;
                    var found = false;
                    foreach (var byLoc in pkgSources.Values.Where(each => each.Location == srcLoc))
                    {
                        _checkedUnregisteredPackageSources.Add(srcLoc, byLoc);
                        yield return byLoc;
                        found = true;
                    }
                    if (found)
                    {
                        continue;
                    }

                    Debug(Resources.Messages.NotFoundRegisteredSource, src, NuGetConstant.ProviderName);

                    // doesn't look like we have this as a source.
                    if (Uri.IsWellFormedUriString(src, UriKind.Absolute))
                    {
                        // we have been passed in an URI
                        var srcUri = new Uri(src);
                        if (SupportedSchemes.Contains(srcUri.Scheme.ToLower()))
                        {
                            // it's one of our supported uri types.
                            var isValidated = false;

                            if (!SkipValidate.Value)
                            {
                                isValidated = PathUtility.ValidateSourceUri(SupportedSchemes, srcUri);
                            }

                            if (SkipValidate.Value || isValidated)
                            {
                                Debug(Resources.Messages.SuccessfullyValidated, src);

                                PackageSource newSource = new PackageSource
                                    {
                                        Location = srcUri.ToString(),
                                        Name = srcUri.ToString(),
                                        Trusted = false,
                                        IsRegistered = false,
                                        IsValidated = isValidated,
                                    };
                                _checkedUnregisteredPackageSources.Add(src, newSource);
                                yield return newSource;
                                continue;
                            }
                            WriteError(ErrorCategory.InvalidArgument, src, Constants.Messages.SourceLocationNotValid, src);
                            Warning(Constants.Messages.UnableToResolveSource, src);
                            continue;
                        }

                        // Not a valid location?
                        WriteError(ErrorCategory.InvalidArgument, src, Constants.Messages.UriSchemeNotSupported, src);
                        Warning(Constants.Messages.UnableToResolveSource, src);
                        continue;
                    }

                    // is it a file path?
                    if (Directory.Exists(src))
                    {
                        Debug(Resources.Messages.SourceIsADirectory, src);

                        PackageSource newSource = new PackageSource
                            {
                                Location = src,
                                Name = src,
                                Trusted = true,
                                IsRegistered = false,
                                IsValidated = true,
                            };
                        _checkedUnregisteredPackageSources.Add(src, newSource);
                        yield return newSource;
                    }
                    else
                    {
                        // Not a valid location?
                        Warning(Constants.Messages.UnableToResolveSource, src);
                    }
                }
            }
        }

        private XDocument Config {
            get {
                if (_config == null)
                {
                    Debug(Resources.Messages.LoadingConfigurationFile, ConfigurationFileLocation);

                    XDocument doc = null;

                    try{
                        using (FileStream fs = new FileStream(ConfigurationFileLocation, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                            // load the xdocument from the file stream
                            doc = XmlUtility.LoadSafe(fs, ignoreWhiteSpace: true);
                        }

                        Debug(Resources.Messages.LoadedConfigurationFile, ConfigurationFileLocation);

                        if (doc.Root != null && doc.Root.Name != null && String.Equals(doc.Root.Name.LocalName, "configuration", StringComparison.OrdinalIgnoreCase)) {
                            _config = doc;
                            return _config;
                        }
                        Warning(Resources.Messages.MissingConfigurationElement, ConfigurationFileLocation);
                    }
                    catch (Exception e)
                    {
                        // a bad xml doc or a folder gets deleted somehow
                        Warning(e.Message);                      
                        //string dir = Path.GetDirectoryName(ConfigurationFileLocation);
                        //if (dir != null && !Directory.Exists(dir))
                        //{
                        //    Debug(Resources.Messages.CreateDirectory, dir);  
                        //    Directory.CreateDirectory(dir);
                        //}

                        //Debug(Resources.Messages.UseDefaultConfig);                      
                        //_config.Load(new MemoryStream((byte[])Encoding.UTF8.GetBytes(DefaultConfig)));
                    }
                }

                return _config;
            }
            set {
                
                if (value == null) {
                    Debug(Resources.Messages.SettingConfigurationToNull);
                    return;
                }

                _config = value;

                Verbose(Resources.Messages.SavingConfigurationWithFile, ConfigurationFileLocation);
                var stringBuilder = new System.Text.StringBuilder();

                var settings = new XmlWriterSettings
                {
                    OmitXmlDeclaration = false,
                    Indent = true,
                    NewLineOnAttributes = true,
                    NamespaceHandling = NamespaceHandling.OmitDuplicates
                };

                using (var xmlWriter = XmlWriter.Create(stringBuilder, settings))
                {
                    _config.Save(xmlWriter);
                    File.WriteAllText(ConfigurationFileLocation, _config.ToString());
                }
            }
        }

        private string ConfigurationFileLocation {
            get {
                if (String.IsNullOrWhiteSpace(_configurationFileLocation))
                {
                    // get the value from the request
                    var path = GetOptionValue("ConfigFile");

                    if (!String.IsNullOrWhiteSpace(path))
                    {
                        _configurationFileLocation = path;

                        if (!File.Exists(_configurationFileLocation))
                        {
                            WriteError(ErrorCategory.InvalidArgument, _configurationFileLocation, Resources.Messages.FileNotFound);
                        }

                    } else {
                        var appdataFolder = Environment.GetEnvironmentVariable("localappdata");
                        _configurationFileLocation = Path.Combine(appdataFolder, "nuget", NuGetConstant.SettingsFileName);

                        //create directory if does not exist
                        string dir = Path.GetDirectoryName(_configurationFileLocation);
                        if (dir != null && !Directory.Exists(dir))
                        {
                            Debug(Resources.Messages.CreateDirectory, dir);
                            Directory.CreateDirectory(dir);
                        }
                        //create place holder config file
                        if (!File.Exists(_configurationFileLocation))
                        {
                            Debug(Resources.Messages.CreateFile, _configurationFileLocation);
                            File.WriteAllText(_configurationFileLocation, DefaultConfig);
                        }
                    }
                }
              
                return _configurationFileLocation;
            }
        }

        private IDictionary<string, PackageSource> RegisteredPackageSources
        {
            get {
                if (_registeredPackageSources == null)
                {
                    _registeredPackageSources = new ConcurrentDictionary<string, PackageSource>(StringComparer.OrdinalIgnoreCase);

                    try
                    {
                        Debug(Resources.Messages.DebugInfoCallMethod3, "NuGetRequest", "RegisteredPackageSources", ConfigurationFileLocation);

                        var config = Config;
                        if (config != null)
                        {
                            // get the configuration node
                            var configuration = config.ElementsNoNamespace("configuration").FirstOrDefault();
                            if (configuration != null)
                            {
                                // get the packageSources node
                                var packageSources = configuration.ElementsNoNamespace("packageSources").FirstOrDefault();
                                if (packageSources != null)
                                {
                                    _registeredPackageSources = packageSources.Elements("add")
                                        .Where(each => each.Attribute("key") != null && each.Attribute("value") != null)
                                        .ToDictionaryNicely(each => each.Attribute("key").Value, each =>
                                            new PackageSource
                                            {
                                                Name = each.Attribute("key").Value,
                                                Location = each.Attribute("value").Value,
                                                Trusted = each.Attribute("trusted") != null && each.Attribute("trusted").Value.IsTrue(),
                                                IsRegistered = true,
                                                IsValidated = each.Attribute("validated") != null && each.Attribute("validated").Value.IsTrue(),
                                            }, StringComparer.OrdinalIgnoreCase);
                                }
                            }
                        }
                    }
                    catch (Exception)
                    {
                        _registeredPackageSources = new ConcurrentDictionary<string, PackageSource>();
                    }
                }

                return _registeredPackageSources;
            }
        }

        private IEnumerable<PackageItem> GetPackageById(PackageSource source, 
            string name,
            Request request, 
            string requiredVersion = null, 
            string minimumVersion = null, 
            string maximumVersion = null,
            bool minInclusive = true,
            bool maxInclusive = true) {
            try {
                Debug(Resources.Messages.DebugInfoCallMethod3, "NuGetRequest", "GetPackageById", name);

                // otherwise fall back to traditional behavior
                var pkgs = source.Repository.FindPackagesById(name, request);

                if (AllVersions.Value){
                    //Display versions from lastest to oldest
                    pkgs = (from p in pkgs select p).OrderByDescending(x => x.Version);
                }

                //A user does not provide version info, we choose the latest
                if (!AllVersions.Value && (String.IsNullOrWhiteSpace(requiredVersion) && String.IsNullOrWhiteSpace(minimumVersion) && String.IsNullOrWhiteSpace(maximumVersion)))
                {

                    if (AllowPrereleaseVersions.Value || source.Repository.IsFile) {
                        //Handling something like Json.NET 7.0.1-beta3 as well as local repository
                        pkgs = from p in pkgs
                            group p by p.Id
                            into newGroup
                                   select newGroup.Aggregate((current, next) => (next.Version > current.Version) ? next : current);

                    } else {
                        pkgs = from p in pkgs where p.IsLatestVersion select p;
                    }
                }
             
                pkgs = FilterOnContains(pkgs);
                pkgs = ApplySearchFilter(pkgs);

                var results = FilterOnVersion(pkgs, requiredVersion, minimumVersion, maximumVersion, minInclusive, maxInclusive)
                    .Select(pkg => new PackageItem {
                        Package = pkg,
                        PackageSource = source,
                        FastPath = MakeFastPath(source, pkg.Id, pkg.Version.ToString())
                    });
                return results;
            } catch (Exception e) {
                e.Dump(this);
                return Enumerable.Empty<PackageItem>();
            }
        }

        private PackageSource ResolvePackageSource(string nameOrLocation) {
            Debug(Resources.Messages.DebugInfoCallMethod3, "NuGetRequest", "ResolvePackageSource", nameOrLocation);

            if (IsCanceled) {
                return null;
            }

            var source = FindRegisteredSource(nameOrLocation);
            if (source != null) {
                Debug(Resources.Messages.FoundRegisteredSource, nameOrLocation, NuGetConstant.ProviderName);
                return source;
            }

            Debug(Resources.Messages.NotFoundRegisteredSource, nameOrLocation, NuGetConstant.ProviderName);

            try {
                // is the given value a filename?
                if (File.Exists(nameOrLocation)) {
                    Debug(Resources.Messages.SourceIsAFilePath, nameOrLocation);

                    return new PackageSource() {
                        IsRegistered = false,
                        IsValidated = true,
                        Location = nameOrLocation,
                        Name = nameOrLocation,
                        Trusted = true,
                    };
                }
            } catch {
            }

            try {
                // is the given value a directory?
                if (Directory.Exists(nameOrLocation)) {
                    Debug(Resources.Messages.SourceIsADirectory, nameOrLocation);
                    return new PackageSource() {
                        IsRegistered = false,
                        IsValidated = true,
                        Location = nameOrLocation,
                        Name = nameOrLocation,
                        Trusted = true,
                    };
                }
            } catch {
            }

            if (Uri.IsWellFormedUriString(nameOrLocation, UriKind.Absolute)) {
                var uri = new Uri(nameOrLocation, UriKind.Absolute);
                if (!SupportedSchemes.Contains(uri.Scheme.ToLowerInvariant())) {
                    WriteError(ErrorCategory.InvalidArgument, uri.ToString(), Constants.Messages.UriSchemeNotSupported, uri);
                    return null;
                }

                // this is an URI, and it looks like one type that we support
                if (SkipValidate.Value || PathUtility.ValidateSourceUri(SupportedSchemes, uri)) {
                    return new PackageSource {
                        IsRegistered = false,
                        IsValidated = !SkipValidate.Value,
                        Location = nameOrLocation,
                        Name = nameOrLocation,
                        Trusted = false,
                    };
                }
            }

            WriteError(ErrorCategory.InvalidArgument, nameOrLocation, Constants.Messages.UnableToResolveSource, nameOrLocation);
            return null;
        }

        private bool TryParseFastPath(string fastPath, out string source, out string id, out string version, out string[] sources) 
        {
            var match = _regexFastPath.Match(fastPath);
            source = match.Success ? match.Groups["source"].Value.FromBase64() : null;
            id = match.Success ? match.Groups["id"].Value.FromBase64() : null;
            version = match.Success ? match.Groups["version"].Value.FromBase64() : null;
            var srcs = match.Success ? match.Groups["sources"].Value : string.Empty;
            sources = srcs.Split('|').Select(each => each.FromBase64()).Where(each => !string.IsNullOrWhiteSpace(each)).ToArray();
            return match.Success;
        }

        private bool LocationCloseEnoughMatch(string givenLocation, string knownLocation) {
            if (givenLocation.Equals(knownLocation, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            // make trailing slashes consistent
            if (givenLocation.TrimEnd('/').Equals(knownLocation.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            // and trailing backslashes
            if (givenLocation.TrimEnd('\\').Equals(knownLocation.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
            return false;
        }

        private IEnumerable<IPackage> FilterOnVersion(IEnumerable<IPackage> pkgs, string requiredVersion, string minimumVersion, string maximumVersion, bool minInclusive = true, bool maxInclusive = true) {
            if (!String.IsNullOrWhiteSpace(requiredVersion))
            {
                pkgs = pkgs.Where(each => each.Version == new SemanticVersion(requiredVersion));
            } else {
                if (!String.IsNullOrWhiteSpace(minimumVersion))
                {
                    // if minInclusive, then use >= else use >
                    if (minInclusive)
                    {
                        pkgs = pkgs.Where(each => each.Version >= new SemanticVersion(minimumVersion));
                    }
                    else
                    {
                        pkgs = pkgs.Where(each => each.Version > new SemanticVersion(minimumVersion));
                    }
                }

                if (!String.IsNullOrWhiteSpace(maximumVersion))
                {
                    // if maxInclusive, then use < else use <=
                    if (maxInclusive)
                    {
                        pkgs = pkgs.Where(each => each.Version <= new SemanticVersion(maximumVersion));
                    }
                    else
                    {
                        pkgs = pkgs.Where(each => each.Version < new SemanticVersion(maximumVersion));
                    }
                }
            }
           
            return pkgs;
        }

        private IEnumerable<IPackage> FilterOnName(IEnumerable<IPackage> pkgs, string searchTerm, bool useWildCard)
        {
            if (useWildCard) 
            {
                // Applying the wildcard pattern matching
                const WildcardOptions wildcardOptions = WildcardOptions.CultureInvariant | WildcardOptions.IgnoreCase;
                var wildcardPattern = new WildcardPattern(searchTerm, wildcardOptions);

                return pkgs.Where(p => wildcardPattern.IsMatch(p.Id));

            } else {
                return pkgs.Where(each => each.Id.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) > -1);
            }   
        }

        private IEnumerable<IPackage> ApplySearchFilter(IEnumerable<IPackage> pkgs)
        {
            if (SearchFilter == null || SearchFilter.Value.Length == 0)
            {
                foreach (var pkg in pkgs)
                {
                    yield return pkg;
                }

                yield break;
            }

            //Filtering is performed as *AND* intead of *OR"
            //For example -SearchFilter:["Tag=A", "Tag=B"], the returned package should have both A and B.
            foreach (var pkg in pkgs)
            {
                bool includePkg = true;

                foreach (var filter in SearchFilter.Value)
                {
                    string key;
                    string value;

                    if (ParseKeyValueString(filter, out key, out value))
                    {
                        // For now, we only post-filter on Tags. We have a work item MSFT: 4583630 to filter on all properties.
                        if (key == "Tag")
                        {
                            if (pkg.Tags.IndexOf(value, StringComparison.OrdinalIgnoreCase) <= -1)
                            {
                                includePkg = false;
                                break;
                            }
                        }
                    }
                }

                if (includePkg)
                {
                    yield return pkg;
                }
            }
        }
        

        private IEnumerable<IPackage> FilterOnContains(IEnumerable<IPackage> pkgs) {
            if (string.IsNullOrWhiteSpace(Contains.Value))
            {
                return pkgs;
            }
            return pkgs.Where(each => each.Description.IndexOf(Contains.Value, StringComparison.OrdinalIgnoreCase) > -1 || 
                each.Id.IndexOf(Contains.Value, StringComparison.OrdinalIgnoreCase) > -1);
        }

        internal IEnumerable<PackageItem> SearchForPackages(string name) 
        {
            var sources = SelectedSources.AsParallel().WithMergeOptions(ParallelMergeOptions.NotBuffered);

            return sources.SelectMany(source => SearchForPackages(source, name));
        }

        /// <summary>
        /// Search the entire repository for the packages
        /// </summary>
        /// <param name="source">Package Source</param>
        /// <param name="name">Package name</param>
        /// <returns></returns>
        private IEnumerable<PackageItem> SearchForPackages(PackageSource source, string name)
        {
            try 
            {
                Debug(Resources.Messages.DebugInfoCallMethod3, "NuGetRequest", "SearchForPackages", name);

                var isNameContainsWildCard = false;
                var searchTerm = Contains.Value ?? string.Empty;

                // Deal with two cases here:
                // 1. The package name contains wildcards like "Find-Package -name JQ*". We search based on the name.
                // 2. A user does not provide the Name parameter,

                // A user provides name with wildcards. We will use name as the SearchTerm while Contains and Tag will be used
                // for filtering on the results.
                if (!String.IsNullOrWhiteSpace(name) && WildcardPattern.ContainsWildcardCharacters(name)) 
                {
                    isNameContainsWildCard = true;

                    // NuGet does not support PowerShell/POSIX style wildcards and supports only '*' in searchTerm 
                    // Replace the range from '[' - to ']' with * and ? with * then wildcard pattern is applied on the results
                    var tempName = name;
                    var squareBracketPattern = Regex.Escape("[") + "(.*?)]";
                    foreach (Match match in Regex.Matches(tempName, squareBracketPattern)) {
                        tempName = tempName.Replace(match.Value, "*");
                    }

                    //As the nuget does not support wildcard, we remove '?', '*' wildcards from the search string, and then
                    //looking for the longest string in the given string as a keyword for the searching in the repository.
                    //A sample case will be something like find-package sql*Compact*.

                    //When the AllVersions property exists, the query like the following containing the wildcards does not work. We need to remove the wild cards and
                    //replace it with the longest string searhc.
                    //http://www.powershellgallery.com/api/v2/Search()?$orderby=DownloadCount%20desc,Id&searchTerm='tsdprovi*'&targetFramework=''&includePrerelease=false

                    if ((!String.IsNullOrWhiteSpace(name) && source.Location.IndexOf("powershellgallery.com", StringComparison.OrdinalIgnoreCase) == -1)
                        || (AllVersions.Value))
                    {
                        //get rid of wildcard and search the longest string in the given name for nuget.org
                        tempName = tempName.Split('?', '*').OrderBy(namePart => namePart.Length).Last();
                    }

                    //Deal with a case when a user type Find-Package *
                    if (String.Equals(tempName, "*", StringComparison.OrdinalIgnoreCase)) {
                        //We use '' instead of "*" because searchterm='*' does not return the entire repository.
                        tempName = string.Empty;
                    }

                    searchTerm = tempName;
                }

                // add Filters to the search
                if (SearchFilter != null)
                {
                    foreach (var filter in SearchFilter.Value)
                    {
                        string key;
                        string value;

                        if (ParseKeyValueString(filter, out key, out value))
                        {
                            searchTerm = searchTerm + key + ":" + value + " ";
                        }
                        else
                        {
                            this.Warning(Messages.SearchFilterIgnored, filter);
                        }
                    }
                }

                Debug(Resources.Messages.SearchingRepository, source.Repository.Source, searchTerm);

                // Handling case where a user does not provide Name parameter
                var pkgs = source.Repository.Search(searchTerm, this);

                if (!String.IsNullOrWhiteSpace(name))
                {
                    //Filter on the results. This is needed because we replace [...] regex in the searchterm at the begining of this method.
                    pkgs = FilterOnName(pkgs, name, isNameContainsWildCard);
                }

                pkgs = FilterOnContains(pkgs);   

                var pkgsItem = pkgs.Select(pkg => new PackageItem
                   {
                       Package = pkg,
                       PackageSource = source,
                       FastPath = MakeFastPath(source, pkg.Id, pkg.Version.ToString())
                   });

                return pkgsItem;
            }
            catch (Exception e)
            {
                e.Dump(this);
                Warning(e.Message);
                return Enumerable.Empty<PackageItem>();
            }
        }

        /// <summary>
        /// Parses Name=Value parameters that should be replaced once OneGet supports Hashtable DynamicOptions
        /// </summary>
        /// <param name="parameter"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        private bool ParseKeyValueString(string parameter, out string key, out string value)
        {
            if (!String.IsNullOrEmpty(parameter))
            {
                var parameterSplit = parameter.Split(new string[] { "=" }, 2, StringSplitOptions.RemoveEmptyEntries);

                // ignore wrong entries
                if (parameterSplit.Count() == 2)
                {
                    key = parameterSplit[0];
                    value = parameterSplit[1];
                    return true;
                }
            }

            key = null;
            value = null;
            return false;
        }
    }
}