using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace PlatformAutofill.Tests
{
    public class PlatformAutofillRulesTests
    {
        [Fact]
        public void TryExtractFaction_ReturnsFactionSuffix()
        {
            bool success = PlatformAutofillRules.TryExtractFaction("DoublePlatform.IronTeeth", out string? faction);

            Assert.True(success);
            Assert.Equal("IronTeeth", faction);
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
