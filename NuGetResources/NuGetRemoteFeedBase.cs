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
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    /// <summary>
    /// Base for any web feed.
    /// </summary>
    public abstract class NuGetRemoteFeedBase : INuGetFeed
    {
        public IList<NuGetServiceInfo> Endpoints { get; private set; }

        public INuGetResourceCollection ResourcesCollection { get; set; }

        public abstract bool IsAvailable(RequestWrapper request);

        public NuGetRemoteFeedBase()
        {
            this.Endpoints = new List<NuGetServiceInfo>();
        }

        internal T Execute<T>(Func<string, T> executeWithBaseUrl)
        {
            return Execute<T>(executeWithBaseUrl, Constants.DefaultRetryCount, Constants.SimpleBackoffStrategy);
        }

        internal T Execute<T>(Func<string, T> executeWithBaseUrl, int retryCount, Func<int, int> backoffStrategy)
        {
            IList<Exception> allErrors = new List<Exception>();
            foreach (NuGetServiceInfo endpointCandidate in this.Endpoints)
            {
                try
                {
                    return SingleExecuteWithRetries<T>(endpointCandidate, executeWithBaseUrl, retryCount, backoffStrategy);
                } catch (Exception ex)
                {
                    allErrors.Add(ex);
                }
            }

            // If we get here, we've tried all services without success
            throw new AggregateException(allErrors);
        }

        internal T SingleExecuteWithRetries<T>(NuGetServiceInfo endpointInfo, Func<string, T> executeWithBaseUrl, int retryCount, Func<int, int> backoffStrategy)
        {
            int sleepInMs = 0;
            // I don't think there's a huge penalty to collecting an individual service's errors over the course of the retries, and it could be helpful for debugging
            IList<Exception> allErrors = new List<Exception>();
            while (retryCount-- > 0)
            {
                try
                {
                    return executeWithBaseUrl(endpointInfo.Url);
                }
                catch (Exception ex)
                {
                    allErrors.Add(ex);
                }

                // If we get here, the previous attempt has failed. Recalculate the sleep time and wait.
                if (retryCount > 0)
                {
                    sleepInMs = backoffStrategy(sleepInMs);
                    Task.Delay(sleepInMs).Wait();
                }
            }

            // If we get here, we've tried {retryCount} times without success
            throw new AggregateException(allErrors);
        }
    }
}
