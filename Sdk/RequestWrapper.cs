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
    using Microsoft.PackageManagement.Provider.Utility;
    using System;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Security;

    /// <summary>
    /// Wraps Request object functions by providing non-Request based alternatives. For now, this is assumed to run inside an argument completer, which means there's no logging capabilities.
    /// </summary>
    public class RequestWrapper
    {
        /// <summary>
        /// Lock for creating clientWithoutHeaders.
        /// </summary>
        private object lockObjNoHeaders = new object();

        /// <summary>
        /// Lock for creating clientWithHeaders.
        /// </summary>
        private object lockObjHeaders = new object();

        /// <summary>
        /// HttpClient instance without additional headers. In addition to user-defined headers, Accept-Charset="UTF-8" and Accept-Encoding="gzip,deflate" are added.
        /// </summary>
        private HttpClient clientWithoutHeaders = null;

        /// <summary>
        /// HttpClient without any additional headers. Still contains a User-Agent header.
        /// </summary>
        private HttpClient clientWithHeaders = null;

        /// <summary>
        /// Gets the username to use for authentication in the HttpClient, if any.
        /// </summary>
        public string UserName { get; private set; }

        /// <summary>
        /// Gets the password to use for authentication in the HttpClient, if any.
        /// </summary>
        public SecureString UserPassword { get; private set; }
        
        /// <summary>
        /// Gets the proxy to use for the HttpClient, if any.
        /// </summary>
        public IWebProxy Proxy { get; private set; }

        /// <summary>
        /// Gets the user-defined headers to add to the HttpClient, if any. This format is the same as Request: HeaderName=HeaderValue
        /// </summary>
        public string[] Headers { get; private set; }

        /// <summary>
        /// Gets the actual Request object this wraps. If this is null, the above properties are used instead. If this is not null, the above properties are ignored.
        /// </summary>
        public NuGetRequest Request { get; private set; }
        
        /// <summary>
        /// Gets the output handler.
        /// </summary>
        public IRequestOutput Output { get; private set; }

        /// <summary>
        /// Create a RequestWrapper instance without using Request.
        /// </summary>
        public RequestWrapper() : this(null, null, null, null, null)
        {
        }

        /// <summary>
        /// Create a RequestWrapper instance without using Request.
        /// </summary>
        public RequestWrapper(IRequestOutput output) : this(null, null, null, null, output)
        {
        }

        /// <summary>
        /// Create a RequestWrapper instance without using Request.
        /// </summary>
        public RequestWrapper(string userName, SecureString userPassword, IWebProxy proxy, string[] headers, IRequestOutput output = null)
        {
            this.UserName = userName;
            this.UserPassword = userPassword;
            this.Proxy = proxy;
            this.Headers = headers;
            this.Output = output == null ? new NullRequestOutput() : output;
        }

        /// <summary>
        /// Create a RequestWrapper that just calls into <paramref name="request"/>.
        /// </summary>
        /// <param name="request">The actual Request object.</param>
        public RequestWrapper(NuGetRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            this.Request = request;
            this.Output = new NuGetRequestOutput(request);
        }

        /// <summary>
        /// Create a RequestWrapper that calls into <paramref name="request"/> with <param name="userName"> and <param name="userPassword"> credentials.
        /// </summary>
        /// <param name="request">The actual Request object.</param>
        /// <param name="credential">The NetworkCredential object.</param>
        public RequestWrapper(NuGetRequest request, NetworkCredential credential)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            this.Request = request;
            if (credential != null)
            {
                this.UserName = credential.UserName;
                this.UserPassword = credential.SecurePassword;
            }
            this.Output = new NuGetRequestOutput(request);
        }

        /// <summary>
        /// Gets or creates the HttpClient without additional headers.
        /// </summary>
        /// <returns>HttpClient using either given credentials or given Request without headers.</returns>
        public HttpClient GetClient()
        {
            if (this.Request == null)
            {
                if (this.clientWithoutHeaders == null)
                {
                    lock (lockObjNoHeaders)
                    {
                        if (this.clientWithoutHeaders == null)
                        {
                            this.clientWithoutHeaders = PathUtility.GetHttpClientHelper(this.UserName, this.UserPassword, this.Proxy);
                        }
                    }
                }

                return this.clientWithoutHeaders;
            }
            else
            {
                return this.Request.ClientWithoutAcceptHeader;
            }
        }

        /// <summary>
        /// Gets or creates the HttpClient with additional headers.
        /// </summary>
        /// <returns>HttpClient using either given credentials or given Request with headers.</returns>
        public HttpClient GetClientWithHeaders()
        {
            if (this.Request == null)
            {
                if (this.clientWithHeaders == null)
                {
                    lock (lockObjHeaders)
                    {
                        if (this.clientWithHeaders == null)
                        {
                            this.clientWithHeaders = PathUtility.GetHttpClientHelper(this.UserName, this.UserPassword, this.Proxy);

                            this.clientWithHeaders.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Charset", "UTF-8");
                            // Request for gzip and deflate encoding to make the response lighter.
                            this.clientWithHeaders.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip,deflate");

                            if (this.Headers != null)
                            {
                                foreach (var header in this.Headers)
                                {
                                    // header is in the format "A=B" because OneGet doesn't support Dictionary parameters
                                    if (!String.IsNullOrEmpty(header))
                                    {
                                        var headerSplit = header.Split(new string[] { "=" }, 2, StringSplitOptions.RemoveEmptyEntries);

                                        // ignore wrong entries
                                        if (headerSplit.Count() == 2)
                                        {
                                            this.clientWithHeaders.DefaultRequestHeaders.TryAddWithoutValidation(headerSplit[0], headerSplit[1]);
                                        }
                                        else
                                        {
                                            Warning(Resources.Messages.HeaderIgnored, header);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                return this.clientWithoutHeaders;
            }
            else
            {
                return this.Request.Client;
            }
        }

        /// <summary>
        /// Logs a warning message using the inner Request object, if any.
        /// </summary>
        public void Warning(string msg, params object[] args)
        {
            this.Output.Warning(msg, args);
        }

        /// <summary>
        /// Determines if the request is cancelled using the inner Request object, if any.
        /// </summary>
        /// <returns>The IsCanceled property of the inner Request object if it exists; false otherwise.</returns>
        public bool IsCanceled()
        {
            if (this.Request != null)
            {
                return this.Request.IsCanceled;
            }

            return false;
        }

        /// <summary>
        /// Logs a verbose message using the inner Request object, if any.
        /// </summary>
        public void Verbose(string msg, params object[] args)
        {
            this.Output.Verbose(msg, args);
        }

        /// <summary>
        /// Logs a debug message using the inner Request object, if any.
        /// </summary>
        public void Debug(string msg, params object[] args)
        {
            this.Output.Debug(msg, args);
        }

        /// <summary>
        /// Logs a retry download message using the inner Request object, if any.
        /// </summary>
        /// <param name="msg">Message</param>
        /// <param name="num">Retry number</param>
        public void RetryLog(string msg, int num)
        {
            if (this.Request != null)
            {
                this.Request.Verbose(Resources.Messages.RetryingDownload, msg, num);
            }
        }

        public int StartProgress(int activityId, string message)
        {
            if (this.Request != null)
            {
                return this.Request.StartProgress(activityId, message);
            }

            return 0;
        }

        public void Progress(int activityId, int progress, string message)
        {
            if (this.Request != null)
            {
                this.Request.Progress(activityId, progress, message);
            }
        }

        public void CompleteProgress(int activityId, bool successful)
        {
            if (this.Request != null)
            {
                this.Request.CompleteProgress(activityId, successful);
            }
        }
    }
}
