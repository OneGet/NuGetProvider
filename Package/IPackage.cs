namespace Microsoft.PackageManagement.NuGetProvider 
{
    using System;
    using System.Collections.Generic;

    public interface IPackage : IPackageName
    {
        //NugetClient:IPackage
        Uri ReportAbuseUrl { get; }
        long DownloadCount { get; }
        bool IsAbsoluteLatestVersion { get; }
        bool IsLatestVersion { get; }
        bool Listed { get; }
        DateTimeOffset? Published { get; }
        string FullFilePath { get; set; }

        //NugetClient:IPackageMetadata
        string Title { get; }
        IEnumerable<string> Authors { get; }
        IEnumerable<string> Owners { get; }
        Uri IconUrl { get; }
        Uri LicenseUrl { get; }
        Uri ProjectUrl { get; }
        bool RequireLicenseAcceptance { get; }
        bool DevelopmentDependency { get; }
        string Description { get; }
        string Summary { get; }
        string ReleaseNotes { get; }
        string Language { get; }
        string Tags { get; }
        string Copyright { get; }
        Version MinClientVersion { get; }
        List<PackageDependencySet> DependencySetList { get; }
        string PackageHash { get; }
        string PackageHashAlgorithm { get; }
    }
}