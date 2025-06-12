namespace Microsoft.PackageManagement.NuGetProvider 
{

    public interface IPackageRepositoryFactory
    {
        IPackageRepository CreateRepository(PackageRepositoryCreateParameters parameters);
    }
}
