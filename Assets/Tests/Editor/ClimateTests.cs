using InfinityProject.World.Climate;
using InfinityProject.World.Geography;
using NUnit.Framework;

namespace InfinityProject.Tests
{
    public class ClimateTests
    {
        [Test]
        public void ClimateGenerator_IsDeterministic()
        {
            GeographyData geography = MakeFlat(9, 800f, 300f, 5101);
            var settings = new ClimateGenerationSettings();
            ClimateData a = ClimateGenerator.Generate(geography, settings);
            ClimateData b = ClimateGenerator.Generate(geography, settings);
            CollectionAssert.AreEqual(a.TemperatureCelsius, b.TemperatureCelsius);
            CollectionAssert.AreEqual(a.PrecipitationPotential, b.PrecipitationPotential);
        }

        [Test]
        public void ClimateGenerator_HigherElevationIsColder()
        {
            GeographyData geography = MakeEastRisingPlane(3, 200f, 1000f, 5102);
            var settings = new ClimateGenerationSettings
            {
                BaseTemperatureCelsius = 20f,
                LapseRateCelsiusPerKilometre = 6.5f,
                SouthToNorthTemperatureDeltaCelsius = 0f
            };
            ClimateData data = ClimateGenerator.Generate(geography, settings);
            Assert.Greater(data.GetTemperatureCelsius(0, 1), data.GetTemperatureCelsius(2, 1));
            Assert.AreEqual(6.5f, data.GetTemperatureCelsius(0, 1) - data.GetTemperatureCelsius(2, 1), 0.01f);
        }

        [Test]
        public void ClimateGenerator_SouthToNorthDeltaFollowsWorldPosition()
        {
            GeographyData geography = MakeFlat(3, 200f, 100f, 5103);
            var settings = new ClimateGenerationSettings
            {
                LapseRateCelsiusPerKilometre = 0f,
                SouthToNorthTemperatureDeltaCelsius = -4f
            };
            ClimateData data = ClimateGenerator.Generate(geography, settings);
            Assert.AreEqual(4f, data.GetTemperatureCelsius(1, 0) - data.GetTemperatureCelsius(1, 2), 0.001f);
        }

        [Test]
        public void ClimateGenerator_UpwindBarrierReducesLeewardPrecipitation()
        {
            const int resolution = 9;
            float[] heights = new float[resolution * resolution];
            for (int y = 0; y < resolution; y++)
                heights[y * resolution + 4] = 1f;
            GeographyData geography = new(resolution, 800f, 800f, 300f, 5104, heights);
            var settings = new ClimateGenerationSettings
            {
                BackgroundPrecipitationPotential = 0.7f,
                MoistureWindFromDegrees = 270f,
                WindwardBoost = 0f,
                BroadOrographicBoost = 0f,
                RainShadowStrength = 0.6f,
                UpwindSampleRangeMeters = 700f,
                UpwindSampleCount = 8,
                OrographicReliefScaleMeters = 100f
            };
            ClimateData data = ClimateGenerator.Generate(geography, settings);
            Assert.Greater(data.GetPrecipitationPotential(2, 4), data.GetPrecipitationPotential(6, 4));
        }

        [Test]
        public void ClimateGenerator_DisablingTerrainModifiersLeavesUniformBackgroundPrecipitation()
        {
            GeographyData geography = MakeEastRisingPlane(9, 800f, 600f, 5105);
            var settings = new ClimateGenerationSettings
            {
                BackgroundPrecipitationPotential = 0.37f,
                WindwardBoost = 0f,
                BroadOrographicBoost = 0f,
                RainShadowStrength = 0f
            };
            ClimateData data = ClimateGenerator.Generate(geography, settings);
            foreach (float value in data.PrecipitationPotential)
                Assert.AreEqual(0.37f, value, 0.000001f);
        }

        [Test]
        public void ClimateGenerator_OutputsRemainFiniteAndPrecipitationNormalized()
        {
            GeographyData geography = GeographyGenerator.Generate(new GeographyGenerationSettings
            {
                Resolution = 32,
                WidthMeters = 1200f,
                LengthMeters = 1000f,
                MaxElevationMeters = 500f,
                Seed = 5106
            });
            ClimateData data = ClimateGenerator.Generate(geography);
            for (int i = 0; i < data.TemperatureCelsius.Length; i++)
            {
                Assert.IsFalse(float.IsNaN(data.TemperatureCelsius[i]));
                Assert.IsFalse(float.IsInfinity(data.TemperatureCelsius[i]));
                Assert.That(data.PrecipitationPotential[i], Is.InRange(0f, 1f));
                Assert.IsFalse(float.IsNaN(data.PrecipitationPotential[i]));
                Assert.IsFalse(float.IsInfinity(data.PrecipitationPotential[i]));
            }
        }

        private static GeographyData MakeFlat(int resolution, float width, float maxElevation, int seed)
            => new(resolution, width, width, maxElevation, seed, new float[resolution * resolution]);

        private static GeographyData MakeEastRisingPlane(int resolution, float width, float maxElevation, int seed)
        {
            float[] heights = new float[resolution * resolution];
            for (int y = 0; y < resolution; y++)
                for (int x = 0; x < resolution; x++)
                    heights[y * resolution + x] = x / (float)(resolution - 1);
            return new GeographyData(resolution, width, width, maxElevation, seed, heights);
        }
    }
}
