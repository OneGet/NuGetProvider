
namespace Microsoft.PackageManagement.NuGetProvider 
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;

    internal static class PathUtility
    {
        private static readonly char[] _invalidPathChars = Path.GetInvalidPathChars();

        internal static string EnsureTrailingSlash(string path)
        {
            //The value of DirectorySeparatorChar is a slash ("/") on UNIX, and a backslash ("\") on the Windows and Macintosh.
            return EnsureTrailingCharacter(path, Path.DirectorySeparatorChar);
        }

        private static string EnsureTrailingCharacter(string path, char trailingCharacter)
        {
            if (path == null)
            {
                throw new ArgumentNullException("path");
            }

            // if the path is empty, we want to return the original string instead of a single trailing character.
            if (path.Length == 0 || path[path.Length - 1] == trailingCharacter)
            {
                return path;
            }
           
            return path + trailingCharacter;
        }

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

        internal static bool ValidateSourceUri(IEnumerable<string> supportedSchemes, Uri srcUri, Request request)
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

        internal static HttpResponseMessage GetHttpResponse(HttpClient httpClient, string query, Request request)
        {
            var cts = new CancellationTokenSource();

            Timer timer = null;

            try
            {
                Task task = httpClient.GetAsync(query, cts.Token);

                // check every second to see whether request is cancelled
                timer = new Timer(_ =>
                    {
                        if (request.IsCanceled)
                        {
                            cts.Cancel();
                        }
                    },
                    null, 0, 1000);                
                
                // start the task
                task.Wait();

                if (task.IsCompleted && task is Task<HttpResponseMessage>)
                {
                    return (task as Task<HttpResponseMessage>).Result;
                }
            }
            finally
            {
                // dispose the token
                cts.Dispose();
                if (timer != null)
                {
                    // stop timer
                    timer.Change(Timeout.Infinite, Timeout.Infinite);
                    // dispose it
                    timer.Dispose();
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the validated uri. Returns null if we cannot validate it
        /// </summary>
        /// <param name="query"></param>
        /// <param name="request"></param>
        /// <returns></returns>
        internal static Uri ValidateUri(Uri query, Request request)
        {
            var client = new HttpClient(new HttpClientHandler() { UseDefaultCredentials = true });

            var response = GetHttpResponse(client, query.AbsoluteUri, request);

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
                    // other status code is wrong
                    return null;
                }
            }
            else
            {
                query = new Uri(response.RequestMessage.RequestUri.AbsoluteUri);
            }

            //Making a query like: www.nuget.org/api/v2/FindPackagesById()?id='FoooBarr' to check the server available. 
            //'FoooBarr' is an any random package id
            string queryUri = "FoooBarr".MakeFindPackageByIdQuery(PathUtility.UriCombine(query.AbsoluteUri, NuGetConstant.FindPackagesById));

            response = GetHttpResponse(client, queryUri, request);

            // The link is not valid
            if (response == null || !response.IsSuccessStatusCode)
            {
                return null;
            }

            return query;
        }

        internal static string UriCombine(string query, string append)
        {
            if (String.IsNullOrWhiteSpace(query)) return append;
            if (String.IsNullOrWhiteSpace(append)) return query;

            return query.TrimEnd('/') + "/" + append.TrimStart('/');
        }
    }
}