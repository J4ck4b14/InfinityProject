using InfinityProject.World.Geography;
using InfinityProject.World.Hydrology;
using NUnit.Framework;

namespace InfinityProject.Tests
{
    public class HydrologyTests
    {
        [Test]
        public void HydrologyGenerator_FunnelReachesBoundaryOutlet()
        {
            GeographyData geography = MakeFunnelToSouthEast(9, 800f, 100f);
            HydrologyData hydrology = HydrologyGenerator.Generate(geography);

            Assert.AreEqual(0, hydrology.RawSinkCount);
            Assert.Greater(hydrology.OutletCount, 0);

            for (int start = 0; start < hydrology.FlowReceiver.Length; start++)
                AssertPathReachesOutlet(hydrology, start);
        }

        [Test]
        public void HydrologyGenerator_ConservesContributingAreaAtOutlets()
        {
            GeographyData geography = MakeFunnelToSouthEast(17, 1600f, 200f);
            HydrologyData hydrology = HydrologyGenerator.Generate(geography);

            float outletArea = 0f;
            for (int i = 0; i < hydrology.FlowReceiver.Length; i++)
            {
                if (hydrology.FlowReceiver[i] < 0)
                    outletArea += hydrology.FlowAccumulationSquareMeters[i];
            }

            float expected = geography.WidthMeters * geography.LengthMeters;
            Assert.AreEqual(expected, outletArea, expected * 0.00001f);
        }

        [Test]
        public void HydrologyGenerator_BowlPreservesRawSinkButRoutesOverflowToBoundary()
        {
            GeographyData geography = MakeBowl(resolution: 9, widthMeters: 800f, maxElevationMeters: 100f);
            HydrologyData hydrology = HydrologyGenerator.Generate(geography);
            int center = hydrology.Index(4, 4);

            Assert.IsTrue(hydrology.IsRawSink(center));
            Assert.AreEqual(1, hydrology.RawSinkCount);
            Assert.Greater(hydrology.DepressionDepthMeters[center], 0f);
            Assert.Greater(hydrology.MaxDepressionDepthMeters, 0f);
            Assert.Greater(hydrology.DepressionSampleCount, 0);
            Assert.GreaterOrEqual(hydrology.FlowReceiver[center], 0, "The raw sink should route through its spill path after conditioning.");
            AssertPathReachesOutlet(hydrology, center);
        }

        [Test]
        public void HydrologyGenerator_HasNoInternalConditionedTerminals()
        {
            GeographyData geography = MakeBowl(17, 1600f, 200f);
            HydrologyData hydrology = HydrologyGenerator.Generate(geography);
            int resolution = hydrology.Resolution;

            for (int y = 1; y < resolution - 1; y++)
            {
                for (int x = 1; x < resolution - 1; x++)
                {
                    int index = hydrology.Index(x, y);
                    Assert.GreaterOrEqual(hydrology.FlowReceiver[index], 0, $"Interior sample ({x},{y}) may not terminate after conditioning.");
                }
            }
        }

        [Test]
        public void HydrologyGenerator_IsDeterministic()
        {
            GeographyData geography = MakeBowl(13, 1200f, 180f);
            var settings = new HydrologyGenerationSettings
            {
                MinimumDropMeters = 0.0001f,
                ChannelInitiationAreaSquareMeters = 40000f
            };

            HydrologyData a = HydrologyGenerator.Generate(geography, settings);
            HydrologyData b = HydrologyGenerator.Generate(geography, settings);

            CollectionAssert.AreEqual(a.FlowReceiver, b.FlowReceiver);
            CollectionAssert.AreEqual(a.BasinId, b.BasinId);
            CollectionAssert.AreEqual(a.TerminalType, b.TerminalType);
            CollectionAssert.AreEqual(a.RawSinkMask, b.RawSinkMask);
            CollectionAssert.AreEqual(a.DepressionDepthMeters, b.DepressionDepthMeters);
            CollectionAssert.AreEqual(a.FlowAccumulationSquareMeters, b.FlowAccumulationSquareMeters);
        }

        [Test]
        public void HydrologyGenerator_WorldAreaIsResolutionIndependent()
        {
            GeographyData low = MakeFunnelToSouthEast(17, 1600f, 200f);
            GeographyData high = MakeFunnelToSouthEast(33, 1600f, 200f);
            HydrologyData lowHydrology = HydrologyGenerator.Generate(low);
            HydrologyData highHydrology = HydrologyGenerator.Generate(high);

            float expectedArea = 1600f * 1600f;
            Assert.AreEqual(expectedArea, SumOutletArea(lowHydrology), expectedArea * 0.00001f);
            Assert.AreEqual(expectedArea, SumOutletArea(highHydrology), expectedArea * 0.00001f);
        }

        [Test]
        public void HydrologyGenerator_ChannelThresholdUsesPhysicalArea()
        {
            GeographyData geography = MakeFunnelToSouthEast(33, 1600f, 200f);
            HydrologyData hydrology = HydrologyGenerator.Generate(geography);
            float threshold = 0.05f * 1_000_000f;
            int channels = 0;

            for (int i = 0; i < hydrology.FlowReceiver.Length; i++)
            {
                if (hydrology.IsCandidateChannel(i, threshold))
                    channels++;
            }

            Assert.Greater(channels, 0, "A simple convergent funnel should produce candidate channels at a 0.05 km² threshold.");
        }

