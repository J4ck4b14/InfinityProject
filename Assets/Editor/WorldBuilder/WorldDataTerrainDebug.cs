using System;
using InfinityProject.World.Climate;
using InfinityProject.World.Flora;
using InfinityProject.World.Geography;
using InfinityProject.World.Ground;
using InfinityProject.World.Hydrology;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds diagnostic textures and projects them onto a temporary Terrain copy.
/// The source Terrain and its material are never modified.
/// </summary>
internal static class WorldDataTerrainDebug
{
    public enum MapKind
    {
        Height,
        Slope,
        Aspect,
        FlowDirection,
        Accumulation,
        Basins,
        RawSinks,
        DepressionDepth,
        Channels,
        RelativeTwi,
        RunOnPotential,
        WaterAvailabilityPotential,
        SoilRetention,
        Temperature,
        PrecipitationPotential,
        FloraSuitability,
        FloraBiomass,
        FloraLimitingFactor
    }

    private static readonly int DebugMapId = Shader.PropertyToID("_DebugMap");
    private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
    private static readonly int ReliefShadingId = Shader.PropertyToID("_ReliefShading");

    private static Terrain _sourceTerrain;
    private static GameObject _previewObject;
    private static Terrain _previewTerrain;
    private static Material _previewMaterial;
    private static Texture2D _debugTexture;

    public static bool IsActive => _previewObject != null;

    public static void Show(
        Terrain sourceTerrain,
        GeographyData geography,
        HydrologyData hydrology,
        GroundConditionData ground,
        ClimateData climate,
        FloraData flora,
        int floraSpeciesIndex,
        MapKind map,
        float channelThresholdSquareMeters,
        float opacity,
        float reliefShading,
        float liftMeters)
    {
        if (sourceTerrain == null) throw new ArgumentNullException(nameof(sourceTerrain));
        if (IsGeographyMap(map) && geography == null) throw new InvalidOperationException("Geography data is required for this debug map.");
        if (IsHydrologyMap(map) && hydrology == null) throw new InvalidOperationException("Hydrology data is required for this debug map.");
        if (IsGroundMap(map) && ground == null) throw new InvalidOperationException("Ground-condition data is required for this debug map.");
        if (IsClimateMap(map) && climate == null) throw new InvalidOperationException("Climate data is required for this debug map.");
        if (IsFloraMap(map) && (flora == null || flora.SpeciesCount == 0)) throw new InvalidOperationException("Flora data is required for this debug map.");

        EnsurePreviewTerrain(sourceTerrain);
        RebuildTexture(geography, hydrology, ground, climate, flora, floraSpeciesIndex, map, channelThresholdSquareMeters);
        _previewMaterial.SetTexture(DebugMapId, _debugTexture);
        _previewMaterial.SetFloat(OpacityId, Mathf.Clamp01(opacity));
        _previewMaterial.SetFloat(ReliefShadingId, Mathf.Clamp01(reliefShading));

        Vector3 position = sourceTerrain.transform.position;
        position.y += Mathf.Max(0.001f, liftMeters);
        _previewObject.transform.SetPositionAndRotation(position, sourceTerrain.transform.rotation);
        _previewObject.transform.localScale = sourceTerrain.transform.localScale;
        SceneView.RepaintAll();
    }

    public static void UpdateAppearance(float opacity, float reliefShading, float liftMeters)
    {
        if (_previewMaterial == null || _previewObject == null || _sourceTerrain == null) return;
        _previewMaterial.SetFloat(OpacityId, Mathf.Clamp01(opacity));
        _previewMaterial.SetFloat(ReliefShadingId, Mathf.Clamp01(reliefShading));
        Vector3 position = _sourceTerrain.transform.position;
        position.y += Mathf.Max(0.001f, liftMeters);
        _previewObject.transform.SetPositionAndRotation(position, _sourceTerrain.transform.rotation);
        _previewObject.transform.localScale = _sourceTerrain.transform.localScale;
        SceneView.RepaintAll();
    }

