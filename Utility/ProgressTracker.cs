﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.PackageManagement.NuGetProvider.Utility
{
    internal class ProgressTracker
    {
        private static Random randomGen = new Random();
        internal int ProgressID;
        internal int StartPercent;
        internal int EndPercent;

        internal ProgressTracker(int progressID) : this(progressID, 0, 100) { }

        internal ProgressTracker(int progressID, int startPercent, int endPercent)
        {
            ProgressID = progressID;
            StartPercent = startPercent;
            EndPercent = endPercent;
        }

        internal int ConvertPercentToProgress(double percent)
        {
            // for example, if startprogress is 50, end progress is 100, and if we want to complete 50% of that,
            // then the progress returned would be 75
            return StartPercent + (int)((EndPercent - StartPercent) * percent);
        }

        internal static ProgressTracker StartProgress(ProgressTracker parentTracker, string message, NuGetRequest request)
        {
            if (request == null)
            {
                return null;
            }

            // if parent tracker is null, use 0 for parent id, else use the progressid of parent tracker
            return new ProgressTracker(request.StartProgress(parentTracker == null ? 0 : parentTracker.ProgressID, message));
        }

        /// <summary>
        /// Generate a random progress ID that's not 0 (0 is used a lot, so it's dangerous to use here). Eventually we want to change this to
        /// GetUniqueId
        /// but we need a way to remove used IDs first.
        /// </summary>
        /// <returns>Random progress ID in the range (0, Int32.MaxValue)</returns>
        internal static int GetRandomId()
        {
            return randomGen.Next(1, Int32.MaxValue);
        }
    }
}
