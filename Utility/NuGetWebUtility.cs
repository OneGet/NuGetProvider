//
//  Copyright (c) Microsoft Corporation. All rights reserved.
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//  http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//

namespace Microsoft.PackageManagement.NuGetProvider
{
    using Microsoft.PackageManagement.NuGetProvider.Resources;
    using Microsoft.PackageManagement.NuGetProvider.Utility;
    using Microsoft.PackageManagement.Provider.Utility;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Xml.Linq;

    /// <summary>
    /// Contains HTTP utilities for NuGet interaction.
    /// </summary>
    internal sealed class NuGetWebUtility
    {
        /// <summary>
        /// Send the request to the server with buffer size to account for the case where there are more data
        /// that we need to fetch
        /// </summary>
        /// <param name="query"></param>
        /// <param name="request"></param>
        /// <returns></returns>
        public static IEnumerable<PackageBase> SendRequest(string query, NuGetRequest request)
        {
            return SendRequest(query, new RequestWrapper(request));
        }

        public static IEnumerable<PackageBase> SendRequest(string query, RequestWrapper request)
        {
            // Enforce use of TLS 1.2 when sending request
            var securityProtocol = System.Net.ServicePointManager.SecurityProtocol;
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;

            const int bufferSize = 40;
            // number of threads sending the requests
            const int numberOfSenders = 4;

            var startPoint = 0;
            var tasks = new List<Task<Stream>>();

            bool stopSending = false;
            object stopLock = new Object();

            // Send one request first

            // this initial query is of the form http://www.nuget.org/api/v2/FindPackagesById()?id='jquery'&$skip={0}&$top={1}
            UriBuilder initialQuery = new UriBuilder(query.InsertSkipAndTop());

            PackageBase firstPackage = null;

            // Send out an initial request
            // we send out 1 initial request first to check for redirection and check whether repository supports odata
            using (Stream stream = NuGetClient.InitialDownloadDataToStream(initialQuery, startPoint, bufferSize, request))
            {
                if (stream == null)
                {
                    yield break;
                }

                XDocument document = XmlUtility.LoadSafe(stream, ignoreWhiteSpace: true);

                var entries = document.Root.ElementsNoNamespace("entry").ToList();

                // If the initial request has different number of entries than the buffer size, return it because this means the server
                // does not understand odata request or there is no more data. in the former case, we have to stop to prevent infinite loop
                if (entries.Count != bufferSize)
                {
                    request.Debug(Messages.PackagesReceived, entries.Count);
                    stopSending = true;
                }

                foreach (XElement entry in entries)
                {
                    var package = new PackageBase();

                    // set the first package of the request. this is used later to verify that the case when the number of packages in the repository
                    // is the same as the buffer size and the repository does not support odata query. in that case, we want to check whether the first package
                    // exists anywhere in the second call. if it is, then we cancel the request (this is to prevent infinite loop)
                    if (firstPackage == null)
                    {
                        firstPackage = package;
                    }

                    PackageUtility.ReadEntryElement(ref package, entry);
                    yield return package;
                }
            }

            if (stopSending || request.IsCanceled())
            {
                yield break;
            }

            // To avoid more redirection (for example, if the initial query is nuget.org, it will be changed to www.nuget.org

            query = initialQuery.Uri.ToString();

            // Sending the initial requests
            for (var i = 0; i < numberOfSenders; i++)
            {
                // Update the start point to fetch the packages
                startPoint += bufferSize;

                // Get the query
                var newQuery = string.Format(query, startPoint, bufferSize);

                // Send it 
                tasks.Add(Task.Factory.StartNew(() =>
                {
                    Stream items = NuGetClient.DownloadDataToStream(newQuery, request);
                    return items;
                }));
            }

            //Wait for the responses, parse the data, and send to the user
            while (tasks.Count > 0)
            {

                //Cast because the compiler warning: Co-variant array conversion from Task[] to Task[] can cause run-time exception on write operation.
                var index = Task.WaitAny(tasks.Cast<Task>().ToArray());

                using (Stream stream = tasks[index].Result)
                {
                    if (stream == null)
                    {
                        yield break;
                    }

                    XDocument document = XmlUtility.LoadSafe(stream, ignoreWhiteSpace: true);

                    var entries = document.Root.ElementsNoNamespace("entry").ToList();

                    if (entries.Count < bufferSize)
                    {
                        request.Debug(Messages.PackagesReceived, entries.Count);
                        lock (stopLock)
                        {
                            stopSending = true;
                        }
                    }

                    foreach (XElement entry in entries)
                    {
                        var package = new PackageBase();

                        PackageUtility.ReadEntryElement(ref package, entry);

                        if (firstPackage != null)
                        {
                            // check whether first package in the first request exists anywhere in the second request
                            if (string.Equals(firstPackage.GetFullName(), package.GetFullName(), StringComparison.OrdinalIgnoreCase)
                                && string.Equals(firstPackage.Version, package.Version, StringComparison.OrdinalIgnoreCase))
                            {
                                lock (stopLock)
                                {
                                    stopSending = true;
                                }

                                break;
                            }
                        }

                        yield return package;
                    }

                    // we only needs to check for the existence of the first package in the second request. don't need to do for subsequent request
                    if (firstPackage != null)
                    {
                        firstPackage = null;
                    }
                }

                // checks whether we should stop sending requests
                if (!stopSending && !request.IsCanceled())
                {
                    // Make sure nobody else is updating the startPoint
                    lock (stopLock)
                    {
                        // update the startPoint
                        startPoint += bufferSize;
                    }
                    // Make a new request with the new startPoint
                    var newQuery = string.Format(query, startPoint, bufferSize);

                    //Keep sending a request 
                    tasks[index] = (Task.Factory.StartNew(searchQuery =>
                    {
                        var items = NuGetClient.DownloadDataToStream(searchQuery.ToStringSafe(), request);
                        return items;
                    }, newQuery));

                }
                else
                {
                    if (request.IsCanceled())
                    {
                        request.Warning(Messages.RequestCanceled, "NuGetWebUtility", "SendRequest");
                        //stop sending request to the remote server
                        stopSending = true;
                    }

                    tasks.RemoveAt(index);
                }
            }

            // Change back to user specified security protocol
            System.Net.ServicePointManager.SecurityProtocol = securityProtocol;
        }