    public static void Clear()
    {
        if (_previewObject != null) UnityEngine.Object.DestroyImmediate(_previewObject);
        if (_debugTexture != null) UnityEngine.Object.DestroyImmediate(_debugTexture);
        if (_previewMaterial != null) UnityEngine.Object.DestroyImmediate(_previewMaterial);
        _sourceTerrain = null;
        _previewObject = null;
        _previewTerrain = null;
        _previewMaterial = null;
        _debugTexture = null;
        SceneView.RepaintAll();
    }

    private static void EnsurePreviewTerrain(Terrain sourceTerrain)
    {
        if (_sourceTerrain == sourceTerrain && _previewObject != null && _previewTerrain != null && _previewMaterial != null)
        {
            _previewTerrain.terrainData = sourceTerrain.terrainData;
            return;
        }

        Clear();
        Shader shader = Shader.Find("Hidden/InfinityProject/Terrain/DebugDataOverlay");
        if (shader == null) throw new InvalidOperationException("Infinity terrain debug shader could not be found or has not compiled.");

        _sourceTerrain = sourceTerrain;
        _previewMaterial = new Material(shader) { name = "Infinity World Data Debug Material", hideFlags = HideFlags.HideAndDontSave };
        _previewObject = new GameObject("Infinity World Data Debug Preview") { hideFlags = HideFlags.HideAndDontSave };
        _previewTerrain = _previewObject.AddComponent<Terrain>();
        _previewTerrain.terrainData = sourceTerrain.terrainData;
        _previewTerrain.materialTemplate = _previewMaterial;
        _previewTerrain.drawTreesAndFoliage = false;
        _previewTerrain.drawInstanced = sourceTerrain.drawInstanced;
        _previewTerrain.heightmapPixelError = sourceTerrain.heightmapPixelError;
        _previewTerrain.basemapDistance = sourceTerrain.basemapDistance;
        _previewTerrain.allowAutoConnect = false;
        _previewTerrain.groupingID = int.MinValue + 731;
    }

