namespace Microsoft.PackageManagement.NuGetProvider 
{
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.PackageManagement.Provider.Utility;

    public interface IPackageRepository
    {
        /// <summary>
        /// Gets the resources for this package repository.
        /// </summary>
        INuGetResourceCollection ResourceProvider { get; }

        /// <summary>
        /// Package source location
        /// </summary>
        string Source { get; }

        /// <summary>
        /// True if a file repository.
        /// </summary>
        bool IsFile { get; }

        /// <summary>
        /// Finds packages that match the exact Id and version.
        /// </summary>
        /// <returns>The package if found, null otherwise.</returns>
        IPackage FindPackage(NuGetSearchContext findContext, NuGetRequest request); 

        /// <summary>
        /// Returns a sequence of packages with the specified id.
        /// </summary>
        NuGetSearchResult FindPackagesById(NuGetSearchContext findContext, NuGetRequest request);

        /// <summary>
        /// Nuget V2 metadata supports a method 'Search'. It takes three parameters, searchTerm, targetFramework, and includePrerelease.
        /// </summary>
        /// <param name="searchTerm">search uri</param>
        /// <param name="request"></param>
        /// <returns></returns>
        NuGetSearchResult Search(NuGetSearchContext searchContext,  NuGetRequest request);

        /// <summary>
        /// Download a package from this repository.
        /// </summary>
        /// <param name="packageView">PublicObjectView containing PackageItem object to download.</param>
        /// <param name="destination">Location to download package to.</param>
        /// <param name="request">Currently executing request.</param>
        /// <returns>True if package download was successful; false otherwise.</returns>
        bool DownloadPackage(PublicObjectView packageView, string destination, NuGetRequest request);

        /// <summary>
        /// Install a package from this repository.
        /// </summary>
        /// <param name="packageView">PublicObjectView containing PackageItem object to download.</param>
        /// <param name="request">Currently executing request.</param>
        /// <returns>True if package install was successful; false otherwise.</returns>
        bool InstallPackage(PublicObjectView packageView, NuGetRequest request);
    }
}