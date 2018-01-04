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

    internal class ConcurrentInMemoryCache
    {
        private static ConcurrentInMemoryCache instance = new ConcurrentInMemoryCache();
        public static ConcurrentInMemoryCache Instance
        {
            get
            {
                return instance;
            }
        }

        private Dictionary<string, object> cache = new Dictionary<string, object>();
        public T GetOrAdd<T>(string key, Func<T> constructor)
        {
            T res = default(T);
            if (!cache.ContainsKey(key))
            {
                lock (cache)
                {
                    if (!cache.ContainsKey(key))
                    {
                        res = constructor();
                        cache[key] = res;
                    }
                }
            }

            return (T)cache[key];
        }

        public bool TryGet<T>(string key, out T val)
        {
            if (cache.ContainsKey(key))
            {
                val = (T)cache[key];
                return true;
            }

            val = default(T);
            return false;
        }
    }
}
