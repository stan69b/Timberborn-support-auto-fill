using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace PlatformAutofill.Tests
{
    public class PlatformAutofillRulesTests
    {
        [Theory]
        [InlineData(-4, 0)]
        [InlineData(0, 0)]
        [InlineData(1, 1)]
        [InlineData(2, 2)]
        [InlineData(99, 2)]
        public void ClampSupportIndex_ClampsToSupportedRange(int requestedIndex, int expectedIndex)
        {
            Assert.Equal(expectedIndex, PlatformAutofillRules.ClampSupportIndex(requestedIndex));
        }

        [Fact]
        public void TryExtractFaction_ReturnsFactionSuffix()
        {
            bool success = PlatformAutofillRules.TryExtractFaction("DoublePlatform.IronTeeth", out string? faction);

            Assert.True(success);
            Assert.Equal("IronTeeth", faction);
        }

        [Fact]
        public void TryExtractFaction_UsesSuffixAfterLastDot()
        {
            bool success = PlatformAutofillRules.TryExtractFaction("Mod.Platform.Folktails", out string? faction);

            Assert.True(success);
            Assert.Equal("Folktails", faction);
        }

        [Fact]
        public void TryExtractFaction_RejectsInvalidNames()
        {
            Assert.False(PlatformAutofillRules.TryExtractFaction("Platform", out _));
            Assert.False(PlatformAutofillRules.TryExtractFaction("Platform.", out _));
        }

        [Fact]
        public void EnumerateSupportTemplateNames_OrdersLargestToSmallest()
        {
            string[] candidates = PlatformAutofillRules
                .EnumerateSupportTemplateNames(9, "Folktails")
                .ToArray();

            Assert.Equal(
                new[] { "TriplePlatform.Folktails", "DoublePlatform.Folktails", "Platform.Folktails" },
                candidates);
        }

        [Fact]
        public void EnumerateSupportTemplateNames_ReturnsEmptyForEmptyFaction()
        {
            Assert.Empty(PlatformAutofillRules.EnumerateSupportTemplateNames(2, string.Empty));
        }

        [Fact]
        public void EnumerateSupportTemplateNames_ReturnsSinglePlatformWhenClampedToZero()
        {
            string[] candidates = PlatformAutofillRules
                .EnumerateSupportTemplateNames(-100, "Folktails")
                .ToArray();

            Assert.Equal(new[] { "Platform.Folktails" }, candidates);
        }

        [Fact]
        public void OrderSupportPlacements_SortsBottomUpThenCoordinates()
        {
            SupportPlacementStub[] placements =
            {
                new SupportPlacementStub("B", 4, 2, 0),
                new SupportPlacementStub("C", 1, 5, 5),
                new SupportPlacementStub("A", 4, 1, 3),
                new SupportPlacementStub("D", 4, 1, 1),
            };

            string[] orderedNames = PlatformAutofillRules
                .OrderSupportPlacements(placements, placement => placement.BottomZ, placement => placement.X, placement => placement.Y)
                .Select(placement => placement.Name)
                .ToArray();

            Assert.Equal(new[] { "C", "D", "A", "B" }, orderedNames);
        }

        [Fact]
        public void FindGapBottom_ReturnsTerrainTopWhenNoSupportBaseExists()
        {
            int gapBottom = PlatformAutofillRules.FindGapBottom(
                terrainTop: 3,
                placedBottomZ: 8,
                hasExistingSupportBaseAt: _ => false);

            Assert.Equal(3, gapBottom);
        }

        [Fact]
        public void FindGapBottom_ReturnsCellAboveHighestSupportBaseBelowPlacement()
        {
            int gapBottom = PlatformAutofillRules.FindGapBottom(
                terrainTop: 1,
                placedBottomZ: 9,
                hasExistingSupportBaseAt: z => z == 6 || z == 2);

            Assert.Equal(7, gapBottom);
        }

        [Fact]
        public void FindGapBottom_DoesNotSearchBelowTerrainTop()
        {
            List<int> visited = new List<int>();

            int gapBottom = PlatformAutofillRules.FindGapBottom(
                terrainTop: 4,
                placedBottomZ: 7,
                hasExistingSupportBaseAt: z =>
                {
                    visited.Add(z);
                    return false;
                });

            Assert.Equal(4, gapBottom);
            Assert.Equal(new[] { 6, 5, 4 }, visited);
        }

        [Fact]
        public void TrySelectSupportPlacement_PicksLowestValidBottom()
        {
            Dictionary<int, PlatformAutofillRules.OccupiedZRange> ranges = new Dictionary<int, PlatformAutofillRules.OccupiedZRange>
            {
                [1] = new PlatformAutofillRules.OccupiedZRange(1, 6),
                [2] = new PlatformAutofillRules.OccupiedZRange(2, 6),
                [4] = new PlatformAutofillRules.OccupiedZRange(4, 6),
            };

            bool success = PlatformAutofillRules.TrySelectSupportPlacement(
                gapBottomZ: 2,
                desiredTopZ: 6,
                supportSizeZ: 3,
                candidateZ => ranges.TryGetValue(candidateZ, out PlatformAutofillRules.OccupiedZRange range) ? range : null,
                out PlatformAutofillRules.SupportPlacementSelection selection,
                out string summary);

            Assert.True(success);
            Assert.Equal(2, selection.CandidateZ);
            Assert.Equal(2, selection.BottomZ);
            Assert.Equal(6, selection.TopZ);
            Assert.Contains("2->2..6 top floor fit", summary);
        }

        [Fact]
        public void TrySelectSupportPlacement_IgnoresCandidatesThatDipBelowGapBottom()
        {
            Dictionary<int, PlatformAutofillRules.OccupiedZRange> ranges = new Dictionary<int, PlatformAutofillRules.OccupiedZRange>
            {
                [1] = new PlatformAutofillRules.OccupiedZRange(1, 6),
                [2] = new PlatformAutofillRules.OccupiedZRange(2, 6),
            };

            bool success = PlatformAutofillRules.TrySelectSupportPlacement(
                gapBottomZ: 2,
                desiredTopZ: 6,
                supportSizeZ: 3,
                candidateZ => ranges.TryGetValue(candidateZ, out PlatformAutofillRules.OccupiedZRange range) ? range : null,
                out PlatformAutofillRules.SupportPlacementSelection selection,
                out string summary);

            Assert.True(success);
            Assert.Equal(2, selection.CandidateZ);
            Assert.DoesNotContain("1->1..6 top floor fit", summary);
            Assert.Contains("1->1..6 top", summary);
        }

        [Fact]
        public void TrySelectSupportPlacement_UsesBufferedSearchRange()
        {
            Dictionary<int, PlatformAutofillRules.OccupiedZRange> ranges = new Dictionary<int, PlatformAutofillRules.OccupiedZRange>
            {
                [7] = new PlatformAutofillRules.OccupiedZRange(4, 8),
            };

            bool success = PlatformAutofillRules.TrySelectSupportPlacement(
                gapBottomZ: 4,
                desiredTopZ: 8,
                supportSizeZ: 1,
                candidateZ => ranges.TryGetValue(candidateZ, out PlatformAutofillRules.OccupiedZRange range) ? range : null,
                out PlatformAutofillRules.SupportPlacementSelection selection,
                out string summary);

            Assert.True(success);
            Assert.Equal(7, selection.CandidateZ);
            Assert.Contains("search=1..11", summary);
        }

        [Fact]
        public void TrySelectSupportPlacement_PrefersLowerBottomWhenMultipleFitsExist()
        {
            Dictionary<int, PlatformAutofillRules.OccupiedZRange> ranges = new Dictionary<int, PlatformAutofillRules.OccupiedZRange>
            {
                [2] = new PlatformAutofillRules.OccupiedZRange(3, 8),
                [6] = new PlatformAutofillRules.OccupiedZRange(2, 8),
            };

            bool success = PlatformAutofillRules.TrySelectSupportPlacement(
                gapBottomZ: 2,
                desiredTopZ: 8,
                supportSizeZ: 2,
                candidateZ => ranges.TryGetValue(candidateZ, out PlatformAutofillRules.OccupiedZRange range) ? range : null,
                out PlatformAutofillRules.SupportPlacementSelection selection,
                out _);

            Assert.True(success);
            Assert.Equal(6, selection.CandidateZ);
            Assert.Equal(2, selection.BottomZ);
        }

        [Fact]
        public void TrySelectSupportPlacement_ReturnsFalseWhenNoFitExists()
        {
            Dictionary<int, PlatformAutofillRules.OccupiedZRange> ranges = new Dictionary<int, PlatformAutofillRules.OccupiedZRange>
            {
                [2] = new PlatformAutofillRules.OccupiedZRange(2, 5),
                [3] = new PlatformAutofillRules.OccupiedZRange(1, 6),
            };

            bool success = PlatformAutofillRules.TrySelectSupportPlacement(
                gapBottomZ: 2,
                desiredTopZ: 6,
                supportSizeZ: 3,
                candidateZ => ranges.TryGetValue(candidateZ, out PlatformAutofillRules.OccupiedZRange range) ? range : null,
                out PlatformAutofillRules.SupportPlacementSelection _,
                out string summary);

            Assert.False(success);
            Assert.Contains("sizeZ=3", summary);
        }

        [Fact]
        public void TrySelectSupportPlacement_ReportsEmptyCandidateListWhenResolverReturnsNothing()
        {
            bool success = PlatformAutofillRules.TrySelectSupportPlacement(
                gapBottomZ: 2,
                desiredTopZ: 6,
                supportSizeZ: 3,
                candidateZ => null,
                out PlatformAutofillRules.SupportPlacementSelection _,
                out string summary);

            Assert.False(success);
            Assert.Contains("candidates=[]", summary);
        }

        private struct SupportPlacementStub
        {
            public SupportPlacementStub(string name, int bottomZ, int x, int y)
            {
                Name = name;
                BottomZ = bottomZ;
                X = x;
                Y = y;
            }

            public string Name { get; }
            public int BottomZ { get; }
            public int X { get; }
            public int Y { get; }
        }
    }
}
