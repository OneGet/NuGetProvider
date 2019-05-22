using System.Security.AccessControl;
using Microsoft.PackageManagement.Internal.Utility.Platform;

namespace Microsoft.PackageManagement.NuGetProvider
{
    using System;
    using System.Text;
    using System.Net.Http;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Management.Automation;
    using System.Text.RegularExpressions;
    using System.Xml;
    using System.Xml.Linq;
    using System.Collections.Concurrent;
    using System.Diagnostics.CodeAnalysis;
    using Resources;
    using System.Security.Cryptography;
    using System.Security;
    using System.Runtime.InteropServices;
    using System.Net;
    using SemanticVersion = Microsoft.PackageManagement.Provider.Utility.SemanticVersion;
    using Microsoft.PackageManagement.Provider.Utility;
    using Microsoft.PackageManagement.Internal.Utility.Platform;
    using System.Diagnostics;

    /// <summary> 
    /// This class drives the Request class that is an interface exposed from the PackageManagement Platform to the provider to use.
    /// </summary>
    public abstract class NuGetRequest : Request
    {
        private static readonly Regex _regexFastPath = new Regex(@"\$(?<source>[\w,\+,\/,=]*)\\(?<id>[\w,\+,\/,=]*)\\(?<version>[\w,\+,\/,=]*)\\(?<sources>[\w,\+,\/,=]*)(\\(?<powershellget>[\w,\+,\/,=]*))?");

        private static readonly byte[] _nugetBytes = Encoding.UTF8.GetBytes("NuGet");
        private string _configurationFileLocation;
        private XDocument _config;

        internal readonly Lazy<bool> AllowPrereleaseVersions;
        internal readonly Lazy<bool> AllVersions;
        internal readonly Lazy<string> Contains;
        internal readonly Lazy<bool> ExcludeVersion;
        internal readonly Lazy<string[]> FilterOnTag;
        internal readonly Lazy<string[]> Headers;
        internal readonly Lazy<string> Scope;
        internal readonly Lazy<PackageBase[]> InstalledPackages;

        private static IDictionary<string, PackageSource> _registeredPackageSources;

        private static IDictionary<string, PackageSource> _checkedUnregisteredPackageSources = new ConcurrentDictionary<string, PackageSource>();

        private string _destinationPath = null;

        internal Lazy<bool> SkipValidate;  //??? Seems to be a design choice. Why let a user to decide?
        // we cannot enable skipdepedencies because this will break downlevel psget which sets skipdependencies to true
        internal Lazy<bool> SkipDependencies;
        //internal ImplictLazy<bool> ContinueOnFailure;
        //internal ImplictLazy<bool> FindByCanonicalId;

        private HttpClient _httpClient;
        private HttpClient _httpClientWithoutAcceptHeader;
        private bool? _isCalledFromPowerShellGet;
        private string _CredentialUsername;
        private SecureString _CredentialPassword;

        public HttpClient SetHttpClient (HttpClient client)
        {
            return _httpClient = client;
        }

        public override string CredentialUsername
        {
            get { return _CredentialUsername; }
            set { _CredentialUsername = value; }
        }

        public override SecureString CredentialPassword
        {
            get { return _CredentialPassword; }
            set { _CredentialPassword = value; }
        }

        internal const string DefaultConfig = @"<?xml version=""1.0""?>
<configuration>
  <packageSources>
  </packageSources>
</configuration>";

        /// <summary>
        /// Ctor required by the PackageManagement Platform
        /// </summary>
        protected NuGetRequest()
        {
            FilterOnTag = new Lazy<string[]>(() => (GetOptionValues("FilterOnTag") ?? new string[0]).ToArray());
            Contains = new Lazy<string>(() => GetOptionValue("Contains"));
            ExcludeVersion = new Lazy<bool>(() => GetOptionValue("ExcludeVersion").IsTrue());
            AllowPrereleaseVersions = new Lazy<bool>(() => GetOptionValue("AllowPrereleaseVersions").IsTrue());
            AllVersions = new Lazy<bool>(() => GetOptionValue("AllVersions").IsTrue());

            SkipValidate = new Lazy<bool>(() => GetOptionValue("SkipValidate").IsTrue());
            Scope = new Lazy<string>(() => GetOptionValue("Scope"));

            SkipDependencies = new Lazy<bool>(() => GetOptionValue("SkipDependencies").IsTrue());
            //ContinueOnFailure = new ImplictLazy<bool>(() => GetOptionValue("ContinueOnFailure").IsTrue());           
            //FindByCanonicalId = new ImplictLazy<bool>(() => GetOptionValue("FindByCanonicalId").IsTrue());

            Headers = new Lazy<string[]>(() => (GetOptionValues("Headers") ?? new string[0]).ToArray());
            InstalledPackages = new Lazy<PackageBase[]>(() => (GetInstalledPackagesOptionValue()).ToArray());
        }

        // parse the list of installed packages
        private IEnumerable<PackageBase> GetInstalledPackagesOptionValue()
        {
            // get possible installed packages
            var installedPackages = GetOptionValues("InstalledPackages") ?? new string[0];

            // parse the installed package options passed in
            foreach (var installedPackage in installedPackages)
            {
                // we assume that the name passed in is something like jquery#1.2.5
                string[] nameAndVersion = installedPackage.Split(new[] { "!#!" }, StringSplitOptions.None);

                var package = new PackageBase();

                // only name passed in, no version
                if (nameAndVersion.Count() == 1)
                {
                    package.Id = nameAndVersion[0];
                    yield return package;
                }
                else if (nameAndVersion.Count() == 2)
                {
                    // this means there is version
                    SemanticVersion semVers = null;

                    try
                    {
                        // convert version to semvers
                        semVers = new SemanticVersion(nameAndVersion[1]);
                    }
                    catch
                    {
                        // not a valid version, ignores this entry
                        continue;
                    }

                    // set name and version of this installed packages
                    package.Id = nameAndVersion[0];
                    package.Version = semVers.Version.ToStringSafe();

                    yield return package;
                }
            }
        }

        /// <summary>
        /// Package sources
        /// </summary>
        internal string[] OriginalSources { get; set; }

        /// <summary>
        /// Package installation location used by get-installedpackages.
        /// </summary>
        internal IEnumerable<string> InstalledPath
        {
            get
            {
                var path = GetOptionValue("Destination");

                if (!string.IsNullOrWhiteSpace(path))
                {
                    yield return Path.GetFullPath(path);
                }
                else
                {
                    // If a user does not specify -destination, we will look into the default locations.
                    yield return AllUserDefaultInstallLocation;
                    yield return CurrentUserDefaultInstallLocation;
                }
            }
        }

