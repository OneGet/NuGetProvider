namespace Microsoft.PackageManagement.NuGetProvider  
{
    using System;
    using System.Text.RegularExpressions;

    internal static class Constants
    {
        #region copy common-constants-implementation /internal/public

        internal const string MinVersion = "0.0.0.1";
        internal const string MSGPrefix = "MSG:";
        internal const string Download = "Download";
        internal const string Install = "Install";
        public readonly static string[] FeaturePresent = new string[0];
        internal const string CurrentUser = "CurrentUser";
        internal const string AllUsers = "AllUsers";
        internal const string SearchPageCount = "200";
        internal const int SearchPageCountInt = 200;
        internal const string PackageIdTemplateParameter = "{id}";
        internal const string PackageVersionTemplateParameter = "{version}";
        internal const string PackageIdLowerTemplateParameter = "{id-lower}";
        internal const string PackageVersionLowerTemplateParameter = "{version-lower}";
        internal const string PackageIdQueryParam = "id";
        internal const string PrereleaseQueryParam = "prerelease";
        internal const string QueryQueryParam = "q";
        internal const string TakeQueryParam = "take";
        internal const string SkipQueryParam = "skip";
        /// <summary>
        /// {0} = base URL
        /// {1} = id lower
        /// {2} = version lower
        /// {3} = / or empty, depending on last character of {0}
        /// </summary>
        internal const string NuGetDownloadUriTemplate = "{0}{3}{1}/{2}/{1}.{2}.nupkg";
        internal static readonly Regex MyGetFeedRegex = new Regex("myget.org/F/(.*?)/api/v3/index.json");
        /// <summary>
        /// {0} = Scheme
        /// {1} = Host
        /// {2} = Feed name
        /// </summary>
        internal const string MyGetGalleryUriTemplate = "{0}://{1}/feed/{2}/package/nuget/{{id-lower}}/{{version-lower}}";
        internal const string NuGetGalleryUriTemplate = "https://www.nuget.org/packages/{id-lower}/{version-lower}";
        internal const string DummyPackageId = "FoooBarr";
        internal const string PackageServiceType = "PackageDisplayMetadataUriTemplate";
        internal const string PackageVersionServiceType = "PackageVersionDisplayMetadataUriTemplate";
        internal const string RegistrationBaseServiceType = "RegistrationsBaseUrl";
        internal const string SearchQueryServiceType = "SearchQueryService";
        internal const string PackageBaseAddressType = "PackageBaseAddress";
        internal const string ReportAbuseAddressType = "ReportAbuseUriTemplate";
        internal const string AutocompleteAddressType = "SearchAutocompleteService";
        internal const string TagQueryParam = "tag";
        internal const string DescriptionQueryParam = "description";
        internal const string NuGetOrgHost = "nuget.org";
        internal const string MyGetOrgHost = "myget.org";
        internal const string XmlStartContent = "<?xml";
        /// <summary>
        /// {0} = base URL for registrations
        /// {1} = / or empty
        /// {2} = package id lowercase
        /// </summary>
        internal const string NuGetRegistrationUrlTemplatePackage = "{0}{1}{2}/index.json";
        /// <summary>
        /// {0} = base URL for registrations
        /// {1} = / or empty
        /// {2} = package id lowercase
        /// {3} = package version lowercase
        /// </summary>
        internal const string NuGetRegistrationUrlTemplatePackageVersion = "{0}{1}{2}/{3}.json";
        internal const string SemVerLevelQueryParam = "semverlevel";
        internal const string SemVerLevel2 = "2.0.0";
        /// <summary>
        /// {0} = base URL for blob store
        /// {1} = / or empty
        /// {2} = package id
        /// </summary>
        internal const string VersionIndexTemplate = "{0}{1}{2}/index.json";
        internal const int DefaultRetryCount = 3;
        internal static readonly Func<int, int> SimpleBackoffStrategy = (int oldSleep) => { if (oldSleep <= 0) { return 1000; } else { return oldSleep * 2; } };
        internal const int DefaultExtraPackageCount = 5;
        //public static string[] Empty = new string[0];

        internal static class Features
        {
            public const string AutomationOnly = "automation-only";
            public const string MagicSignatures = "magic-signatures";
            public const string SupportedExtensions = "file-extensions";
            public const string SupportedSchemes = "uri-schemes";
            public const string SupportsPowerShellModules = "supports-powershell-modules";
            public const string SupportsRegexSearch = "supports-regex-search";
            public const string SupportsSubstringSearch = "supports-substring-search";
            public const string SupportsWildcardSearch = "supports-wildcard-search";
        }

        internal static class Messages
        {
            public const string UnableToFindDependencyPackage = "MSG:UnableToFindDependencyPackage";
            public const string DependentPackageFailedInstallOrDownload = "MSG:DependentPackageFailedInstallOrDownload";
            public const string MissingRequiredParameter = "MSG:MissingRequiredParameter";
            public const string PackageFailedInstallOrDownload = "MSG:PackageFailedInstallOrDownload";
            public const string PackageSourceExists = "MSG:PackageSourceExists";
            public const string ProtocolNotSupported = "MSG:ProtocolNotSupported";
            public const string ProviderPluginLoadFailure = "MSG:ProviderPluginLoadFailure";
            public const string ProviderSwidtagUnavailable = "MSG:ProviderSwidtagUnavailable";
            public const string RemoveEnvironmentVariableRequiresElevation = "MSG:RemoveEnvironmentVariableRequiresElevation";
            public const string SchemeNotSupported = "MSG:SchemeNotSupported";
            public const string SourceLocationNotValid = "MSG:SourceLocationNotValid";
            public const string UnableToCopyFileTo = "MSG:UnableToCopyFileTo";
            public const string UnableToCreateShortcutTargetDoesNotExist = "MSG:UnableToCreateShortcutTargetDoesNotExist";
            public const string UnableToDownload = "MSG:UnableToDownload";
            public const string UnableToOverwriteExistingFile = "MSG:UnableToOverwriteExistingFile";
            public const string UnableToRemoveFile = "MSG:UnableToRemoveFile";
            public const string UnableToResolvePackage = "MSG:UnableToResolvePackage";
            public const string UnableToResolveSource = "MSG:UnableToResolveSource";
            public const string UnableToUninstallPackage = "MSG:UnableToUninstallPackage";
            public const string UnknownFolderId = "MSG:UnknownFolderId";
            public const string UnknownProvider = "MSG:UnknownProvider";
            public const string UnsupportedArchive = "MSG:UnsupportedArchive";
            public const string UnsupportedProviderType = "MSG:UnsupportedProviderType";
            public const string UriSchemeNotSupported = "MSG:UriSchemeNotSupported";
            public const string UserDeclinedUntrustedPackageInstall = "MSG:UserDeclinedUntrustedPackageInstall";
            public const string HashNotFound = "MSG:HashNotFound";
            public const string HashNotMatch = "MSG:HashNotMatch";
            public const string HashNotSupported = "MSG:HashNotSupported";
            public const string DependencyLoopDetected = "MSG:DependencyLoopDetected";
            public const string CouldNotGetResponseFromQuery = "MSG:CouldNotGetResponseFromQuery";
            public const string SkippedDownloadedPackage = "MSG:SkippedDownloadedPackage";
            public const string InstallRequiresCurrentUserScopeParameterForNonAdminUser = "MSG:InstallRequiresCurrentUserScopeParameterForNonAdminUser";
            
        }

        internal static class OptionType {
            public const string String = "String";
            public const string StringArray = "StringArray";
            public const string Int = "Int";
            public const string Switch = "Switch";
            public const string Folder = "Folder";
            public const string File = "File";
            public const string Path = "Path";
            public const string Uri = "Uri";
            public const string SecureString = "SecureString";
        }

        internal static class PackageStatus
        {
            public const string Available = "Available";
            public const string Dependency = "Dependency";
            public const string Installed = "Installed";
            public const string Uninstalled = "Uninstalled";
        }

        internal static class Parameters
        {
            public const string IsUpdate = "IsUpdatePackageSource";
            public const string Name = "Name";
            public const string Location = "Location";
        }

        internal static class Signatures
        {
            public const string Cab = "4D534346";
            public const string OleCompoundDocument = "D0CF11E0A1B11AE1";
            public const string Zip = "504b0304";
            //public static string[] ZipVariants = new[] {Zip, /* should have EXEs? */};
        }

        internal static class SwidTag
        {
            public const string SoftwareIdentity = "SoftwareIdentity";
        }

        #endregion
    }
}
