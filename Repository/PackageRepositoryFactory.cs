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

        public IPackageRepository CreateRepository(PackageRepositoryCreateParameters parms)
        {
            if (parms == null)
            {
                throw new ArgumentNullException("parms");
            }

            if (parms.Location == null)
            {
                throw new ArgumentNullException("parms.Location");
            }
            
            if (parms.Request == null)
            {
                throw new ArgumentNullException("parms.Request");
            }

            // we cannot call new uri on file path on linux because it will error out
            if (System.IO.Directory.Exists(parms.Location))
            {
                return new LocalPackageRepository(parms.Location, parms.Request);
            }

            Uri uri = new Uri(parms.Location);

            if (uri.IsFile)
            {
                return new LocalPackageRepository(uri.LocalPath, parms.Request);
            }

            return new NuGetPackageRepository(parms);
        }
    }
}
