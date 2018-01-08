﻿//
// This file was cloned from https://github.com/OneGet/oneget on 1/8/2018 2:01:44 PM
// Do not directly modify this file. Make a pull request at https://github.com/OneGet/oneget first instead.
// Then run Sync-OneGetCode.ps1 in this repository to bring the changes down and merge the new files.
//

namespace Microsoft.PackageManagement.Provider.Utility
{
    using System.IO;
    using System.Xml;
    using System.Xml.Linq;

    public static class XmlUtility
    {
        public static XDocument LoadSafe(string filePath)
        {
            var settings = CreateSafeSettings();
            using (var reader = XmlReader.Create(filePath, settings))
            {
                return XDocument.Load(reader);
            }
        }

        public static XDocument LoadSafe(Stream input, bool ignoreWhiteSpace)
        {
            var settings = CreateSafeSettings(ignoreWhiteSpace);
            var reader = XmlReader.Create(input, settings);
            return XDocument.Load(reader);
        }

        private static XmlReaderSettings CreateSafeSettings(bool ignoreWhiteSpace = false)
        {
            var safeSettings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreWhitespace = ignoreWhiteSpace
            };

            return safeSettings;
        }
    }
}