using InfinityProject.World.Climate;
using InfinityProject.World.Flora;
using InfinityProject.World.Geography;
using InfinityProject.World.Ground;
using InfinityProject.World.Hydrology;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;

/// <summary>
/// Scene View overlay that projects Infinity simulation diagnostics directly onto the 3D Terrain.
/// </summary>
[Overlay(typeof(SceneView), "World Data Debug", true)]
[Icon("d_Terrain Icon")]
public sealed class WorldDataDebugOverlay : IMGUIOverlay, ITransientOverlay
{
    private enum DataDomain { Geography, Hydrology, Ground, Climate, Flora }

    private static readonly string[] GeographyLabels = { "Height", "Slope", "Aspect" };
    private static readonly WorldDataTerrainDebug.MapKind[] GeographyMaps =
    {
        WorldDataTerrainDebug.MapKind.Height,
        WorldDataTerrainDebug.MapKind.Slope,
        WorldDataTerrainDebug.MapKind.Aspect
    };

    private static readonly string[] HydrologyLabels = { "Direction", "Accumulation", "Basins", "Raw Sinks", "Depressions", "Channels" };
    private static readonly WorldDataTerrainDebug.MapKind[] HydrologyMaps =
    {
        WorldDataTerrainDebug.MapKind.FlowDirection,
        WorldDataTerrainDebug.MapKind.Accumulation,
        WorldDataTerrainDebug.MapKind.Basins,
        WorldDataTerrainDebug.MapKind.RawSinks,
        WorldDataTerrainDebug.MapKind.DepressionDepth,
        WorldDataTerrainDebug.MapKind.Channels
    };

    private static readonly string[] GroundLabels = { "Relative TWI", "Run-on", "Water Availability", "Soil Retention" };
    private static readonly WorldDataTerrainDebug.MapKind[] GroundMaps =
    {
        WorldDataTerrainDebug.MapKind.RelativeTwi,
        WorldDataTerrainDebug.MapKind.RunOnPotential,
        WorldDataTerrainDebug.MapKind.WaterAvailabilityPotential,
        WorldDataTerrainDebug.MapKind.SoilRetention
    };

    private static readonly string[] ClimateLabels = { "Temperature", "Precipitation" };
    private static readonly WorldDataTerrainDebug.MapKind[] ClimateMaps =
    {
        WorldDataTerrainDebug.MapKind.Temperature,
        WorldDataTerrainDebug.MapKind.PrecipitationPotential
    };

    private static readonly string[] FloraLabels = { "Suitability", "Initial Occupancy", "Limiting Factor" };
    private static readonly WorldDataTerrainDebug.MapKind[] FloraMaps =
    {
        WorldDataTerrainDebug.MapKind.FloraSuitability,
        WorldDataTerrainDebug.MapKind.FloraBiomass,
        WorldDataTerrainDebug.MapKind.FloraLimitingFactor
    };

    public bool visible
    {
        get
        {
            Terrain terrain = GetActiveTerrain();
            GeographyReference reference = terrain != null ? terrain.GetComponent<GeographyReference>() : null;
            return reference != null && reference.Geography != null && reference.Geography.HasData;
        }
    }

    private bool _enabled;
    private DataDomain _domain = DataDomain.Hydrology;
    private int _geographyMapIndex;
    private int _hydrologyMapIndex = 1;
    private int _groundMapIndex = 2;
    private int _climateMapIndex;
    private int _floraMapIndex;
    private int _floraSpeciesIndex;
    private float _opacity = 0.88f;
    private float _reliefShading = 0.34f;
    private float _liftMeters = 0.04f;
    private float _channelThresholdSquareMeters = 50000f;
    private int _channelSourceCount;
    private int _conditionedTransitSampleCount;

    private Terrain _cachedTerrain;
    private GeographyAsset _geographyAsset;
    private GeographyData _geographyData;
    private int _geographyRevision = -1;
    private HydrologyAsset _hydrologyAsset;
    private HydrologyData _hydrologyData;
    private int _hydrologyGenerationRevision = -1;
    private string _hydrologyStatus;
    private GroundConditionAsset _groundAsset;
    private GroundConditionData _groundData;
    private int _groundGenerationRevision = -1;
    private string _groundStatus;
    private ClimateAsset _climateAsset;
    private ClimateData _climateData;
    private int _climateGenerationRevision = -1;
    private string _climateStatus;
    private FloraAsset _floraAsset;
    private FloraData _floraData;
    private int _floraGenerationRevision = -1;
    private string _floraStatus;
    private Vector2 _scroll;

