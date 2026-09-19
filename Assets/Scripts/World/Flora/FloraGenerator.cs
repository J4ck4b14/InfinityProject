using System;
using InfinityProject.World.Climate;
using InfinityProject.World.Geography;
using InfinityProject.World.Ground;
using UnityEngine;

namespace InfinityProject.World.Flora
{
    /// <summary>Flora V1.1: environment-derived potential using temperature + Water Availability + substrate retention.</summary>
    public static class FloraGenerator
    {
        private const float LimitingTieEpsilon = 0.0005f;
        public static FloraData Generate(GeographyData geography, GroundConditionData ground, ClimateData climate, FloraGenerationSettings settings = null)
        {
            if (geography == null) throw new ArgumentNullException(nameof(geography)); if (ground == null) throw new ArgumentNullException(nameof(ground)); if (climate == null) throw new ArgumentNullException(nameof(climate));
            ValidateCompatible(geography, ground, climate); settings ??= new FloraGenerationSettings(); settings = settings.Clone(); settings.Validate();
            if (settings.Species.Count == 0) return new FloraData(geography.Resolution, geography.WidthMeters, geography.LengthMeters, geography.Seed, Array.Empty<FloraSpeciesData>());
            int count = geography.Resolution * geography.Resolution; var layers = new FloraSpeciesData[settings.Species.Count];
            for (int s = 0; s < settings.Species.Count; s++)
            {
                FloraSpeciesProfile p = settings.Species[s]; var suitability = new float[count]; var occupancy = new float[count];
                var capacity = new float[count]; var biomass = new float[count]; var limiting = new byte[count];
                for (int i = 0; i < count; i++)
                {
                    float temperature = TriangularResponse(climate.TemperatureCelsius[i], p.MinimumTemperatureCelsius, p.OptimalTemperatureCelsius, p.MaximumTemperatureCelsius);
                    float water = TriangularResponse(ground.WaterAvailabilityPotential[i], p.MinimumWaterAvailabilityPotential, p.OptimalWaterAvailabilityPotential, p.MaximumWaterAvailabilityPotential);
                    float retention = RisingResponse(ground.SoilRetentionPotential[i], p.MinimumSoilRetentionPotential, p.FullSoilRetentionPotential);
                    float minimum = Mathf.Clamp01(Mathf.Min(temperature, water, retention));
                    suitability[i] = minimum; limiting[i] = (byte)DetermineLimitingFactor(minimum, temperature, water, retention);
                    occupancy[i] = minimum > p.EstablishmentThreshold ? Mathf.InverseLerp(p.EstablishmentThreshold, 1f, minimum) : 0f;
                    capacity[i] = p.MaximumBiomassKgPerSquareMeter * minimum; biomass[i] = capacity[i] * occupancy[i] * p.InitialOccupancyFraction;
                    if (!IsFinite(minimum) || !IsFinite(occupancy[i]) || !IsFinite(capacity[i]) || !IsFinite(biomass[i])) throw new InvalidOperationException($"Flora became non-finite for species '{p.StableId}' at sample {i}.");
                }
                layers[s] = new FloraSpeciesData(p.StableId, p.DisplayName, suitability, occupancy, capacity, biomass, limiting);
            }
            return new FloraData(geography.Resolution, geography.WidthMeters, geography.LengthMeters, geography.Seed, layers);
        }

        public static float TriangularResponse(float value, float minimum, float optimum, float maximum)
        {
            if (maximum < minimum) (minimum, maximum) = (maximum, minimum); optimum = Mathf.Clamp(optimum, minimum, maximum);
            if (value < minimum || value > maximum) return 0f; if (Mathf.Abs(maximum - minimum) <= 0.000001f) return Mathf.Approximately(value, minimum) ? 1f : 0f;
            if (value <= optimum) return Mathf.Abs(optimum - minimum) <= 0.000001f ? 1f : Mathf.InverseLerp(minimum, optimum, value);
            return Mathf.Abs(maximum - optimum) <= 0.000001f ? 1f : 1f - Mathf.InverseLerp(optimum, maximum, value);
        }
        public static float RisingResponse(float value, float minimum, float full)
        {
            if (full < minimum) full = minimum; if (value < minimum) return 0f; if (Mathf.Abs(full - minimum) <= 0.000001f) return 1f;
            return Mathf.Clamp01(Mathf.InverseLerp(minimum, full, value));
        }
        private static FloraLimitingFactor DetermineLimitingFactor(float minimum, float temperature, float water, float retention)
        {
            if (minimum >= 1f - LimitingTieEpsilon) return FloraLimitingFactor.None; int ties = 0; FloraLimitingFactor factor = FloraLimitingFactor.None;
            Check(temperature, FloraLimitingFactor.Temperature); Check(water, FloraLimitingFactor.WaterAvailability); Check(retention, FloraLimitingFactor.SoilRetention);
            return ties > 1 ? FloraLimitingFactor.Multiple : factor;
            void Check(float value, FloraLimitingFactor candidate) { if (Mathf.Abs(value - minimum) > LimitingTieEpsilon) return; ties++; factor = candidate; }
        }
        private static void ValidateCompatible(GeographyData g, GroundConditionData ground, ClimateData c)
        {
            if (ground.Resolution != g.Resolution || c.Resolution != g.Resolution) throw new ArgumentException("Geography, Ground Conditions and Climate must use the same resolution.");
            if (Mathf.Abs(ground.WidthMeters - g.WidthMeters) > 0.001f || Mathf.Abs(ground.LengthMeters - g.LengthMeters) > 0.001f || Mathf.Abs(c.WidthMeters - g.WidthMeters) > 0.001f || Mathf.Abs(c.LengthMeters - g.LengthMeters) > 0.001f) throw new ArgumentException("Geography, Ground Conditions and Climate must describe the same physical world extent.");
            if (ground.SourceGeographySeed != g.Seed || c.SourceGeographySeed != g.Seed) throw new ArgumentException("Ground Conditions or Climate do not belong to this Geography seed.");
        }
        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
