namespace Microsoft.PackageManagement.NuGetProvider 
{
    using System;
    using System.Net;

    public class PackageRepositoryFactory : IPackageRepositoryFactory
    {
        private static readonly PackageRepositoryFactory _default = new PackageRepositoryFactory();

        public static PackageRepositoryFactory Default
        {
            get { return _default;}
        }

        public IPackageRepository CreateRepository(PackageRepositoryCreateParameters parameters)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException("parameters");
            }

            if (parameters.Location == null)
            {
                throw new ArgumentNullException("parameters.Location");
            }
            
            if (parameters.Request == null)
            {
                throw new ArgumentNullException("parameters.Request");
            }

            IPackageRepository repository = ConcurrentInMemoryCache.Instance.GetOrAdd<IPackageRepository>(parameters.Location, () =>
            {
                // we cannot call new uri on file path on linux because it will error out
                if (System.IO.Directory.Exists(parameters.Location))
                {
                    return new LocalPackageRepository(parameters.Location, parameters.Request);
                }

                Uri uri = new Uri(parameters.Location);

                if (uri.IsFile)
                {
                    return new LocalPackageRepository(uri.LocalPath, parameters.Request);
                }

                return new NuGetPackageRepository(parameters);
            });

            return repository;
        }
    }
}
