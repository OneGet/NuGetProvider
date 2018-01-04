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
    /// <summary>
    /// A single NuGet API search term.
    /// </summary>
    public class NuGetSearchTerm
    {
        public enum NuGetSearchTermType
        {
            Tag,
            SearchTerm,
            Id,
            PackageType,
            AutoComplete,
            OriginalPSPattern,
            Contains
        }

        /// <summary>
        /// Gets the type of search term.
        /// </summary>
        public NuGetSearchTermType Term { get; private set; }

        /// <summary>
        /// Gets the search text.
        /// </summary>
        public string Text { get; private set; }

        public NuGetSearchTerm(NuGetSearchTermType type, string text)
        {
            this.Term = type;
            this.Text = text;
        }

        public override string ToString()
        {
            return this.Term.ToString() + "@" + this.Term;
        }
    }
}