    private static void RebuildTexture(
        GeographyData geography,
        HydrologyData hydrology,
        GroundConditionData ground,
        ClimateData climate,
        FloraData flora,
        int floraSpeciesIndex,
        MapKind map,
        float channelThresholdSquareMeters)
    {
        if (_debugTexture != null)
        {
            UnityEngine.Object.DestroyImmediate(_debugTexture);
            _debugTexture = null;
        }

        int resolution = IsGeographyMap(map) ? geography.Resolution :
            IsHydrologyMap(map) ? hydrology.Resolution :
            IsGroundMap(map) ? ground.Resolution :
            IsClimateMap(map) ? climate.Resolution : flora.Resolution;

        _debugTexture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
        {
            name = $"Infinity Terrain Debug — {map}",
            filterMode = IsDiscreteMap(map) ? FilterMode.Point : FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        var pixels = new Color32[resolution * resolution];
        if (IsGeographyMap(map)) BuildGeographyPixels(geography, map, pixels);
        else if (IsHydrologyMap(map)) BuildHydrologyPixels(hydrology, map, channelThresholdSquareMeters, pixels);
        else if (IsGroundMap(map)) BuildGroundPixels(ground, map, pixels);
        else if (IsClimateMap(map)) BuildClimatePixels(climate, map, pixels);
        else BuildFloraPixels(flora, floraSpeciesIndex, map, pixels);

        _debugTexture.SetPixels32(pixels);
        _debugTexture.Apply(false, true);
    }

    private static void BuildGeographyPixels(GeographyData data, MapKind map, Color32[] pixels)
    {
        for (int i = 0; i < pixels.Length; i++)
        {
            Color color;
            switch (map)
            {
                case MapKind.Slope:
                    float slope = Mathf.Clamp01(data.SlopeDegrees[i] / 60f);
                    color = new Color(slope, slope, slope, 1f);
                    break;
                case MapKind.Aspect:
                    color = Color.HSVToRGB(Mathf.Repeat(data.AspectDegrees[i], 360f) / 360f, 0.75f, 0.95f);
                    color.a = 1f;
                    break;
                default:
                    float h = Mathf.Clamp01(data.NormalizedHeight[i]);
                    color = new Color(h, h, h, 1f);
                    break;
            }
            pixels[i] = color;
        }
    }

    private static void BuildHydrologyPixels(HydrologyData data, MapKind map, float channelThresholdSquareMeters, Color32[] pixels)
    {
        float worldLog = Mathf.Log10(data.WorldAreaSquareMeters + 1f);
        float localArea = Mathf.Max(data.SampleSpacingX * data.SampleSpacingZ, 1f);
        float accumulationDenominator = Mathf.Log10(data.WorldAreaSquareMeters / localArea + 1f);

        for (int i = 0; i < pixels.Length; i++)
        {
            Color color;
            switch (map)
            {
                case MapKind.FlowDirection:
                    if (data.FlowReceiver[i] < 0) color = Color.black;
                    else color = Color.HSVToRGB(data.GetFlowBearingDegrees(i) / 360f, 0.85f, 0.95f);
                    color.a = 1f;
                    break;
                case MapKind.Basins:
                    color = Color.HSVToRGB(Mathf.Repeat(data.BasinId[i] * 0.61803398875f, 1f), 0.55f, 0.90f);
                    color.a = 1f;
                    break;
                case MapKind.RawSinks:
                    if (data.IsRawSink(i)) color = new Color(1f, 0.08f, 0.04f, 1f);
                    else if (data.IsOutlet(i)) color = new Color(0.05f, 0.85f, 1f, 1f);
                    else color = new Color(0f, 0f, 0f, 0f);
                    break;
                case MapKind.DepressionDepth:
                    float depth = data.DepressionDepthMeters[i];
                    if (depth <= 0f) color = new Color(0f, 0f, 0f, 0f);
                    else
                    {
                        float denominator = Mathf.Max(data.MaxDepressionDepthMeters, 0.0001f);
                        float t = Mathf.Clamp01(Mathf.Log10(depth + 1f) / Mathf.Log10(denominator + 1f));
                        color = Color.Lerp(new Color(0.04f, 0.12f, 0.26f, 0.35f), new Color(0.25f, 0.9f, 1f, 1f), t);
                    }
                    break;
                case MapKind.Channels:
                    if (data.IsCandidateConditionedTransit(i, channelThresholdSquareMeters))
                    {
                        float t = Mathf.Clamp01(Mathf.Log10(data.FlowAccumulationSquareMeters[i] + 1f) / Mathf.Max(worldLog, 0.0001f));
                        color = Color.Lerp(new Color(0.06f, 0.16f, 0.75f, 0.72f), new Color(0.16f, 0.46f, 1f, 0.92f), t);
                    }
                    else if (data.IsCandidateOpenChannel(i, channelThresholdSquareMeters))
                    {
                        float t = Mathf.Clamp01(Mathf.Log10(data.FlowAccumulationSquareMeters[i] + 1f) / Mathf.Max(worldLog, 0.0001f));
                        color = Color.Lerp(new Color(0.02f, 0.42f, 0.54f, 0.78f), new Color(0f, 1f, 1f, 1f), t);
                    }
                    else color = new Color(0f, 0f, 0f, 0f);
                    break;
                default:
                    float accumulation = Mathf.Max(data.FlowAccumulationSquareMeters[i], localArea);
                    float numerator = Mathf.Log10(accumulation / localArea + 1f);
                    float a = Mathf.Clamp01(numerator / Mathf.Max(accumulationDenominator, 0.0001f));
                    a = Mathf.Pow(a, 1.8f);
                    color = Color.Lerp(new Color(0.005f, 0.008f, 0.015f, 1f), new Color(0.4f, 0.9f, 1f, 1f), a);
                    break;
            }
            pixels[i] = color;
        }
        if (map == MapKind.Channels) PaintChannelSourceMarkers(data, channelThresholdSquareMeters, pixels);
    }

    private static void PaintChannelSourceMarkers(HydrologyData data, float channelThresholdSquareMeters, Color32[] pixels)
    {
        int resolution = data.Resolution;
        Color32 marker = (Color32)new Color(0f, 1f, 1f, 1f);
        for (int i = 0; i < pixels.Length; i++)
        {
            if (!data.IsCandidateChannelSource(i, channelThresholdSquareMeters)) continue;
            int x = i % resolution;
            int y = i / resolution;
            Paint(x, y); Paint(x - 1, y); Paint(x + 1, y); Paint(x, y - 1); Paint(x, y + 1);
        }
        void Paint(int x, int y)
        {
            if (x < 0 || y < 0 || x >= resolution || y >= resolution) return;
            pixels[y * resolution + x] = marker;
        }
    }

    private static void BuildGroundPixels(GroundConditionData data, MapKind map, Color32[] pixels)
    {
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = map switch
            {
                MapKind.RelativeTwi => (Color32)GroundConditionDebugWindow.WetnessColor(data.GetRelativeWetness01(i)),
                MapKind.RunOnPotential => (Color32)GroundConditionDebugWindow.RunOnColor(data.RunOnPotential[i]),
                MapKind.WaterAvailabilityPotential => (Color32)GroundConditionDebugWindow.WaterColor(data.WaterAvailabilityPotential[i]),
                _ => (Color32)GroundConditionDebugWindow.RetentionColor(data.SoilRetentionPotential[i])
            };
        }
    }