    public override void OnCreated()
    {
        Selection.selectionChanged += OnSelectionChanged;
        minSize = new Vector2(265f, 190f);
        maxSize = new Vector2(700f, 820f);
        size = new Vector2(345f, 460f);
    }

    public override void OnWillBeDestroyed()
    {
        Selection.selectionChanged -= OnSelectionChanged;
        WorldDataTerrainDebug.Clear();
    }

    public override void OnGUI()
    {
        Terrain terrain = GetActiveTerrain();
        if (terrain == null)
        {
            WorldDataTerrainDebug.Clear();
            return;
        }

        SyncData(terrain);
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.BeginVertical();
        EditorGUILayout.LabelField(terrain.name, EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Projected simulation diagnostics", EditorStyles.miniLabel);
        EditorGUILayout.Space(5f);

        EditorGUI.BeginChangeCheck();
        bool enabled = EditorGUILayout.ToggleLeft("Project debug map on terrain", _enabled, EditorStyles.boldLabel);
        if (EditorGUI.EndChangeCheck())
        {
            _enabled = enabled;
            if (_enabled) RebuildPreview();
            else
            {
                WorldDataTerrainDebug.Clear();
                _hydrologyData = null;
                _groundData = null;
                _climateData = null;
                _floraData = null;
            }
        }

        using (new EditorGUI.DisabledScope(!_enabled))
        {
            EditorGUI.BeginChangeCheck();
            _domain = (DataDomain)GUILayout.Toolbar((int)_domain, new[] { "Geography", "Hydrology", "Ground", "Climate", "Flora" });
            switch (_domain)
            {
                case DataDomain.Geography:
                    _geographyMapIndex = GUILayout.SelectionGrid(_geographyMapIndex, GeographyLabels, 3);
                    break;
                case DataDomain.Hydrology:
                    _hydrologyMapIndex = GUILayout.SelectionGrid(_hydrologyMapIndex, HydrologyLabels, 3);
                    break;
                case DataDomain.Ground:
                    _groundMapIndex = GUILayout.SelectionGrid(_groundMapIndex, GroundLabels, 3);
                    break;
                case DataDomain.Climate:
                    _climateMapIndex = GUILayout.SelectionGrid(_climateMapIndex, ClimateLabels, 2);
                    break;
                case DataDomain.Flora:
                    _floraMapIndex = GUILayout.SelectionGrid(_floraMapIndex, FloraLabels, 3);
                    if (_floraData != null && _floraData.SpeciesCount > 0)
                    {
                        string[] names = new string[_floraData.SpeciesCount];
                        for (int i = 0; i < names.Length; i++) names[i] = _floraData.Species[i].DisplayName;
                        _floraSpeciesIndex = EditorGUILayout.Popup("Species", Mathf.Clamp(_floraSpeciesIndex, 0, names.Length - 1), names);
                    }
                    break;
            }

            if (_domain == DataDomain.Hydrology && CurrentMap == WorldDataTerrainDebug.MapKind.Channels)
            {
                float squareKilometres = _channelThresholdSquareMeters / 1_000_000f;
                squareKilometres = Mathf.Max(0.000001f, EditorGUILayout.FloatField("Channel threshold (km²)", squareKilometres));
                _channelThresholdSquareMeters = squareKilometres * 1_000_000f;
            }

            if (EditorGUI.EndChangeCheck()) RebuildPreview();

            EditorGUILayout.Space(4f);
            EditorGUI.BeginChangeCheck();
            _opacity = EditorGUILayout.Slider("Overlay opacity", _opacity, 0.1f, 1f);
            _reliefShading = EditorGUILayout.Slider("Relief shading", _reliefShading, 0f, 1f);
            _liftMeters = EditorGUILayout.Slider("Surface lift (m)", _liftMeters, 0.005f, 0.20f);
            if (EditorGUI.EndChangeCheck()) WorldDataTerrainDebug.UpdateAppearance(_opacity, _reliefShading, _liftMeters);
        }

        EditorGUILayout.Space(5f);
        DrawStatus();
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndScrollView();
    }

    private WorldDataTerrainDebug.MapKind CurrentMap
    {
        get
        {
            return _domain switch
            {
                DataDomain.Geography => GeographyMaps[Mathf.Clamp(_geographyMapIndex, 0, GeographyMaps.Length - 1)],
                DataDomain.Hydrology => HydrologyMaps[Mathf.Clamp(_hydrologyMapIndex, 0, HydrologyMaps.Length - 1)],
                DataDomain.Ground => GroundMaps[Mathf.Clamp(_groundMapIndex, 0, GroundMaps.Length - 1)],
                DataDomain.Climate => ClimateMaps[Mathf.Clamp(_climateMapIndex, 0, ClimateMaps.Length - 1)],
                _ => FloraMaps[Mathf.Clamp(_floraMapIndex, 0, FloraMaps.Length - 1)]
            };
        }
    }

    private void SyncData(Terrain terrain)
    {
        GeographyAsset geographyAsset = terrain.GetComponent<GeographyReference>()?.Geography;
        HydrologyAsset hydrologyAsset = terrain.GetComponent<HydrologyReference>()?.Hydrology;
        GroundConditionAsset groundAsset = terrain.GetComponent<GroundConditionReference>()?.GroundConditions;
        ClimateAsset climateAsset = terrain.GetComponent<ClimateReference>()?.Climate;
        FloraAsset floraAsset = terrain.GetComponent<FloraReference>()?.Flora;

        bool changed = _cachedTerrain != terrain ||
                       geographyAsset != _geographyAsset || (geographyAsset != null && geographyAsset.GenerationRevision != _geographyRevision) ||
                       hydrologyAsset != _hydrologyAsset || (hydrologyAsset != null && hydrologyAsset.GenerationRevision != _hydrologyGenerationRevision) ||
                       groundAsset != _groundAsset || (groundAsset != null && groundAsset.GenerationRevision != _groundGenerationRevision) ||
                       climateAsset != _climateAsset || (climateAsset != null && climateAsset.GenerationRevision != _climateGenerationRevision) ||
                       floraAsset != _floraAsset || (floraAsset != null && floraAsset.GenerationRevision != _floraGenerationRevision);
        if (!changed) return;

        _cachedTerrain = terrain;
        _geographyAsset = geographyAsset;
        _geographyRevision = geographyAsset != null ? geographyAsset.GenerationRevision : -1;
        _geographyData = WorldEditorDataCache.GetGeography(geographyAsset);

        _hydrologyAsset = hydrologyAsset;
        _hydrologyGenerationRevision = hydrologyAsset != null ? hydrologyAsset.GenerationRevision : -1;
        _hydrologyData = null;
        _hydrologyStatus = "Generate Hydrology to project hydrological maps.";
        if (hydrologyAsset != null && hydrologyAsset.HasMetadata)
        {
            _channelThresholdSquareMeters = hydrologyAsset.Settings.ChannelInitiationAreaSquareMeters;
            _hydrologyStatus = hydrologyAsset.Matches(geographyAsset)
                ? null
                : "Hydrology belongs to an older Geography revision. Regenerate it before debugging.";
        }

        _groundAsset = groundAsset;
        _groundGenerationRevision = groundAsset != null ? groundAsset.GenerationRevision : -1;
        _groundData = null;
        _groundStatus = "Generate Ground Conditions to project ground maps.";
        if (groundAsset != null && groundAsset.HasMetadata)
        {
            _groundStatus = groundAsset.Matches(geographyAsset, hydrologyAsset, climateAsset)
                ? null
                : "Ground Conditions belong to older Geography/Hydrology/Climate revisions. Regenerate them before debugging.";
        }

        _climateAsset = climateAsset;
        _climateGenerationRevision = climateAsset != null ? climateAsset.GenerationRevision : -1;
        _climateData = null;
        _climateStatus = "Generate Climate to project climate maps.";
        if (climateAsset != null && climateAsset.HasMetadata)
        {
            _climateStatus = climateAsset.Matches(geographyAsset)
                ? null
                : "Climate belongs to an older Geography revision. Regenerate it before debugging.";
        }

        _floraAsset = floraAsset;
        _floraGenerationRevision = floraAsset != null ? floraAsset.GenerationRevision : -1;
        _floraData = null;
        _floraStatus = "Generate Flora to project establishment maps.";
        if (floraAsset != null && floraAsset.HasMetadata)
        {
            _floraStatus = floraAsset.Matches(geographyAsset, hydrologyAsset, groundAsset, climateAsset)
                ? null
                : "Flora belongs to older upstream revisions. Regenerate its stale dependencies, then Flora.";
        }

        if (_enabled) RebuildPreview();
    }

    private void EnsureCurrentDomainData()
    {
        ReleaseInactiveDomainData();

        switch (_domain)
        {
            case DataDomain.Hydrology:
                if (_hydrologyData == null && _hydrologyStatus == null &&
                    !WorldEditorDataCache.TryGetHydrology(_hydrologyAsset, out _hydrologyData))
                    _hydrologyStatus = "Hydrology metadata exists, but its derived cache is missing. Regenerate Hydrology.";
                break;
            case DataDomain.Ground:
                if (_groundData == null && _groundStatus == null &&
                    !WorldEditorDataCache.TryGetGround(_groundAsset, out _groundData))
                    _groundStatus = "Ground metadata exists, but its derived cache is missing. Regenerate Ground Conditions.";
                break;
            case DataDomain.Climate:
                if (_climateData == null && _climateStatus == null &&
                    !WorldEditorDataCache.TryGetClimate(_climateAsset, out _climateData))
                    _climateStatus = "Climate metadata exists, but its derived cache is missing. Regenerate Climate.";
                break;
            case DataDomain.Flora:
                if (_floraData == null && _floraStatus == null &&
                    !WorldEditorDataCache.TryGetFlora(_floraAsset, out _floraData))
                    _floraStatus = "Flora metadata exists, but its derived cache is missing. Regenerate Flora.";
                if (_floraData != null && _floraData.SpeciesCount > 0)
                    _floraSpeciesIndex = Mathf.Clamp(_floraSpeciesIndex, 0, _floraData.SpeciesCount - 1);
                break;
        }
    }

    private void ReleaseInactiveDomainData()
    {
        if (_domain != DataDomain.Hydrology) _hydrologyData = null;
        if (_domain != DataDomain.Ground) _groundData = null;
        if (_domain != DataDomain.Climate) _climateData = null;
        if (_domain != DataDomain.Flora) _floraData = null;
    }

    private void RebuildPreview()
    {
        if (_enabled) EnsureCurrentDomainData();
        if (!_enabled || _cachedTerrain == null || _geographyData == null)
        {
            WorldDataTerrainDebug.Clear();
            return;
        }
        if (_domain == DataDomain.Hydrology && _hydrologyData == null ||
            _domain == DataDomain.Ground && _groundData == null ||
            _domain == DataDomain.Climate && _climateData == null ||
            _domain == DataDomain.Flora && (_floraData == null || _floraData.SpeciesCount == 0))
        {
            WorldDataTerrainDebug.Clear();
            return;
        }

        try
        {
            if (_domain == DataDomain.Hydrology && CurrentMap == WorldDataTerrainDebug.MapKind.Channels)
            {
                _channelSourceCount = _hydrologyData.CountCandidateChannelSources(_channelThresholdSquareMeters);
                _conditionedTransitSampleCount = _hydrologyData.CountCandidateConditionedTransitSamples(_channelThresholdSquareMeters);
            }

            WorldDataTerrainDebug.Show(
                _cachedTerrain,
                _geographyData,
                _hydrologyData,
                _groundData,
                _climateData,
                _floraData,
                _floraSpeciesIndex,
                CurrentMap,
                _channelThresholdSquareMeters,
                _opacity,
                _reliefShading,
                _liftMeters);
        }
        catch (System.Exception exception)
        {
            WorldDataTerrainDebug.Clear();
            Debug.LogException(exception);
        }
    }

    private void DrawStatus()
    {
        if (!_enabled)
        {
            EditorGUILayout.HelpBox("The projection uses a temporary Terrain copy sharing the same TerrainData. It never mutates simulation truth or the source Terrain material.", MessageType.Info);
            return;
        }
        if (_domain == DataDomain.Hydrology && _hydrologyData == null) { EditorGUILayout.HelpBox(_hydrologyStatus, MessageType.Warning); return; }
        if (_domain == DataDomain.Ground && _groundData == null) { EditorGUILayout.HelpBox(_groundStatus, MessageType.Warning); return; }
        if (_domain == DataDomain.Climate && _climateData == null) { EditorGUILayout.HelpBox(_climateStatus, MessageType.Warning); return; }
        if (_domain == DataDomain.Flora && (_floraData == null || _floraData.SpeciesCount == 0)) { EditorGUILayout.HelpBox(_floraStatus, MessageType.Warning); return; }

        switch (CurrentMap)
        {
            case WorldDataTerrainDebug.MapKind.Height:
                EditorGUILayout.HelpBox("Height — normalized Geography elevation projected onto the landform.", MessageType.None); break;
            case WorldDataTerrainDebug.MapKind.Slope:
                EditorGUILayout.HelpBox("Slope — black is flat; white reaches 60° or steeper.", MessageType.None); break;
            case WorldDataTerrainDebug.MapKind.Aspect:
                EditorGUILayout.HelpBox("Aspect — hue shows downslope bearing directly on the slopes it describes.", MessageType.None); break;
            case WorldDataTerrainDebug.MapKind.FlowDirection:
                EditorGUILayout.HelpBox("Direction — hue represents conditioned D8 receiver bearing. It is drainage routing, not necessarily exposed stream flow.", MessageType.None); break;
            case WorldDataTerrainDebug.MapKind.Accumulation:
                EditorGUILayout.HelpBox("Accumulation — brighter cyan marks progressively larger upstream contributing area.", MessageType.None); break;
            case WorldDataTerrainDebug.MapKind.Basins:
                EditorGUILayout.HelpBox($"Outlet basins — {_hydrologyData.BasinCount:N0} colour-coded catchments.", MessageType.None); break;
            case WorldDataTerrainDebug.MapKind.RawSinks:
                EditorGUILayout.HelpBox("Raw sinks — red local minima and cyan boundary outlets.", MessageType.None); break;
            case WorldDataTerrainDebug.MapKind.DepressionDepth:
                EditorGUILayout.HelpBox("Depressions — terrain requiring fill-to-spill conditioning; brighter cyan means deeper fill.", MessageType.None); break;
            case WorldDataTerrainDebug.MapKind.Channels:
                EditorGUILayout.HelpBox($"Composite channels — cyan exposed channel, blue conditioned transit, bright-cyan crosses genuine channel heads. {_channelSourceCount:N0} heads; {_conditionedTransitSampleCount:N0} conditioned samples.", MessageType.None); break;
            case WorldDataTerrainDebug.MapKind.RelativeTwi:
                EditorGUILayout.HelpBox("Relative TWI — 5th–95th percentile normalization within this world. Display only; do not treat as a cross-world ecological scale.", MessageType.None); break;
            case WorldDataTerrainDebug.MapKind.RunOnPotential:
                EditorGUILayout.HelpBox("Run-on Potential — excess upstream contributing area above Ground's fixed local support area; independent of climatic supply.", MessageType.None); break;
            case WorldDataTerrainDebug.MapKind.WaterAvailabilityPotential:
                EditorGUILayout.HelpBox("Water Availability — direct precipitation supply plus saturating run-on contribution. This is Flora's water input in V1.1.", MessageType.None); break;
            case WorldDataTerrainDebug.MapKind.SoilRetention:
                EditorGUILayout.HelpBox("Soil Retention Potential — derived from Ground's fixed physical-scale slope, not Geography's resolution-dependent local slope; not soil depth or fertility.", MessageType.None); break;
            case WorldDataTerrainDebug.MapKind.Temperature:
                EditorGUILayout.HelpBox("Temperature — fixed -20°C..40°C diagnostic scale; elevation and north/south position are causal inputs.", MessageType.None); break;
            case WorldDataTerrainDebug.MapKind.PrecipitationPotential:
                EditorGUILayout.HelpBox("Precipitation Potential — static 0..1 atmospheric supply/exposure from prevailing wind and terrain; not rainfall volume.", MessageType.None); break;
            case WorldDataTerrainDebug.MapKind.FloraSuitability:
                EditorGUILayout.HelpBox("Flora Suitability — weakest environmental response for the selected species (law of the minimum).", MessageType.None); break;
            case WorldDataTerrainDebug.MapKind.FloraBiomass:
                EditorGUILayout.HelpBox("Flora Initial Occupancy — generated 0..1 starting occupancy used only to initialize living Flora. Current biomass through time is owned by Flora State / Ecology Lab.", MessageType.None); break;
            case WorldDataTerrainDebug.MapKind.FloraLimitingFactor:
                EditorGUILayout.HelpBox("Limiting Factor — red temperature, blue/cyan water availability, brown retention, magenta multiple, light grey none.", MessageType.None); break;
        }
    }

    private void OnSelectionChanged()
    {
        WorldDataTerrainDebug.Clear();
        _cachedTerrain = null;
        _geographyData = null;
        _hydrologyData = null;
        _groundData = null;
        _climateData = null;
        _floraData = null;
        RepaintSceneViews();
    }

    private static Terrain GetActiveTerrain()
    {
        GameObject selected = Selection.activeGameObject;
        return selected != null ? selected.GetComponent<Terrain>() : null;
    }

    private static void RepaintSceneViews() => SceneView.RepaintAll();
}
