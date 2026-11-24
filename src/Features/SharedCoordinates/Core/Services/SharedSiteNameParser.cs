using System;
using System.Collections.Generic;

namespace Synthetic.Features.SharedCoordinates.Services
{
    /// <summary>
    /// Pure instance-name fallback parser for resolving an assigned Shared Site name from a
    /// Revit link instance display name when <c>BuiltInParameter.GEO_LOCATION</c> is unavailable
    /// or <c>&lt;Not Shared&gt;</c>.
    /// </summary>
    public static class SharedSiteNameParser
    {
        private const string LocationDelimiter = " : location ";
        private const string SegmentDelimiter = " : ";
        private const string NotSharedToken = "<Not Shared>";

        /// <summary>
        /// Resolves a Shared Site name from <paramref name="instanceName"/> using Revit link display-name
        /// conventions and optional canonical matching against <paramref name="knownLinkSiteNames"/>.
        /// </summary>
        /// <param name="instanceName">The <c>RevitLinkInstance.Name</c> display string.</param>
        /// <param name="knownLinkSiteNames">Optional collection of <c>ProjectLocation.Name</c> values from the linked model.</param>
        /// <returns>
        /// The resolved Shared Site name (using canonical casing from <paramref name="knownLinkSiteNames"/> when matched),
        /// or <c>null</c> when the instance is <c>&lt;Not Shared&gt;</c> or no valid site name can be resolved.
        /// </returns>
        public static string? ResolveFromInstanceName(
            string? instanceName,
            IReadOnlyCollection<string>? knownLinkSiteNames)
        {
            if (string.IsNullOrWhiteSpace(instanceName))
            {
                return null;
            }

            Dictionary<string, string>? canonicalSites = BuildCanonicalSiteLookup(knownLinkSiteNames);
            string resolvedName = instanceName!;

            // Scan " : location " right-to-left so a delimiter inside the linked filename does not shadow
            // the trailing site assignment, while skipping trailing " : location <Not Shared>" suffixes
            // appended when RVT_LINK_INSTANCE_NAME encodes the target site before coordinates are shared.
            int searchEnd = resolvedName.Length - 1;
            while (searchEnd >= LocationDelimiter.Length - 1)
            {
                int locIdx = resolvedName.LastIndexOf(LocationDelimiter, searchEnd, StringComparison.OrdinalIgnoreCase);
                if (locIdx < 0)
                {
                    break;
                }

                string afterLoc = resolvedName.Substring(locIdx + LocationDelimiter.Length);
                int nextSep = afterLoc.IndexOf(SegmentDelimiter, StringComparison.Ordinal);
                string candidate = (nextSep >= 0 ? afterLoc.Substring(0, nextSep) : afterLoc).Trim();

                if (!string.IsNullOrWhiteSpace(candidate) &&
                    !string.Equals(candidate, NotSharedToken, StringComparison.OrdinalIgnoreCase))
                {
                    if (canonicalSites != null && canonicalSites.TryGetValue(candidate, out string? canonicalName))
                    {
                        return canonicalName;
                    }

                    return candidate;
                }

                if (locIdx == 0)
                {
                    break;
                }

                searchEnd = locIdx - 1;
            }

            if (resolvedName.IndexOf(NotSharedToken, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return null;
            }

            if (canonicalSites != null)
            {
                int lastColonIdx = resolvedName.LastIndexOf(SegmentDelimiter, StringComparison.Ordinal);
                if (lastColonIdx >= 0)
                {
                    string candidate = resolvedName.Substring(lastColonIdx + SegmentDelimiter.Length).Trim();
                    if (!string.IsNullOrWhiteSpace(candidate) &&
                        !string.Equals(candidate, NotSharedToken, StringComparison.OrdinalIgnoreCase) &&
                        canonicalSites.TryGetValue(candidate, out string? canonicalName))
                    {
                        return canonicalName;
                    }
                }
            }

            return null;
        }

        private static Dictionary<string, string>? BuildCanonicalSiteLookup(IReadOnlyCollection<string>? knownLinkSiteNames)
        {
            if (knownLinkSiteNames == null || knownLinkSiteNames.Count == 0)
            {
                return null;
            }

            Dictionary<string, string>? lookup = null;
            foreach (string? siteName in knownLinkSiteNames)
            {
                if (string.IsNullOrWhiteSpace(siteName))
                {
                    continue;
                }

                string trimmed = siteName!.Trim();
                if (string.Equals(trimmed, NotSharedToken, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (lookup == null)
                {
                    lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                if (!lookup.ContainsKey(trimmed))
                {
                    lookup[trimmed] = siteName;
                }
            }

            return lookup;
        }
    }
}