    private static void BuildClimatePixels(ClimateData data, MapKind map, Color32[] pixels)
    {
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = map == MapKind.Temperature
                ? (Color32)ClimateDebugWindow.TemperatureColor(data.TemperatureCelsius[i])
                : (Color32)ClimateDebugWindow.PrecipitationColor(data.PrecipitationPotential[i]);
    }

    private static void BuildFloraPixels(FloraData data, int speciesIndex, MapKind map, Color32[] pixels)
    {
        FloraSpeciesData species = data.Species[Mathf.Clamp(speciesIndex, 0, data.SpeciesCount - 1)];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = map switch
            {
                MapKind.FloraSuitability => (Color32)FloraDebugWindow.SuitabilityColor(species.EstablishmentSuitability[i]),
                MapKind.FloraBiomass => (Color32)FloraDebugWindow.BiomassColor(species.InitialBiomass[i]),
                _ => (Color32)FloraDebugWindow.LimitingFactorColor((FloraLimitingFactor)species.LimitingFactor[i])
            };
        }
    }

    private static bool IsGeographyMap(MapKind map)
        => map == MapKind.Height || map == MapKind.Slope || map == MapKind.Aspect;

    private static bool IsHydrologyMap(MapKind map)
        => map == MapKind.FlowDirection || map == MapKind.Accumulation || map == MapKind.Basins ||
           map == MapKind.RawSinks || map == MapKind.DepressionDepth || map == MapKind.Channels;

    private static bool IsGroundMap(MapKind map)
        => map == MapKind.RelativeTwi || map == MapKind.RunOnPotential || map == MapKind.WaterAvailabilityPotential || map == MapKind.SoilRetention;

    private static bool IsClimateMap(MapKind map)
        => map == MapKind.Temperature || map == MapKind.PrecipitationPotential;

    private static bool IsFloraMap(MapKind map)
        => map == MapKind.FloraSuitability || map == MapKind.FloraBiomass || map == MapKind.FloraLimitingFactor;

    private static bool IsDiscreteMap(MapKind map)
        => map == MapKind.Basins || map == MapKind.RawSinks || map == MapKind.Channels ||
           map == MapKind.FlowDirection || map == MapKind.FloraLimitingFactor;
}