        /// <summary>
        /// Package destination path
        /// </summary>
        internal string Destination
        {
            get
            {
                if (_destinationPath != null)
                {
                    return _destinationPath;
                }
                var path = GetOptionValue("Destination");

                if (!string.IsNullOrWhiteSpace(path))
                {
                    _destinationPath = Path.GetFullPath(path);
                    return _destinationPath;
                }

                // If a user does not give -destination for the install, we put the package under $env:USERPROFILE\PackageManagement\nuget\packages folder
                // or  $env:programfiles\PackageManagement\NuGet\Packages\  if you are an admin.
                try
                {
                    string basePath;

                    var scope = (Scope == null) ? null : Scope.Value;
                    scope = string.IsNullOrWhiteSpace(scope) ? Constants.AllUsers : scope;

                    if (scope.EqualsIgnoreCase(Constants.CurrentUser))
                    {
                        // Does not matter whether elevated or not
                        basePath = CurrentUserDefaultInstallLocation;
                    }
                    else if (ProviderServices.IsElevated)
                    {
                        //Scope=AllUser or No Scope but elevated
                        basePath = AllUserDefaultInstallLocation;
                    }
                    else
                    {
                        //Scope=AllUser but not elevated
                        WriteError(ErrorCategory.InvalidOperation, ErrorCategory.InvalidOperation.ToString(),
                            Constants.Messages.InstallRequiresCurrentUserScopeParameterForNonAdminUser,
                            AllUserDefaultInstallLocation, CurrentUserDefaultInstallLocation);
                        return string.Empty;
                    }

                    if (!Directory.Exists(basePath))
                    {
                        CreateDirectoryInternal(basePath);
                    }
                    _destinationPath = basePath;
                    return basePath;
                }
                catch (Exception e)
                {
                    e.Dump(this);
                    WriteError(ErrorCategory.InvalidArgument, "Destination", Constants.Messages.MissingRequiredParameter,
                        "Destination");
                    return string.Empty;
                }
            }
        }

        internal void CreateDirectoryInternal(string dirPath)
        {
            try
            {
                if (!Directory.Exists(dirPath))
                {
                    Directory.CreateDirectory(dirPath);
                }
            }
            catch (System.UnauthorizedAccessException ex)
            {
                if (OSInformation.IsWindows)
                {
                    //a user specifies 'AllUsers' that requires Admin privilege. However his console gets launched by non-elevated.
                    WriteError(ErrorCategory.InvalidOperation, ErrorCategory.InvalidOperation.ToString(),
                        Resources.Messages.InstallRequiresCurrentUserScopeParameterForNonAdminUser,
                        dirPath, CurrentUserDefaultInstallLocation);
                }
                else
                {
                    WriteError(ErrorCategory.InvalidOperation, ErrorCategory.InvalidOperation.ToString(),
                        Resources.Messages.InstallRequiresCurrentUserScopeParameterForNonSudoUser,
                        dirPath, CurrentUserDefaultInstallLocation);
                }
                Verbose(ex.Message);
                throw;
            }
        }
        internal string CurrentUserDefaultInstallLocation
        {
            get
            {
                var path = Path.Combine(OSInformation.LocalAppDataDirectory, "PackageManagement", "NuGet", "Packages");
                Debug("CurrentUserDefaultInstallLocation: {0}", path);
                return path;
            }
        }

        internal string AllUserDefaultInstallLocation
        {
            get
            {
                var path = Path.Combine(OSInformation.ProgramFilesDirectory, "PackageManagement", "NuGet", "Packages");
                Debug("AllUserDefaultInstallLocation: {0}", path);
                return path;
            }
        }