        [Test]
        public void HydrologyData_ChannelSourcesAreThresholdEntryPoints()
        {
            GeographyData geography = MakeFunnelToSouthEast(33, 1600f, 200f);
            HydrologyData hydrology = HydrologyGenerator.Generate(geography);
            float threshold = 0.05f * 1_000_000f;
            int sources = hydrology.CountCandidateChannelSources(threshold);

            Assert.Greater(sources, 0, "The diagnostic channel network should expose at least one channel head.");

            for (int i = 0; i < hydrology.FlowReceiver.Length; i++)
            {
                if (!hydrology.IsCandidateChannelSource(i, threshold))
                    continue;

                hydrology.Coordinates(i, out int x, out int y);
                for (int oy = -1; oy <= 1; oy++)
                {
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        if (ox == 0 && oy == 0)
                            continue;
                        int nx = x + ox;
                        int ny = y + oy;
                        if (nx < 0 || ny < 0 || nx >= hydrology.Resolution || ny >= hydrology.Resolution)
                            continue;

                        int upstream = hydrology.Index(nx, ny);
                        Assert.IsFalse(
                            hydrology.FlowReceiver[upstream] == i && hydrology.IsCandidateChannel(upstream, threshold),
                            "A channel head may not already have an upstream thresholded channel sample.");
                    }
                }
            }
        }


        [Test]
        public void HydrologyData_OpenChannelsExcludeConditionedDepressionTransit()
        {
            GeographyData geography = MakeBowl(17, 1600f, 200f);
            HydrologyData hydrology = HydrologyGenerator.Generate(geography);
            float threshold = 1f;
            int center = hydrology.Index(8, 8);

            Assert.IsTrue(hydrology.IsDepressionSample(center), "The bowl center must require fill-to-spill conditioning.");
            Assert.IsTrue(hydrology.IsCandidateChannel(center, threshold), "Conditioned routing still carries contributing area through the basin.");
            Assert.IsTrue(hydrology.IsCandidateConditionedTransit(center, threshold), "The thresholded basin route must be classified as conditioned transit.");
            Assert.IsFalse(hydrology.IsCandidateOpenChannel(center, threshold), "A filled-depression route may not be presented as an exposed surface stream.");
            Assert.IsFalse(hydrology.IsCandidateChannelSource(center, threshold), "A channel head may not be placed inside a conditioned depression.");

            for (int i = 0; i < hydrology.FlowReceiver.Length; i++)
            {
                if (hydrology.IsCandidateChannelSource(i, threshold))
                    Assert.IsFalse(hydrology.IsDepressionSample(i), "Every displayed channel head must lie on exposed terrain.");
            }
        }

        [Test]
        public void HydrologyData_ChannelDiagnosticPartitionsOpenAndConditionedSegments()
        {
            GeographyData geography = MakeBowl(17, 1600f, 200f);
            HydrologyData hydrology = HydrologyGenerator.Generate(geography);
            float threshold = 1f;
            int openCount = 0;
            int conditionedCount = 0;

            for (int i = 0; i < hydrology.FlowReceiver.Length; i++)
            {
                bool candidate = hydrology.IsCandidateChannel(i, threshold);
                bool open = hydrology.IsCandidateOpenChannel(i, threshold);
                bool conditioned = hydrology.IsCandidateConditionedTransit(i, threshold);

                Assert.AreEqual(candidate, open || conditioned,
                    $"Thresholded sample {i} must be represented as either open channel or conditioned transit.");
                Assert.IsFalse(open && conditioned,
                    $"Sample {i} cannot be both exposed channel and conditioned transit.");

                if (open) openCount++;
                if (conditioned) conditionedCount++;
            }

            Assert.Greater(openCount, 0, "The bowl should retain exposed thresholded routing outside its depression.");
            Assert.Greater(conditionedCount, 0, "The bowl should expose conditioned transit inside its depression.");
        }

        private static void AssertPathReachesOutlet(HydrologyData hydrology, int start)
        {
            int current = start;
            int guard = hydrology.FlowReceiver.Length + 1;
            while (hydrology.FlowReceiver[current] >= 0 && guard-- > 0)
                current = hydrology.FlowReceiver[current];

            Assert.Greater(guard, 0, $"Routing from sample {start} contains a cycle.");
            Assert.IsTrue(hydrology.IsOutlet(current), $"Routing from sample {start} must terminate at a boundary outlet.");
        }

        private static float SumOutletArea(HydrologyData hydrology)
        {
            float total = 0f;
            for (int i = 0; i < hydrology.FlowReceiver.Length; i++)
            {
                if (hydrology.FlowReceiver[i] < 0)
                    total += hydrology.FlowAccumulationSquareMeters[i];
            }
            return total;
        }

        private static GeographyData MakeFunnelToSouthEast(int resolution, float widthMeters, float maxElevationMeters)
        {
            float[] heights = new float[resolution * resolution];
            int max = resolution - 1;
            float maxDistanceSquared = 2f * max * max;

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float dx = max - x;
                    float dy = max - y;
                    heights[y * resolution + x] = (dx * dx + dy * dy) / maxDistanceSquared;
                }
            }

            return new GeographyData(resolution, widthMeters, widthMeters, maxElevationMeters, 1001, heights);
        }

        private static GeographyData MakeBowl(int resolution, float widthMeters, float maxElevationMeters)
        {
            float[] heights = new float[resolution * resolution];
            float center = (resolution - 1) * 0.5f;
            float maxDistanceSquared = 2f * center * center;

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    heights[y * resolution + x] = (dx * dx + dy * dy) / maxDistanceSquared;
                }
            }

            return new GeographyData(resolution, widthMeters, widthMeters, maxElevationMeters, 2002, heights);
        }
    }
}
