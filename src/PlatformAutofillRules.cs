using System;
using System.Collections.Generic;
using System.Linq;

namespace PlatformAutofill
{
    internal static class PlatformAutofillRules
    {
        internal static readonly string[] SupportTemplatePrefixes = { "Platform", "DoublePlatform", "TriplePlatform" };

        internal static int ClampSupportIndex(int supportIndex)
        {
            return Math.Max(0, Math.Min(supportIndex, SupportTemplatePrefixes.Length - 1));
        }

        internal static bool IsSupportTemplateName(string templateName)
        {
            if (string.IsNullOrEmpty(templateName))
            {
                return false;
            }

            return SupportTemplatePrefixes.Any(prefix => templateName.StartsWith(prefix + ".", StringComparison.Ordinal));
        }

        internal static bool TryExtractFaction(string templateName, out string? faction)
        {
            int dot = templateName.LastIndexOf('.');
            if (dot < 0 || dot >= templateName.Length - 1)
            {
                faction = null;
                return false;
            }

            faction = templateName.Substring(dot + 1);
            return true;
        }

        internal static IEnumerable<string> EnumerateSupportTemplateNames(int maxSupportIndex, string faction)
        {
            if (string.IsNullOrEmpty(faction))
            {
                yield break;
            }

            int clampedMaxIndex = ClampSupportIndex(maxSupportIndex);
            for (int sizeIdx = clampedMaxIndex; sizeIdx >= 0; sizeIdx--)
            {
                yield return $"{SupportTemplatePrefixes[sizeIdx]}.{faction}";
            }
        }

        internal static int FindGapBottom(int terrainTop, int placedBottomZ, Func<int, bool> hasExistingSupportBaseAt)
        {
            for (int z = placedBottomZ - 1; z >= terrainTop; z--)
            {
                if (hasExistingSupportBaseAt(z))
                {
                    return z + 1;
                }
            }

            return terrainTop;
        }

        internal static IEnumerable<T> OrderSupportPlacements<T>(
            IEnumerable<T> placements,
            Func<T, int> bottomZSelector,
            Func<T, int> xSelector,
            Func<T, int> ySelector)
        {
            return placements
                .OrderBy(bottomZSelector)
                .ThenBy(xSelector)
                .ThenBy(ySelector);
        }

        internal static bool TrySelectSupportPlacement(
            int gapBottomZ,
            int desiredTopZ,
            int supportSizeZ,
            Func<int, OccupiedZRange?> occupiedZRangeResolver,
            Func<int, bool> candidateValidator,
            out SupportPlacementSelection selection,
            out string searchSummary)
        {
            int searchMinZ = gapBottomZ - supportSizeZ - 2;
            int searchMaxZ = desiredTopZ + supportSizeZ + 2;
            int bestCandidateZ = int.MinValue;
            int bestBottomZ = int.MinValue;
            int bestTopZ = int.MinValue;
            bool found = false;
            List<string> candidateSummaries = new();

            for (int candidateZ = searchMinZ; candidateZ <= searchMaxZ; candidateZ++)
            {
                OccupiedZRange? occupiedZRange = occupiedZRangeResolver(candidateZ);
                if (occupiedZRange == null)
                {
                    continue;
                }

                int minZ = occupiedZRange.Value.MinZ;
                int maxZ = occupiedZRange.Value.MaxZ;
                bool reachesTop = maxZ == desiredTopZ;
                bool staysAboveFloor = minZ >= gapBottomZ;
                candidateSummaries.Add(
                    $"{candidateZ}->{minZ}..{maxZ}" +
                    (reachesTop ? " top" : "") +
                    (staysAboveFloor ? " floor" : "") +
                    (reachesTop && staysAboveFloor ? " fit" : ""));

                if (!reachesTop || !staysAboveFloor)
                {
                    continue;
                }

                if (!candidateValidator(candidateZ))
                {
                    continue;
                }

                if (!found || minZ < bestBottomZ)
                {
                    bestCandidateZ = candidateZ;
                    bestBottomZ = minZ;
                    bestTopZ = maxZ;
                    found = true;
                }
            }

            searchSummary =
                $"sizeZ={supportSizeZ} search={searchMinZ}..{searchMaxZ} candidates=[{string.Join(", ", candidateSummaries)}]";

            if (!found)
            {
                selection = default;
                return false;
            }

            selection = new SupportPlacementSelection(bestCandidateZ, bestBottomZ, bestTopZ);
            return true;
        }

        internal static bool TrySelectSupportPlacement(
            int gapBottomZ,
            int desiredTopZ,
            int supportSizeZ,
            Func<int, OccupiedZRange?> occupiedZRangeResolver,
            out SupportPlacementSelection selection,
            out string searchSummary)
        {
            return TrySelectSupportPlacement(
                gapBottomZ,
                desiredTopZ,
                supportSizeZ,
                occupiedZRangeResolver,
                _ => true,
                out selection,
                out searchSummary);
        }

        internal readonly struct OccupiedZRange
        {
            internal OccupiedZRange(int minZ, int maxZ)
            {
                MinZ = minZ;
                MaxZ = maxZ;
            }

            internal int MinZ { get; }
            internal int MaxZ { get; }
        }

        internal readonly struct SupportPlacementSelection
        {
            internal SupportPlacementSelection(int candidateZ, int bottomZ, int topZ)
            {
                CandidateZ = candidateZ;
                BottomZ = bottomZ;
                TopZ = topZ;
            }

            internal int CandidateZ { get; }
            internal int BottomZ { get; }
            internal int TopZ { get; }
        }
    }
}