        /// <summary>
        /// Get the PackageItem object from the fast path
        /// </summary>
        /// <param name="fastPath"></param>
        /// <returns></returns>
        internal PackageItem GetPackageByFastpath(string fastPath)
        {
            Debug(Resources.Messages.DebugInfoCallMethod3, "NuGetRequest", "GetPackageByFastpath", fastPath);

            string sourceLocation;
            string id;
            string version;
            string[] sources;

            if (TryParseFastPath(fastPath, out sourceLocation, out id, out version, out sources))
            {
                var source = ResolvePackageSource(sourceLocation);

                if (source.IsSourceAFile)
                {
                    return GetPackageByFilePath(sourceLocation);
                }

                // repository should not be null if source is not a file
                if (source.Repository == null)
                {
                    return null;
                }

                // Have to find package again to get possible dependencies
                var pkg = source.Repository.FindPackage(new NuGetSearchContext()
                {
                    PackageInfo = new PackageEntryInfo(id),
                    RequiredVersion = new SemanticVersion(version)
                }, this);

                // only finds the pkg if it is a file. so we don't return it here
                // otherwise we make another download request
                return new PackageItem
                {
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
            try
            {

                if (NuGetPathUtility.IsManifest(filePath))
                {
                    //.nuspec 
                    package = PackageUtility.ProcessNuspec(filePath);

                }
                else if (NuGetPathUtility.IsPackageFile(filePath))
                {
                    //.nupkg or .zip
                    //The file name may contains version.  ex: jQuery.2.1.1.nupkg
                    package = PackageUtility.DecompressFile(filePath, packageName);

                }
                else
                {
                    Warning(Resources.Messages.InvalidFileExtension, filePath);

                }

            }
            catch (Exception ex)
            {
                ex.Dump(this);
            }

            if (package == null)
            {
                return null;
            }

            var source = ResolvePackageSource(filePath);

            return new PackageItem
            {
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
        internal PackageItem GetPackageByFilePath(string filePath)
        {
            Debug(Resources.Messages.DebugInfoCallMethod3, "NuGetRequest", "GetPackageByFilePath", filePath);
            var packageName = Path.GetFileNameWithoutExtension(filePath);

            var pkgItem = GetPackageByFilePath(filePath, packageName);

            return pkgItem;
        }

        /// <summary>
        /// Unregister the package source
        /// </summary>
        /// <param name="id">package source id or name</param>
        internal void RemovePackageSource(string id)
        {
            Debug(Resources.Messages.DebugInfoCallMethod3, "NuGetRequest", "RemovePackageSource", id);
            var config = Config;
            if (config == null)
            {
                return;
            }

            try
            {

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
                if (nodes == null)
                {
                    return;
                }

                foreach (XElement node in nodes)
                {

                    if (node.Attribute("key") != null && String.Equals(node.Attribute("key").Value, id, StringComparison.OrdinalIgnoreCase))
                    {
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

                if (_checkedUnregisteredPackageSources.ContainsKey(id))
                {
                    _checkedUnregisteredPackageSources.Remove(id);
                }


                //var source = config.SelectNodes("/configuration/packageSources/add").Cast<XmlNode>().FirstOrDefault(node => String.Equals(node.Attributes["key"].Value, id, StringComparison.CurrentCultureIgnoreCase));

                //if (source != null)
                //{
                //    source.ParentNode.RemoveChild(source);
                //    Config = config;
                //    Verbose(Resources.Messages.RemovedPackageSource, id);
                //}
            }
            catch (Exception ex)
            {
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
        internal void AddPackageSource(string name, string location, bool isTrusted, bool isValidated)
        {

            Debug(Resources.Messages.DebugInfoCallMethod, "NuGetRequest", string.Format(CultureInfo.InvariantCulture, "AddPackageSource - name= {0}, location={1}", name, location));
            try
            {
                // here the source is already validated by the caller
                var config = Config;
                if (config == null)
                {
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
                        Request = this,
                        Name = name,
                        Location = location,
                        Trusted = isTrusted,
                        IsRegistered = true,
                        IsValidated = isValidated,
                    });
                }

            }
            catch (Exception ex)
            {
                ex.Dump(this);
            }
        }

        /// <summary>
        /// Check if the package source location is valid
        /// </summary>
        /// <param name="location"></param>
        /// <returns></returns>
        internal bool ValidateSourceLocation(string location)
        {

            Debug(Resources.Messages.DebugInfoCallMethod3, "NuGetRequest", "ValidateSourceLocation", location);
            //Handling http: or file: cases
            if (Uri.IsWellFormedUriString(location, UriKind.Absolute))
            {
                return NuGetPathUtility.ValidateSourceUri(SupportedSchemes, new Uri(location), this);
            }
            try
            {
                //UNC or local file
                if (Directory.Exists(location) || File.Exists(location))
                {
                    return true;
                }
            }
            catch
            {
            }
            return false;
        }

        /// <summary>
        /// Supported package source schemes by this provider
        /// </summary>
        internal static IEnumerable<string> SupportedSchemes
        {
            get
            {
                return NuGetProvider.Features[Constants.Features.SupportedSchemes];
            }
        }

        /// <summary>
        /// Return the package source object
        /// </summary>
        /// <param name="name">The package source name to search for</param>
        /// <returns>package source object</returns>
        internal PackageSource FindRegisteredSource(string name)
        {

            Debug(Resources.Messages.DebugInfoCallMethod3, "NuGetRequest", "FindRegisteredSource", name);
            var srcs = RegisteredPackageSources;

            if (srcs == null)
            {
                return null;
            }
            if (srcs.ContainsKey(name))
            {
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
        internal bool YieldPackages(IEnumerable<PackageItem> packageReferences, string searchKey)
        {
            var foundPackage = false;
            if (packageReferences == null)
            {
                return false;
            }

            Debug(Resources.Messages.Iterating, searchKey);

            IEnumerable<PackageItem> packageItems = packageReferences;

            int count = 0;

            foreach (var pkg in packageItems)
            {
                foundPackage = true;
                try
                {
                    if (!YieldPackage(pkg, searchKey))
                    {
                        break;
                    }

                    count++;
                }
                catch (Exception e)
                {
                    e.Dump(this);
                    return false;
                }
            }

            Verbose(Resources.Messages.TotalPackageYield, count, searchKey);
            Debug(Resources.Messages.CompletedIterating, searchKey);

            return foundPackage;
        }

        private string MakeTagId(PackageItem pkg)
        {
            if (pkg == null || pkg.Package == null)
            {
                return string.Empty;
            }

            // the tag id will look like this zlib#1.2.8.8#Jean-loup Gailly;Mark Adler
            return string.Format(CultureInfo.CurrentCulture, "{0}#{1}", pkg.Package.Id, pkg.Package.Version.ToString());
        }

        internal bool IsCalledFromPowerShellGet
        {
            get
            {
                // not initialized yet
                if (!_isCalledFromPowerShellGet.HasValue)
                {
                    _isCalledFromPowerShellGet = Headers.Value.Any(header => header.StartsWith("PSGalleryClientVersion=", StringComparison.OrdinalIgnoreCase));
                }

                return _isCalledFromPowerShellGet.Value;
            }
            set
            {
                _isCalledFromPowerShellGet = true;
            }
        }


        /// <summary>
        /// HttpClient with Accept-CharSet and Accept-Encoding Header
        /// We want to reuse HttpClient
        /// </summary>
        internal HttpClient Client
        {
            get
            {
                if (_httpClient == null)
                {
                    _httpClient = PathUtility.GetHttpClientHelper(CredentialUsername, CredentialPassword, WebProxy);

                    _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Charset", "UTF-8");
                    // Request for gzip and deflate encoding to make the response lighter.
                    _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip,deflate");

                    foreach (var header in Headers.Value)
                    {
                        // header is in the format "A=B" because OneGet doesn't support Dictionary parameters
                        if (!String.IsNullOrEmpty(header))
                        {
                            var headerSplit = header.Split(new string[] { "=" }, 2, StringSplitOptions.RemoveEmptyEntries);

                            // ignore wrong entries
                            if (headerSplit.Count() == 2)
                            {
                                _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(headerSplit[0], headerSplit[1]);
                            }
                            else
                            {
                                Warning(Messages.HeaderIgnored, header);
                            }
                        }
                    }
                }

                return _httpClient;
            }
        }

        /// <summary>
        /// HttpClient without any Accept header (this will only have User-Agent header)
        /// </summary>
        internal HttpClient ClientWithoutAcceptHeader
        {
            get
            {
                if (_httpClientWithoutAcceptHeader == null)
                {
                    _httpClientWithoutAcceptHeader = PathUtility.GetHttpClientHelper(CredentialUsername, CredentialPassword, WebProxy);
                }

                return _httpClientWithoutAcceptHeader;
            }
        }

        /// <summary>
        /// Communicate to the PackageManagement platform about the package info
        /// </summary>
        /// <param name="pkg"></param>
        /// <param name="searchKey"></param>
        /// <param name="destinationPath"></param>
        /// <returns></returns>
        internal bool YieldPackage(PackageItem pkg, string searchKey, string destinationPath = null)
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

                    // downlevel machine does not have AddTagId interface in request object so it will return null
                    // hence we can't check it here.
                    AddTagId(MakeTagId(pkg));

                    // if we need to report installation location, add a payload and add directory to that
                    if (!string.IsNullOrWhiteSpace(destinationPath))
                    {
                        string payload = AddPayload();

                        if (string.IsNullOrWhiteSpace(payload))
                        {
                            return false;
                        }

                        AddDirectory(payload, Path.GetFileName(destinationPath), Path.GetDirectoryName(destinationPath), null, true);
                    }

                    if (AddMetadata(pkg.FastPath, "copyright", pkg.Package.Copyright) == null)
                    {
                        return false;
                    }
                    if (AddMetadata(pkg.FastPath, "description", pkg.Package.Description) == null)
                    {
                        return false;
                    }
                    if (AddMetadata(pkg.FastPath, "licenseNames", pkg.Package.LicenseNames) == null)
                    {
                        return false;
                    }
                    if (AddMetadata(pkg.FastPath, "requireLicenseAcceptance", pkg.Package.RequireLicenseAcceptance.ToString()) == null)
                    {
                        return false;
                    }
                    if (AddMetadata(pkg.FastPath, "language", pkg.Package.Language) == null)
                    {
                        return false;
                    }
                    if (AddMetadata(pkg.FastPath, "releaseNotes", pkg.Package.ReleaseNotes) == null)
                    {
                        return false;
                    }
                    if (AddMetadata(pkg.FastPath, "isLatestVersion", pkg.Package.IsLatestVersion.ToString()) == null)
                    {
                        return false;
                    }
                    if (AddMetadata(pkg.FastPath, "isAbsoluteLatestVersion", pkg.Package.IsAbsoluteLatestVersion.ToString()) == null)
                    {
                        return false;
                    }
                    if (pkg.Package.MinClientVersion != null && AddMetadata(pkg.FastPath, "minClientVersion", pkg.Package.MinClientVersion.ToString()) == null)
                    {
                        return false;
                    }

                    if (pkg.Package.VersionDownloadCount != -1)
                    {
                        if (AddMetadata(pkg.FastPath, "versionDownloadCount", pkg.Package.VersionDownloadCount.ToString(CultureInfo.CurrentCulture)) == null)
                        {
                            return false;
                        }
                    }

                    if (pkg.Package.DownloadCount != -1)
                    {
                        if (AddMetadata(pkg.FastPath, "downloadCount", pkg.Package.DownloadCount.ToString(CultureInfo.CurrentCulture)) == null)
                        {
                            return false;
                        }
                    }

                    if (pkg.Package.PackageSize != -1)
                    {
                        if (AddMetadata(pkg.FastPath, "packageSize", pkg.Package.PackageSize.ToString(CultureInfo.CurrentCulture)) == null)
                        {
                            return false;
                        }
                    }
                    if (pkg.Package.Published != null)
                    {
                        if (AddMetadata(pkg.FastPath, "published", pkg.Package.Published.ToString()) == null)
                        {
                            return false;
                        }
                    }
                    if (pkg.Package.Created != null)
                    {
                        if (AddMetadata(pkg.FastPath, "created", pkg.Package.Created.ToString()) == null)
                        {
                            return false;
                        }
                    }
                    if (pkg.Package.LastEdited != null)
                    {
                        if (AddMetadata(pkg.FastPath, "lastEdited", pkg.Package.LastEdited.ToString()) == null)
                        {
                            return false;
                        }
                    }
                    if (pkg.Package.LastUpdated != null)
                    {
                        if (AddMetadata(pkg.FastPath, "lastUpdated", pkg.Package.LastUpdated.ToString()) == null)
                        {
                            return false;
                        }
                    }
                    if (AddMetadata(pkg.FastPath, "tags", pkg.Package.Tags) == null)
                    {
                        return false;
                    }
                    if (AddMetadata(pkg.FastPath, "title", pkg.Package.Title) == null)
                    {
                        return false;
                    }
                    if (AddMetadata(pkg.FastPath, "developmentDependency", pkg.Package.DevelopmentDependency.ToString()) == null)
                    {
                        return false;
                    }
                    if (AddMetadata(pkg.FastPath, "FromTrustedSource", pkg.PackageSource.Trusted.ToString()) == null)
                    {
                        return false;
                    }
                    if (pkg.Package.LicenseUrl != null && !String.IsNullOrWhiteSpace(pkg.Package.LicenseUrl.ToString()))
                    {
                        if (AddLink(pkg.Package.LicenseUrl, "license", null, null, null, null, null) == null)
                        {
                            return false;
                        }
                    }
                    if (pkg.Package.ProjectUrl != null && !String.IsNullOrWhiteSpace(pkg.Package.ProjectUrl.ToString()))
                    {
                        if (AddLink(pkg.Package.ProjectUrl, "project", null, null, null, null, null) == null)
                        {
                            return false;
                        }
                    }
                    if (pkg.Package.ReportAbuseUrl != null && !String.IsNullOrWhiteSpace(pkg.Package.ReportAbuseUrl.ToString()))
                    {
                        if (AddLink(pkg.Package.ReportAbuseUrl, "abuse", null, null, null, null, null) == null)
                        {
                            return false;
                        }
                    }
                    if (pkg.Package.IconUrl != null && !String.IsNullOrWhiteSpace(pkg.Package.IconUrl.ToString()))
                    {
                        if (AddLink(pkg.Package.IconUrl, "icon", null, null, null, null, null) == null)
                        {
                            return false;
                        }
                    }
                    if (pkg.Package.GalleryDetailsUrl != null && !String.IsNullOrWhiteSpace(pkg.Package.GalleryDetailsUrl.ToString()))
                    {
                        if (AddLink(pkg.Package.GalleryDetailsUrl, "galleryDetails", null, null, null, null, null) == null)
                        {
                            return false;
                        }
                    }
                    if (pkg.Package.LicenseReportUrl != null && !String.IsNullOrWhiteSpace(pkg.Package.LicenseReportUrl.ToString()))
                    {
                        if (AddLink(pkg.Package.LicenseReportUrl, "licenseReport", null, null, null, null, null) == null)
                        {
                            return false;
                        }
                    }
                    if (pkg.Package.Authors.Any(author => AddEntity(author.Trim(), author.Trim(), "author", null) == null))
                    {
                        return false;
                    }

                    if (pkg.Package.Owners.Any(owner => AddEntity(owner.Trim(), owner.Trim(), "owner", null) == null))
                    {
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
                }
                else
                {
                    return false;
                }
            }
            catch (Exception e)
            {
                e.Dump(this);
                return false;
            }
            return true;
        }

        internal bool MinAndMaxVersionMatched(SemanticVersion packageVersion, string minimumVersion, string maximumVersion, bool minInclusive, bool maxInclusive)
        {
            if (!string.IsNullOrWhiteSpace(minimumVersion))
            {
                // Check whether we are checking for min inclusive version
                if (minInclusive)
                {
                    // version too small, not what we are looking for
                    if (packageVersion < new SemanticVersion(minimumVersion))
                    {
                        return false;
                    }
                }
                else
                {
                    if (packageVersion <= new SemanticVersion(minimumVersion))
                    {
                        return false;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(maximumVersion))
            {
                // Check whether we are checking for max inclusive version
                if (maxInclusive)
                {
                    // version too big, not what we are looking for
                    if (packageVersion > new SemanticVersion(maximumVersion))
                    {
                        return false;
                    }
                }
                else
                {
                    if (packageVersion >= new SemanticVersion(maximumVersion))
                    {
                        return false;
                    }
                }
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
                var paths = InstalledPath.Where(Directory.Exists).ToArray();

                // if directory does not exist then just return false
                if (!paths.Any())
                {
                    return false;
                }

                // look in the destination directory for directories that contain *.nupkg & .nuspec files.  
                var subdirs = paths.SelectMany(Directory.EnumerateDirectories);

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
                                    // if min and max version not matched
                                    if (!MinAndMaxVersionMatched(pkgItem.Package.Version, minimumVersion, maximumVersion, minInclusive, maxInclusive))
                                    {
                                        continue;
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
        /// <param name="isDependency">Whether the package is a dependency</param>
        /// <param name="requiredVersion"></param>
        /// <param name="minimumVersion"></param>
        /// <param name="maximumVersion"></param>
        /// <param name="maxInclusive"></param>
        /// <param name="minInclusive"></param>
        /// <param name="request"></param>
        /// <returns></returns>
        internal IEnumerable<PackageItem> GetPackageById(string name, NuGetRequest request, string requiredVersion = null,
            string minimumVersion = null, string maximumVersion = null, bool minInclusive = true, bool maxInclusive = true, bool isDependency = false)
        {
            if (String.IsNullOrWhiteSpace(name))
            {
                return Enumerable.Empty<PackageItem>();
            }

            return SelectedSources.AsParallel().WithMergeOptions(ParallelMergeOptions.NotBuffered).SelectMany(source => GetPackageById(source, name, request, requiredVersion, minimumVersion, maximumVersion, minInclusive, maxInclusive, isDependency));
        }

        /// <summary>
        /// Encrypting a path containing package source location, id, version and source
        /// </summary>
        /// <param name="source">package source</param>
        /// <param name="id">package id</param>
        /// <param name="version">package version</param>
        /// <returns></returns>
        internal string MakeFastPath(PackageSource source, string id, string version)
        {
            // if this is called from powershellget, append nonupkg at the end
            if (IsCalledFromPowerShellGet)
            {
                return String.Format(CultureInfo.InvariantCulture, @"${0}\{1}\{2}\{3}\{4}", source.Serialized, id.ToBase64(), version.ToBase64(), (Sources ?? new string[0]).Select(each => each.ToBase64()).SafeAggregate((current, each) => current + "|" + each), "powershellget".ToBase64());
            }

            return String.Format(CultureInfo.InvariantCulture, @"${0}\{1}\{2}\{3}", source.Serialized, id.ToBase64(), version.ToBase64(), (Sources ?? new string[0]).Select(each => each.ToBase64()).SafeAggregate((current, each) => current + "|" + each));
        }

        /// <summary>
        /// Return all registered package sources
        /// </summary>
        internal IEnumerable<PackageSource> SelectedSources
        {
            get
            {
                if (IsCanceled)
                {
                    yield break;
                }

                //get sources from user's input
                var sources = (OriginalSources ?? Enumerable.Empty<string>()).Union(PackageSources ?? Enumerable.Empty<string>()).ToArray();
                //get sources from config file that registered earlier 
                var pkgSources = RegisteredPackageSources;

                Debug(Resources.Messages.RegisteredSources, pkgSources.Count, NuGetConstant.ProviderName);

                //If a user does not provider -source, we use the registered ones
                if (sources.Length == 0)
                {
                    // return them all.
                    foreach (var src in pkgSources.Values)
                    {
                        Debug(src.Name);
                        // set the request of the registered one to the current request because it may have additional information like credential
                        src.Request = this;
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
                        _checkedUnregisteredPackageSources[src].Request = this;
                        yield return _checkedUnregisteredPackageSources[src];
                        continue;
                    }

                    // check to see if we have a source with either that name
                    // or that URI first.
                    if (pkgSources.ContainsKey(src))
                    {
                        Debug(Resources.Messages.FoundRegisteredSource, src, NuGetConstant.ProviderName);
                        _checkedUnregisteredPackageSources.Add(src, pkgSources[src]);
                        pkgSources[src].Request = this;
                        yield return pkgSources[src];
                        continue;
                    }

                    var srcLoc = src;
                    var found = false;
                    foreach (var byLoc in pkgSources.Values)
                    {
                        // srcLoc does not match byLoc.Location, try to check for srcLoc with "/" appended at the end
                        if (!string.Equals(byLoc.Location, srcLoc, StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(byLoc.Location, string.Concat(srcLoc, "/"), StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(string.Concat(byLoc.Location, "/"), srcLoc, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        // in this case, do not store the srcLoc into the dictionary, otherwise, the provider may resolve http://www.nuget.org/api/v2 to some source with location
                        // http://www.nuget.org/api/v2/ and oneget will raise an error (it thinks we are dishonest :()
                        byLoc.Location = srcLoc;
                        // set request of byloc to this because this request may have credential information
                        byLoc.Request = this;
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
                                try
                                {
                                    // Only validate once
                                    isValidated = ConcurrentInMemoryCache.Instance.GetOrAdd(String.Format(CultureInfo.InvariantCulture, "SelectedSources:{0}", srcUri.AbsoluteUri), () => NuGetPathUtility.ValidateSourceUri(SupportedSchemes, srcUri, this));
                                }
                                catch (Exception ex)
                                {
                                    ex.Dump(this);
                                }
                            }

                            if (SkipValidate.Value || isValidated)
                            {
                                Debug(Resources.Messages.SuccessfullyValidated, src);

                                yield return new PackageSource
                                {
                                    Request = this,
                                    Location = srcUri.ToString(),
                                    Name = srcUri.ToString(),
                                    Trusted = false,
                                    IsRegistered = false,
                                    IsValidated = isValidated,
                                };
                                continue;
                            }
                            Warning(Constants.Messages.UnableToResolveSource, src);
                            continue;
                        }

                        // Not a valid location?
                        Warning(Constants.Messages.UnableToResolveSource, src);
                        continue;
                    }

                    // is it a file path?
                    if (Directory.Exists(src))
                    {
                        Debug(Resources.Messages.SourceIsADirectory, src);

                        PackageSource newSource = new PackageSource
                        {
                            Request = this,
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

        private XDocument Config
        {
            get
            {
                if (_config == null)
                {
                    Debug(Resources.Messages.LoadingConfigurationFile, ConfigurationFileLocation);

                    XDocument doc = null;

                    try
                    {
                        using (FileStream fs = new FileStream(ConfigurationFileLocation, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            // load the xdocument from the file stream
                            doc = XmlUtility.LoadSafe(fs, ignoreWhiteSpace: true);
                        }

                        Debug(Resources.Messages.LoadedConfigurationFile, ConfigurationFileLocation);

                        if (doc.Root != null && doc.Root.Name != null && String.Equals(doc.Root.Name.LocalName, "configuration", StringComparison.OrdinalIgnoreCase))
                        {
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
            set
            {

                if (value == null)
                {
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

        private string ConfigurationFileLocation
        {
            get
            {
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

                    }
                    else
                    {

                        var appdataFolder = Environment.GetEnvironmentVariable("appdata");
                        if (!OSInformation.IsWindows)
                        {
                            // $Home/.config
                            appdataFolder = OSInformation.ConfigLocation;
                        }

                        _configurationFileLocation = Path.Combine(appdataFolder, "NuGet", NuGetConstant.SettingsFileName);

                        //create directory if does not exist
                        string dir = Path.GetDirectoryName(_configurationFileLocation);
                        if (dir != null && !Directory.Exists(dir))
                        {
                            Debug(Resources.Messages.CreateDirectory, dir);
                            CreateDirectoryInternal(dir);
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
            get
            {
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
                                                Request = this,
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
            NuGetRequest request,
            string requiredVersion = null,
            string minimumVersion = null,
            string maximumVersion = null,
            bool minInclusive = true,
            bool maxInclusive = true,
            bool isDependency = false)
        {
            try
            {
                Debug(Resources.Messages.DebugInfoCallMethod3, "NuGetRequest", "GetPackageById", name);

                // source should be attached to a repository
                if (source.Repository == null)
                {
                    return Enumerable.Empty<PackageItem>();
                }

                // otherwise fall back to traditional behavior
                var findResult = source.Repository.FindPackagesById(new NuGetSearchContext()
                {
                    PackageInfo = new PackageEntryInfo(name),
                    RequiredVersion = String.IsNullOrWhiteSpace(requiredVersion) ? null : new SemanticVersion(requiredVersion),
                    MinimumVersion = String.IsNullOrWhiteSpace(minimumVersion) ? null : new SemanticVersion(minimumVersion),
                    MaximumVersion = String.IsNullOrWhiteSpace(maximumVersion) ? null : new SemanticVersion(maximumVersion),
                    AllowPrerelease = AllowPrereleaseVersions.Value,
                    AllVersions = AllVersions.Value,
                    EnableDeepMetadataBypass = AllVersions.Value, // Bypass deep metadata when allversions is requested
                }, request);
                var pkgs = findResult.Result;

                // exact version is required if required version is not null or empty OR maxversion == minversion and min and max inclusive are true
                bool exactVersionRequired = (!string.IsNullOrWhiteSpace(requiredVersion))
                    || (!string.IsNullOrWhiteSpace(maximumVersion)
                        && !string.IsNullOrWhiteSpace(minimumVersion)
                        && (new SemanticVersion(minimumVersion) == new SemanticVersion(maximumVersion))
                        && minInclusive && maxInclusive);

                // find out if all versions of a package are unlisted
                // unlisted versions will have a published year as 1900 or earlier
                var listedPkgs = pkgs.Where(p => (!p.Published.HasValue || p.Published.Value.Year > 1900));
                var allVersionsUnlisted= listedPkgs.IsNullOrEmpty();

                if (!exactVersionRequired)
                {
                    // if at least one version is listed, return the set of listed packages.
                    // if all verisons are unlisted and package is not a dependency, don't return any packages
                    // if all versions are unlisted and it is a dependency, just return all the packages
                    if (!allVersionsUnlisted)
                    {
                        pkgs = listedPkgs;
                    }
                    else if (allVersionsUnlisted && !isDependency)
                    {
                        pkgs = null;
                    }
                }

                if (AllVersions.Value)
                {
                    //Display versions from lastest to oldest
                    pkgs = (from p in pkgs select p).OrderByDescending(x => x.Version);
                }
                else if (String.IsNullOrWhiteSpace(requiredVersion) && String.IsNullOrWhiteSpace(minimumVersion) && String.IsNullOrWhiteSpace(maximumVersion))
                {

                    if (AllowPrereleaseVersions.Value || source.Repository.IsFile)
                    {
                        //Handling something like Json.NET 7.0.1-beta3 as well as local repository
                        pkgs = from p in pkgs
                               group p by p.Id
                            into newGroup
                               select newGroup.Aggregate((current, next) => (next.Version > current.Version) ? next : current);

                    }
                    else
                    {
                        pkgs = from p in pkgs where (p.IsLatestVersion) select p;
                    }
                }
                else if (!exactVersionRequired && !AllowPrereleaseVersions.Value)
                {
                    // if exact version is not required and allow prerelease is false, we will have to filter out prerelease version
                    pkgs = from p in pkgs where string.IsNullOrWhiteSpace(p.Version.SpecialVersion) select p;
                }

                pkgs = PackageFilterUtility.FilterOnContains(pkgs, Contains.Value);
                if (FilterOnTag != null)
                {
                    pkgs = PackageFilterUtility.FilterOnTags(pkgs, FilterOnTag.Value);
                }

                if (findResult.VersionPostFilterRequired)
                {
                    pkgs = PackageFilterUtility.FilterOnVersion(pkgs, requiredVersion, minimumVersion, maximumVersion, minInclusive, maxInclusive);
                }

                IEnumerable<PackageItem> results = pkgs.Select(pkg => new PackageItem
                {
                    Package = pkg,
                    PackageSource = source,
                    FastPath = MakeFastPath(source, pkg.Id, pkg.Version.ToString())
                });

                return results;
            }
            catch (Exception e)
            {
                e.Dump(this);
                return Enumerable.Empty<PackageItem>();
            }
        }

        private PackageSource ResolvePackageSource(string nameOrLocation)
        {
            Debug(Resources.Messages.DebugInfoCallMethod3, "NuGetRequest", "ResolvePackageSource", nameOrLocation);

            if (IsCanceled)
            {
                return null;
            }

            var source = FindRegisteredSource(nameOrLocation);
            if (source != null)
            {
                Debug(Resources.Messages.FoundRegisteredSource, nameOrLocation, NuGetConstant.ProviderName);
                return source;
            }

            Debug(Resources.Messages.NotFoundRegisteredSource, nameOrLocation, NuGetConstant.ProviderName);

            try
            {
                // is the given value a filename?
                if (File.Exists(nameOrLocation))
                {
                    Debug(Resources.Messages.SourceIsAFilePath, nameOrLocation);

                    return new PackageSource()
                    {
                        Request = this,
                        IsRegistered = false,
                        IsValidated = true,
                        Location = nameOrLocation,
                        Name = nameOrLocation,
                        Trusted = true,
                    };
                }
            }
            catch
            {
            }

            try
            {
                // is the given value a directory?
                if (Directory.Exists(nameOrLocation))
                {
                    Debug(Resources.Messages.SourceIsADirectory, nameOrLocation);
                    return new PackageSource()
                    {
                        Request = this,
                        IsRegistered = false,
                        IsValidated = true,
                        Location = nameOrLocation,
                        Name = nameOrLocation,
                        Trusted = true,
                    };
                }
            }
            catch
            {
            }

            if (Uri.IsWellFormedUriString(nameOrLocation, UriKind.Absolute))
            {
                var uri = new Uri(nameOrLocation, UriKind.Absolute);
                if (!SupportedSchemes.Contains(uri.Scheme.ToLowerInvariant()))
                {
                    WriteError(ErrorCategory.InvalidArgument, uri.ToString(), Constants.Messages.UriSchemeNotSupported, uri);
                    return null;
                }

                // this is an URI, and it looks like one type that we support
                if (SkipValidate.Value || NuGetPathUtility.ValidateSourceUri(SupportedSchemes, uri, this))
                {
                    var uriSource = new PackageSource
                    {
                        Request = this,
                        IsRegistered = false,
                        IsValidated = !SkipValidate.Value,
                        Location = nameOrLocation,
                        Name = nameOrLocation,
                        Trusted = false,
                    };

                    return uriSource;
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

            if (match.Groups["powershellget"].Success && match.Groups["powershellget"].Value.FromBase64().EqualsIgnoreCase("powershellget"))
            {
                IsCalledFromPowerShellGet = true;
            }

            return match.Success;
        }

        private bool LocationCloseEnoughMatch(string givenLocation, string knownLocation)
        {
            if (givenLocation.Equals(knownLocation, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // make trailing slashes consistent
            if (givenLocation.TrimEnd('/').Equals(knownLocation.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // and trailing backslashes
            if (givenLocation.TrimEnd('\\').Equals(knownLocation.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
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

                // no repository found then returns nothing
                if (source.Repository == null)
                {
                    return Enumerable.Empty<PackageItem>();
                }

                var searchTerm = Contains.Value ?? string.Empty;

                List<NuGetSearchTerm> searchTerms = new List<NuGetSearchTerm>();
                // Deal with two cases here:
                // 1. The package name contains wildcards like "Find-Package -name JQ*". We search based on the name.
                // 2. A user does not provide the Name parameter,

                // A user provides name with wildcards. We will use name as the SearchTerm while Contains and Tag will be used
                // for filtering on the results.
                StringBuilder searchTermsDebugMessageBuilder = new StringBuilder();
                if (!String.IsNullOrWhiteSpace(name) && WildcardPattern.ContainsWildcardCharacters(name))
                {
                    if (AllVersions.Value)
                    {
                        WriteError(ErrorCategory.InvalidArgument, name, Messages.AllVersionsSearchNotSupported);
                        return Enumerable.Empty<PackageItem>();
                    }

                    searchTerms.Add(new NuGetSearchTerm(NuGetSearchTerm.NuGetSearchTermType.OriginalPSPattern, name));

                    // NuGet does not support PowerShell/POSIX style wildcards and supports only '*' in searchTerm 
                    // Replace the range from '[' - to ']' with * and ? with * then wildcard pattern is applied on the results
                    var tempName = name;
                    var squareBracketPattern = Regex.Escape("[") + "(.*?)]";
                    foreach (Match match in Regex.Matches(tempName, squareBracketPattern))
                    {
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
                    if (String.Equals(tempName, "*", StringComparison.OrdinalIgnoreCase))
                    {
                        //We use '' instead of "*" because searchterm='*' does not return the entire repository.
                        tempName = string.Empty;
                    }

                    searchTerm = tempName;
                }

                if (!String.IsNullOrWhiteSpace(searchTerm))
                {
                    searchTerms.Add(new NuGetSearchTerm(NuGetSearchTerm.NuGetSearchTermType.SearchTerm, searchTerm));
                    searchTermsDebugMessageBuilder.AppendFormat(CultureInfo.InvariantCulture, "searchTerm:{0}", searchTerm);
                }

                // Add the Tags for the search.
                if (FilterOnTag != null)
                {
                    // E.g. searchTerm = "tag:dscresource_xFirefox tag:command_Start-Process"
                    foreach (string tag in FilterOnTag.Value.Where(tag => !string.IsNullOrWhiteSpace(tag)))
                    {
                        searchTerms.Add(new NuGetSearchTerm(NuGetSearchTerm.NuGetSearchTermType.Tag, tag));
                        searchTermsDebugMessageBuilder.AppendFormat(CultureInfo.InvariantCulture, " tag:{0}", tag);
                    }
                }

                // Add Contains term for search
                if (!string.IsNullOrWhiteSpace(Contains.Value))
                {
                    searchTerms.Add(new NuGetSearchTerm(NuGetSearchTerm.NuGetSearchTermType.Contains, Contains.Value));
                    searchTermsDebugMessageBuilder.AppendFormat(CultureInfo.InvariantCulture, " contains:\"{0}\"", Contains.Value);
                }

                Verbose(Resources.Messages.SearchingRepository, source.Repository.Source, searchTermsDebugMessageBuilder.ToString());

                // Handling case where a user does not provide Name parameter
                NuGetSearchContext searchContext = new NuGetSearchContext()
                {
                    SearchTerms = searchTerms,
                    AllowPrerelease = AllowPrereleaseVersions.Value,
                    AllVersions = AllVersions.Value
                };
                NuGetSearchResult searchResult = source.Repository.Search(searchContext, this);
                var pkgs = searchResult.Result;

                if (!String.IsNullOrWhiteSpace(name) && searchResult.NamePostFilterRequired)
                {
                    //Filter on the results. This is needed because we replace [...] regex in the searchterm at the begining of this method.
                    pkgs = pkgs.Where(p => PackageFilterUtility.IsValidByName(new PackageEntryInfo(p.Id), searchContext));
                }

                if (!String.IsNullOrWhiteSpace(Contains.Value) && searchResult.ContainsPostFilterRequired)
                {
                    pkgs = PackageFilterUtility.FilterOnContains(pkgs, Contains.Value);
                }

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

        internal NetworkCredential GetCredsFromCredProvider(string query, NuGetRequest request, bool isRetry=false)
        {
            request.Debug("Calling 'GetCredsFromCredProvider' on {0}", query);
            if (query.IsNullOrEmpty())
            {
                request.Debug("Query is null.");
            }

            var osPlatform = Environment.OSVersion.Platform;
            string username = "";
            string password = "";
            // Find credential provider
            // Option 1. Use env var 'NUGET_PLUGIN_PATHS' to find credential provider
            // see: https://docs.microsoft.com/en-us/nuget/reference/extensibility/nuget-cross-platform-plugins#plugin-installation-and-discovery
            // Note: OSX and Linux can only use option 1
            string credProviderPath = "";
            // Nuget prioritizes credential providers stored in the NUGET_PLUGIN_PATHS env var
            string defaultEnvPath = "NUGET_PLUGIN_PATHS";
            string nugetPluginPath = Environment.GetEnvironmentVariable(defaultEnvPath);

            if (!nugetPluginPath.IsNullOrEmpty())
            {
                credProviderPath = nugetPluginPath;
            }
            else
            {
                string path = "%UserProfile%/.nuget/plugins/netcore/CredentialProvider.Microsoft/CredentialProvider.Microsoft.dll";
                if (osPlatform == PlatformID.Unix)
                {
                    // If running Unix
                    path = "$HOME/.nuget/plugins/netcore/CredentialProvider.Microsoft/CredentialProvider.Microsoft.dll";
                }
                credProviderPath = Environment.ExpandEnvironmentVariables(path);
            }

            // Option 2. Use Visual Studio path to find credential provider
            // Visual Studio comes pre-installed with the Azure Artifacts credential provider, so we'll search for that file using vswhere.exe
            // If Windows (ie not unix), we'll use vswhere.exe to find installation path of VS
            // If credProviderPath is already set we can skip option 2
            if (credProviderPath.IsNullOrEmpty() && osPlatform != PlatformID.Unix)
            {
                string vswhereExePath = "";
                // Check both Program Files x86, and Program Files, respectively
                string vswhereExePath64 = "%ProgramFiles(x86)%\\Microsoft Visual Studio\\Installer\\vswhere.exe";
                string vswhereExePath32 = "%ProgramFiles%\\Microsoft Visual Studio\\Installer\\vswhere.exe";
                string fullVswhereExePath64 = Environment.ExpandEnvironmentVariables(vswhereExePath64);
                string fullVswhereExePath32 = Environment.ExpandEnvironmentVariables(vswhereExePath32);
                if (File.Exists(vswhereExePath64))
                {
                    // If the env variable exists, check to see if the path itself exists
                    vswhereExePath = fullVswhereExePath64;
                }
                else if (File.Exists(vswhereExePath32))
                {
                    vswhereExePath = fullVswhereExePath32;
                }

                // Using a process to run VsWhere.exe so that we can find the installation path of Visual Studio
                Process process = new Process();
                process.StartInfo.FileName = vswhereExePath;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.CreateNoWindow = false;
                process.StartInfo.UseShellExecute = false;
                string vsInstallationPath = "";
                try
                {
                    process.Start();
                    StreamReader reader = process.StandardOutput;

                    while (!reader.EndOfStream)
                    {
                        string line = reader.ReadLine();
                        if (Regex.IsMatch(line, @"installationPath"))
                        {
                            // Match all chars after 'installationPath:'
                            Match vsInstallPathMatch = Regex.Match(line, @"(?<=\: ).*");
                            vsInstallationPath = vsInstallPathMatch.ToString();
                            break;
                        }
                    }
                }
                catch (Exception e)
                {
                   request.Debug(e.Message);
                }
                finally
                {
                    process.Close();
                }
                // Then use the installation path discovered by vswhere.exe to create the path to search for credential provider
                // ex: "C:\Program Files (x86)\Microsoft Visual Studio\2017\Enterprise" + "\Common7\IDE\CommonExtensions\Microsoft\NuGet\Plugins\CredentialProvider.Microsoft\CredentialProvider.Microsoft.exe"
                if (vsInstallationPath.IsNullOrEmpty())
                {
                    request.Debug("vsInstallationPath is null.");
                }
                credProviderPath = vsInstallationPath + "\\Common7\\IDE\\CommonExtensions\\Microsoft\\NuGet\\Plugins\\CredentialProvider.Microsoft\\CredentialProvider.Microsoft.exe";
            }

            // Using a process to run CredentialProvider.Microsoft.exe with arguments -V verbose -U query (and -IsRetry when appropriate)
            // See: https://github.com/Microsoft/artifacts-credprovider
            Process proc = new Process();
            var filename = credProviderPath;
            var arguments = "-V verbose -U " + query;
             if (osPlatform == PlatformID.Unix)
            {
                filename = "dotnet";
                arguments = credProviderPath + " " + arguments;
            }
            if (isRetry)
            {
                arguments = arguments + " -I ";
            }
            proc.StartInfo.FileName = filename;
            proc.StartInfo.Arguments = arguments;
            // Need to redirect to save tokens
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.CreateNoWindow = false;
            proc.StartInfo.UseShellExecute = false;

            try
            {
                request.Debug("Calling credential provider installed at {0}", credProviderPath);
                proc.Start();
                StreamReader reader = proc.StandardOutput;

                // Write the redirected output to console
                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine();
                    // Need to decide the level of verbosity that should be displayed (or whether a user specified flag should determine this)
                    if (Regex.IsMatch(line, @"Verbose"))
                    {
                        // Verbose will only print if user passes -Verbose
                        request.Verbose(line);
                    }
                    else if (Regex.IsMatch(line, @"Information"))
                    {
                        // Information will never print to console
                        // Username and password is provided in the following format:
                        // "[Information] [CredentialProvider]Username: "
                        if (Regex.IsMatch(line, @"Username"))
                        {
                            // Match all chars after 'Username:'
                            Match usernameMatch = Regex.Match(line, @"(?<=\: ).*");
                            username = usernameMatch.ToString();
                        }
                        else if (Regex.IsMatch(line, @"Password"))
                        {
                            // Match all chars after 'Password:'
                            Match passwordMatch = Regex.Match(line, @"(?<=\: ).*");
                            password = passwordMatch.ToString();
                        }
                    }
                    else{
                        // Minimal or any other output will always print to console
                        Console.WriteLine(line);
                    }
                }
            }
            catch (Exception e)
            {
                request.Debug(e.Message);
            }
            finally
            {
                proc.Close();
            }

            return new NetworkCredential(username, password);
        }
    }
}
