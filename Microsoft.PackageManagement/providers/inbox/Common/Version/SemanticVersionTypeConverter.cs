﻿//
// This file was cloned from https://github.com/OneGet/oneget on 1/8/2018 2:01:44 PM
// Do not directly modify this file. Make a pull request at https://github.com/OneGet/oneget first instead.
// Then run Sync-OneGetCode.ps1 in this repository to bring the changes down and merge the new files.
//


namespace Microsoft.PackageManagement.Provider.Utility
{
    using System;
    using System.ComponentModel;
    using System.Globalization;

    /// <summary>
    /// Convert String  to SemanticVersion type
    /// </summary>
    public class SemanticVersionTypeConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
        {
            return sourceType == typeof(string);
        }

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            var stringValue = value as string;
            SemanticVersion semVer;
            if (stringValue != null && SemanticVersion.TryParse(stringValue, out semVer))
            {
                return semVer;
            }
            return null;
        }
    }
}