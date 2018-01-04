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
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    
    /// <summary>
    /// Tracks a group of tasks with the same result type.
    /// </summary>
    /// <typeparam name="TResult">Result type of tasks.</typeparam>
    internal class TaskGroup<TResult>
    {
        /// <summary>
        /// Inner tasks
        /// </summary>
        private List<Task<TResult>> tasks;

        /// <summary>
        /// Gets if this task group has any tasks remaining
        /// </summary>
        public bool HasAny
        {
            get
            {
                return tasks.Count > 0;
            }
        }

        public TaskGroup()
        {
            tasks = new List<Task<TResult>>();
        }

        /// <summary>
        /// Add a task for this TaskGroup to track
        /// </summary>
        public void Add(Task<TResult> task)
        {
            tasks.Add(task);
        }

        /// <summary>
        /// Wait for any task to complete and get the result.
        /// </summary>
        /// <returns>Result of the first completed task.</returns>
        public TResult WaitAny()
        {
            int index = Task.WaitAny(tasks.Cast<Task>().ToArray());
            TResult result = tasks[index].Result;
            tasks.RemoveAt(index);
            return result;
        }
    }
}
