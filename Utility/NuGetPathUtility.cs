
using Microsoft.PackageManagement.Provider.Utility;

namespace Microsoft.PackageManagement.NuGetProvider
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Runtime.InteropServices;
    using System.Diagnostics;

    internal static class NuGetPathUtility
    {
        private static readonly char[] _invalidPathChars = Path.GetInvalidPathChars();

        
        internal static bool IsManifest(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            return Path.GetExtension(path).Equals(NuGetConstant.ManifestExtension, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsPackageFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            return Path.GetExtension(path).Equals(NuGetConstant.PackageExtension, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool ValidateSourceUri(IEnumerable<string> supportedSchemes, Uri srcUri, NuGetRequest request)
        {

            if (!supportedSchemes.Contains(srcUri.Scheme.ToLowerInvariant()))
            {
                return false;
            }

            if (srcUri.IsFile)
            {              
                //validate file source location
                if (Directory.Exists(srcUri.LocalPath))
                {
                    return true;
                }
                return false;
            }

            //validate uri source location
            return ValidateUri(srcUri, request) != null;
        }
        
        /// <summary>
        /// Returns the validated uri. Returns null if we cannot validate it
        /// </summary>
        /// <param name="query"></param>
        /// <param name="request"></param>
        /// <returns></returns>
        internal static Uri ValidateUri(Uri query, NuGetRequest request)
        {
            // Validation takes place in two steps:
            //  1. Validate the given URI is valid, resolving redirection
            //  2. Validate we can hit the query service
            NetworkCredential credentials = null;
            var client = request.ClientWithoutAcceptHeader;
   
            var response = PathUtility.GetHttpResponse(client, query.AbsoluteUri, (() => request.IsCanceled),
                ((msg, num) => request.Verbose(Resources.Messages.RetryingDownload, msg, num)), (msg) => request.Verbose(msg), (msg) => request.Debug(msg));

            if (response == null)
            {
                return null;
            }

            // if response is not success, we need to check for redirection
            if (!response.IsSuccessStatusCode)
            {
                // Check for redirection (http status code 3xx)
                if (response.StatusCode == HttpStatusCode.MultipleChoices || response.StatusCode == HttpStatusCode.MovedPermanently
                    || response.StatusCode == HttpStatusCode.Found || response.StatusCode == HttpStatusCode.SeeOther
                    || response.StatusCode == HttpStatusCode.TemporaryRedirect)
                {
                    // get the redirected direction
                    string location = response.Headers.GetValues("Location").FirstOrDefault();
                    if (String.IsNullOrWhiteSpace(location))
                    {
                        return null;
                    }

                    // make a new query based on location
                    query = new Uri(location);
                }
                else
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized && request.CredentialUsername.IsNullOrEmpty() && !(request.suppressCredentialProvider))
                    {
                        // If the uri is not validated, try again using credentials retrieved from credential provider
                        // First call to the credential provider is to get credentials, but if those credentials fail,
                        // we call the cred provider again to ask the user for new credentials, and then search try to validate uri again using new creds
                        credentials = request.GetCredsFromCredProvider(query.AbsoluteUri, request, false);
                        var newClient = PathUtility.GetHttpClientHelper(credentials.UserName, credentials.SecurePassword, request.WebProxy);

                        var newResponse = PathUtility.GetHttpResponse(newClient, query.AbsoluteUri, (() => request.IsCanceled),
                            ((msg, num) => request.Verbose(Resources.Messages.RetryingDownload, msg, num)), (msg) => request.Verbose(msg), (msg) => request.Debug(msg));
                        query = new Uri(newResponse.RequestMessage.RequestUri.AbsoluteUri);

                        request.SetHttpClient(newClient);

                        if (newResponse.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            // Calling the credential provider for a second time, using -IsRetry
                            credentials = request.GetCredsFromCredProvider(query.AbsoluteUri, request, true);
                            newClient = PathUtility.GetHttpClientHelper(credentials.UserName, credentials.SecurePassword, request.WebProxy);

                            newResponse = PathUtility.GetHttpResponse(newClient, query.AbsoluteUri, (() => request.IsCanceled),
                                ((msg, num) => request.Verbose(Resources.Messages.RetryingDownload, msg, num)), (msg) => request.Verbose(msg), (msg) => request.Debug(msg));
                            query = new Uri(newResponse.RequestMessage.RequestUri.AbsoluteUri);

                            request.SetHttpClient(newClient);

                            if (newResponse.StatusCode == HttpStatusCode.Unauthorized)
                            {
                                request.WriteError(ErrorCategory.PermissionDenied, "ValidateUri", Resources.Messages.AccessPermissionDenied, query);
                                return null;
                            }
                        }
                    }
                    else
                    {
                        // other status code is wrong
                        return null;
                    }
                }
            }
            else
            {
                query = new Uri(response.RequestMessage.RequestUri.AbsoluteUri);
            }

            IPackageRepository repo = PackageRepositoryFactory.Default.CreateRepository(new PackageRepositoryCreateParameters(query.AbsoluteUri, request, locationValid: true));
            if (repo.ResourceProvider != null)
            {
                // If the query feed exists, it's a non-local repo
                // Check the query feed to make sure it's available
                // Optionally we could change this to check the packages feed for availability
                if (repo.ResourceProvider.QueryFeed == null || !repo.ResourceProvider.QueryFeed.IsAvailable(new RequestWrapper(request, credentials)))
                {
                    return null;
                }
            }

            return query;
        }

#region CryptProtectData
        //internal struct DATA_BLOB
        //{
        //    public int cbData;
        //    public IntPtr pbData;
        //}

        //internal static void CopyByteToBlob(ref DATA_BLOB blob, byte[] data)
        //{
        //    blob.pbData = Marshal.AllocHGlobal(data.Length);

        //    blob.cbData = data.Length;

        //    Marshal.Copy(data, 0, blob.pbData, data.Length);
        //}

        //internal const uint CRYPTPROTECT_UI_FORBIDDEN = 0x1;

        //[DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        //private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, ref string ppszDataDescr, ref DATA_BLOB pOptionalEntropy,
        //    IntPtr pvReserved, IntPtr pPromptStruct, uint dwFlags, ref DATA_BLOB pDataOut);

        //[DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        //private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string szDataDescr, ref DATA_BLOB pOptionalEntropy,
        //    IntPtr pvReserved, IntPtr pPromptStruct, uint dwFlags, ref DATA_BLOB pDataOut);

        //public static byte[] CryptProtect(byte[] dataIn, byte[] optionalEntropy, bool encryptionOperation)
        //{
        //    DATA_BLOB dataInBlob = new DATA_BLOB();
        //    DATA_BLOB optionalEntropyBlob = new DATA_BLOB();
        //    DATA_BLOB resultBlob = new DATA_BLOB();
        //    string description = String.Empty;

        //    try
        //    {
        //        // copy the encrypted blob
        //        CopyByteToBlob(ref dataInBlob, dataIn);
        //        CopyByteToBlob(ref optionalEntropyBlob, optionalEntropy);

        //        // use local user
        //        uint flags = CRYPTPROTECT_UI_FORBIDDEN;

        //        bool success = false;

        //        // doing decryption
        //        if (!encryptionOperation)
        //        {
        //            // call win32 api
        //            success = CryptUnprotectData(ref dataInBlob, ref description, ref optionalEntropyBlob, IntPtr.Zero, IntPtr.Zero, flags, ref resultBlob);
        //        }
        //        else
        //        {
        //            // doing encryption
        //            success = CryptProtectData(ref dataInBlob, description, ref optionalEntropyBlob, IntPtr.Zero, IntPtr.Zero, flags, ref resultBlob);
        //        }

        //        if (!success)
        //        {
        //            throw new Win32Exception(Marshal.GetLastWin32Error());
        //        }

        //        byte[] unencryptedBytes = new byte[resultBlob.cbData];

        //        Marshal.Copy(resultBlob.pbData, unencryptedBytes, 0, resultBlob.cbData);

        //        return unencryptedBytes;
        //    }
        //    finally
        //    {
        //        // free memory
        //        if (dataInBlob.pbData != IntPtr.Zero)
        //        {
        //            Marshal.FreeHGlobal(dataInBlob.pbData);
        //        }

        //        if (optionalEntropyBlob.pbData != IntPtr.Zero)
        //        {
        //            Marshal.FreeHGlobal(optionalEntropyBlob.pbData);
        //        }

        //        if (resultBlob.pbData != IntPtr.Zero)
        //        {
        //            Marshal.FreeHGlobal(resultBlob.pbData);
        //        }
        //    }
        //}
#endregion
    }
}