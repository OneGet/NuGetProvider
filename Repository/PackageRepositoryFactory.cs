﻿namespace Microsoft.PackageManagement.NuGetProvider 
{
    using System;

    public class PackageRepositoryFactory : IPackageRepositoryFactory
    {
        private static readonly PackageRepositoryFactory _default = new PackageRepositoryFactory();

        public static PackageRepositoryFactory Default
        {
            get { return _default;}
        }

        public virtual IPackageRepository CreateRepository(string packageSource)
        {
            if (packageSource == null)
            {
                throw new ArgumentNullException("packageSource");
            }

            Uri uri = new Uri(packageSource);

            if (uri.IsFile)
            {
                return new LocalPackageRepository(uri.LocalPath);
            }

            return new HttpClientPackageRepository(packageSource);
        }
    }
}