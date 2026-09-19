using InfinityProject.World.Geography;
using NUnit.Framework;
using Unity.Mathematics;

namespace InfinityProject.Tests
{
    public class GeographyTests
    {
        [Test]
        public void GeographyGenerator_IsDeterministic_ForSameSettings()
        {
            var settings = SmallSettings(seed: 4242, resolution: 48);

            GeographyData a = GeographyGenerator.Generate(settings);
            GeographyData b = GeographyGenerator.Generate(settings);

            Assert.AreEqual(a.NormalizedHeight.Length, b.NormalizedHeight.Length);
            for (int i = 0; i < a.NormalizedHeight.Length; i++)
                Assert.AreEqual(a.NormalizedHeight[i], b.NormalizedHeight[i], 0f, $"Mismatch at sample {i}");
        }

        [Test]
        public void GeographyGenerator_DifferentSeeds_ChangeTheWorld()
        {
            GeographyData a = GeographyGenerator.Generate(SmallSettings(seed: 10, resolution: 48));
            GeographyData b = GeographyGenerator.Generate(SmallSettings(seed: 11, resolution: 48));

            bool anyDifference = false;
            for (int i = 0; i < a.NormalizedHeight.Length; i++)
            {
                if (math.abs(a.NormalizedHeight[i] - b.NormalizedHeight[i]) > 0.000001f)
                {
                    anyDifference = true;
                    break;
                }
            }

            Assert.IsTrue(anyDifference, "Changing the seed should change generated geography.");
        }

        [Test]
        public void GeographyGenerator_ProducesFiniteBoundedPhysicalData()
        {
            GeographyData data = GeographyGenerator.Generate(SmallSettings(seed: 99, resolution: 64));

            for (int i = 0; i < data.NormalizedHeight.Length; i++)
            {
                Assert.IsFalse(float.IsNaN(data.NormalizedHeight[i]));
                Assert.IsFalse(float.IsInfinity(data.NormalizedHeight[i]));
                Assert.That(data.NormalizedHeight[i], Is.InRange(0f, 1f));

                Assert.IsFalse(float.IsNaN(data.SlopeDegrees[i]));
                Assert.That(data.SlopeDegrees[i], Is.InRange(0f, 90f));

                Assert.IsFalse(float.IsNaN(data.AspectDegrees[i]));
                Assert.That(data.AspectDegrees[i], Is.InRange(0f, 360f));
            }
        }

        [Test]
        public void GeographyData_DerivesSlopeFromPhysicalMetres()
        {
            const int resolution = 3;
            const float width = 2f;
            const float length = 2f;
            const float maxElevation = 10f;

            // Elevation rises 1 metre per metre toward +X:
            // normalized heights 0.0, 0.1, 0.2 -> 0m, 1m, 2m.
            float[] heights =
            {
                0.0f, 0.1f, 0.2f,
                0.0f, 0.1f, 0.2f,
                0.0f, 0.1f, 0.2f
            };

            var data = new GeographyData(resolution, width, length, maxElevation, 1, heights);

            Assert.AreEqual(45f, data.GetSlopeDegrees(1, 1), 0.01f);
            Assert.AreEqual(270f, data.GetAspectDegrees(1, 1), 0.01f,
                "The surface rises east, so downhill should point west (270°).");
        }

        [Test]
        public void GeographyData_GridAndWorldCoordinatesSharePhysicalScale()
        {
            var data = GeographyGenerator.Generate(SmallSettings(seed: 123, resolution: 101));

            float2 centreGrid = data.LocalMetersToGrid(new float2(data.WidthMeters * 0.5f, data.LengthMeters * 0.5f));
            Assert.AreEqual(50f, centreGrid.x, 0.001f);
            Assert.AreEqual(50f, centreGrid.y, 0.001f);

            float2 corner = data.GridToLocalMeters(data.Resolution - 1, data.Resolution - 1);
            Assert.AreEqual(data.WidthMeters, corner.x, 0.001f);
            Assert.AreEqual(data.LengthMeters, corner.y, 0.001f);
        }

        [Test]
        public void GeographyGenerator_PhysicalShape_IsApproximatelyResolutionIndependent()
        {
            var lowSettings = SmallSettings(seed: 7301, resolution: 64);
            var highSettings = SmallSettings(seed: 7301, resolution: 128);

            GeographyData low = GeographyGenerator.Generate(lowSettings);
            GeographyData high = GeographyGenerator.Generate(highSettings);

            float2[] positions =
            {
                new float2(120f, 210f),
                new float2(500f, 650f),
                new float2(920f, 340f),
                new float2(1100f, 1040f)
            };

            foreach (float2 position in positions)
            {
                float lowHeight = low.SampleNormalizedHeight(position);
                float highHeight = high.SampleNormalizedHeight(position);
                Assert.AreEqual(lowHeight, highHeight, 0.02f,
                    $"Physical terrain changed too much with sampling resolution at {position}.");
            }
        }

        private static GeographyGenerationSettings SmallSettings(int seed, int resolution)
        {
            return new GeographyGenerationSettings
            {
                Seed = seed,
                Resolution = resolution,
                WidthMeters = 1200f,
                LengthMeters = 1200f,
                MaxElevationMeters = 300f,
                BaseElevation = 0.30f,
                MacroScaleMeters = 1400f,
                MacroAmplitude = 0.25f,
                MountainScaleMeters = 650f,
                MountainAmplitude = 0.35f,
                MountainThreshold = 0.56f,
                MountainBlend = 0.15f,
                WarpScaleMeters = 900f,
                WarpStrengthMeters = 120f,
                DetailScaleMeters = 150f,
                DetailAmplitude = 0.04f,
                Octaves = 4,
                Persistence = 0.5f,
                Lacunarity = 2f
            };
        }
    }
}