        /// <summary>
        /// Get results from paged query for NuGet v3
        /// </summary>
        /// <typeparam name="B">Response body type</typeparam>
        /// <typeparam name="R">Result type</typeparam>
        /// <param name="request">Current request</param>
        /// <param name="getTotalResultsCountFromResponse">Delegate to get total result count from first response. Return 0 or a negative number to indicate failure.</param>
        /// <param name="getResultsFromResponse">Delegate to get results from a response body.</param>
        /// <param name="getPackageQuery">Delegate to get the next query, skipping a given number of results.</param>
        /// <param name="parseResponseBody">Delegate to parse the response string body to a response object body.</param>
        /// <returns>All results.</returns>
        public static IEnumerable<R> GetResults<B, R>(NuGetRequest request, Func<B, long> getTotalResultsCountFromResponse, Func<B, IEnumerable<IEnumerable<R>>> getResultsFromResponse, Func<long, string> getNextResponseQuery, Func<string, B> parseResponseBody, int pageCount)
        {
            return GetResults<B, R>(new RequestWrapper(request), getTotalResultsCountFromResponse, getResultsFromResponse, getNextResponseQuery, parseResponseBody, pageCount);
        }

        /// <summary>
        /// Get results from paged query for NuGet v3
        /// </summary>
        /// <typeparam name="B">Response body type</typeparam>
        /// <typeparam name="R">Result type</typeparam>
        /// <param name="request">Current request</param>
        /// <param name="getTotalResultsCountFromResponse">Delegate to get total result count from first response. Return 0 or a negative number to indicate failure.</param>
        /// <param name="getResultsFromResponse">Delegate to get results from a response body.</param>
        /// <param name="getPackageQuery">Delegate to get the next query, skipping a given number of results.</param>
        /// <param name="parseResponseBody">Delegate to parse the response string body to a response object body.</param>
        /// <returns>All results.</returns>
        public static IEnumerable<R> GetResults<B, R>(RequestWrapper request, Func<B, long> getTotalResultsCountFromResponse, Func<B, IEnumerable<IEnumerable<R>>> getResultsFromResponse, Func<long, string> getNextResponseQuery, Func<string, B> parseResponseBody, int pageCount)
        {
            ProgressTracker progressTracker = new ProgressTracker(ProgressTracker.GetRandomId(), 0, 100);
            int childProgressTrackerId = request.StartProgress(progressTracker.ProgressID, Resources.Messages.NuGetServerReadStarted);
            long total = -1;
            long resultCount = 0;
            bool successful = true;
            bool trackIndividualProgress = false;
            TaskGroup<IEnumerable<IEnumerable<R>>> taskGroup = new TaskGroup<IEnumerable<IEnumerable<R>>>();
            System.Threading.CancellationTokenSource cancelToken = new System.Threading.CancellationTokenSource();
            string query = getNextResponseQuery(resultCount);
            string content = new StreamReader(NuGetClient.DownloadDataToStream(query, request)).ReadToEnd();

            B response = parseResponseBody(content);
            total = getTotalResultsCountFromResponse(response);
            if (total < 0)
            {
                total = -2;
                request.Warning(Messages.FailedToParseTotalHitsCount, query);
                successful = false;
            }
            else
            {
                request.Progress(childProgressTrackerId, 0, String.Format(CultureInfo.CurrentCulture, Resources.Messages.NuGetServerReadProgress, resultCount, total));
                // When the result count is low enough, track individual results. Otherwise, track by page.
                trackIndividualProgress = total <= (pageCount * 2);
                taskGroup.Add(Task.Factory.StartNew<IEnumerable<IEnumerable<R>>>(() => { return getResultsFromResponse(response); }, cancelToken.Token));
            }

            if (total >= 0)
            {
                long numberOfPages = total / pageCount + (total % pageCount == 0 ? 0 : 1);
                long pageSkipCount = 0;
                for (long pageNum = 1; pageNum < numberOfPages; pageNum++)
                {
                    pageSkipCount += pageCount;
                    string pageQuery = getNextResponseQuery(pageSkipCount);
                    taskGroup.Add(Task.Factory.StartNew<IEnumerable<IEnumerable<R>>>((q) =>
                    {
                        string pageContent = new StreamReader(NuGetClient.DownloadDataToStream((string)q, request)).ReadToEnd();
                        B pageResponse = parseResponseBody(pageContent);
                        return getResultsFromResponse(pageResponse);
                    }, pageQuery, cancelToken.Token));
                }
            }

            while (taskGroup.HasAny)
            {
                if (request.IsCanceled())
                {
                    cancelToken.Cancel();
                    successful = false;
                    break;
                }

                IEnumerable<IEnumerable<R>> resultCollections = taskGroup.WaitAny();
                foreach (IEnumerable<R> resultCollection in resultCollections)
                {
                    resultCount++;
                    // If trackIndividualProgress == false, this progress will be reported at the end
                    if (trackIndividualProgress)
                    {
                        // Report an individual package is done processing
                        request.Progress(childProgressTrackerId, progressTracker.ConvertPercentToProgress(((double)resultCount) / total),
                            String.Format(CultureInfo.CurrentCulture, Resources.Messages.NuGetServerReadProgress, resultCount, total));
                    }

                    foreach (R result in resultCollection)
                    {
                        yield return result;
                    }
                }

                if (!trackIndividualProgress)
                {
                    // Report that this page is done processing
                    request.Progress(childProgressTrackerId, progressTracker.ConvertPercentToProgress(((double)resultCount) / total),
                        String.Format(CultureInfo.CurrentCulture, Resources.Messages.NuGetServerReadProgress, resultCount, total));
                }
            }

            request.CompleteProgress(childProgressTrackerId, successful);
            request.CompleteProgress(progressTracker.ProgressID, successful);
        }

        public static IEnumerable<R> GetPageResults<B, R>(RequestWrapper request, Func<B, IEnumerable<IEnumerable<R>>> getResultsFromResponse, B response)
        {
            IEnumerable<IEnumerable<R>> resultCollections = getResultsFromResponse(response);
            foreach (IEnumerable<R> resultCollection in resultCollections)
            {
                foreach (R result in resultCollection)
                {
                    yield return result;
                }
            }
        }
    }
}
