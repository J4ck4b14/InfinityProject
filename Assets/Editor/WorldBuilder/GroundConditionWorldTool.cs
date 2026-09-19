using System.Diagnostics;
using InfinityProject.World.Climate;
using InfinityProject.World.Geography;
using InfinityProject.World.Ground;
using InfinityProject.World.Hydrology;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;

[Overlay(typeof(SceneView), "Ground Conditions Tool", false)]
[Icon("d_Terrain Icon")]
public sealed class GroundConditionWorldTool : IMGUIOverlay, ITransientOverlay
{
    public bool visible
    {
        get
        {
            Terrain t = GetActiveTerrain(); if (t == null) return false;
            GeographyAsset g = t.GetComponent<GeographyReference>()?.Geography;
            HydrologyAsset h = t.GetComponent<HydrologyReference>()?.Hydrology;
            ClimateAsset c = t.GetComponent<ClimateReference>()?.Climate;
            return g != null && g.HasData && h != null && h.HasMetadata && c != null && c.HasMetadata;
        }
    }

    private GroundConditionGenerationSettings _settings = new();
    private GroundConditionData _lastData; private GroundConditionAsset _lastAsset; private Terrain _cachedTerrain;
    private bool _generating, _stale, _cacheMissing; private string _lastTiming = string.Empty, _lastScaleSummary = string.Empty;
    private Vector2 _scroll;

    public override void OnCreated() { Selection.selectionChanged += RepaintSceneViews; minSize = new Vector2(250f, 190f); maxSize = new Vector2(580f, 760f); size = new Vector2(320f, 430f); }
    public override void OnWillBeDestroyed() => Selection.selectionChanged -= RepaintSceneViews;

    public override void OnGUI()
    {
        Terrain terrain = GetActiveTerrain(); if (terrain == null) return;
        GeographyAsset geography = terrain.GetComponent<GeographyReference>()?.Geography;
        HydrologyAsset hydrology = terrain.GetComponent<HydrologyReference>()?.Hydrology;
        ClimateAsset climate = terrain.GetComponent<ClimateReference>()?.Climate;
        if (geography == null || hydrology == null || climate == null) return;
        SyncFromTerrain(terrain, geography, hydrology, climate);

        bool hydrologyValid = hydrology.Matches(geography); bool climateValid = climate.Matches(geography);
        _scroll = EditorGUILayout.BeginScrollView(_scroll); EditorGUILayout.BeginVertical();
        EditorGUILayout.LabelField(terrain.name, EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Ground V0.4 — landform scale + climatic water availability", EditorStyles.miniLabel);
        EditorGUILayout.Space(6f);

        EditorGUILayout.LabelField("Physical landform scale", EditorStyles.boldLabel);
        _settings.SlopeAnalysisRadiusMeters = Mathf.Max(0f, EditorGUILayout.FloatField("Ground slope radius (m)", _settings.SlopeAnalysisRadiusMeters));
        EditorGUILayout.LabelField("0 m = native Geography slope (diagnostic only)", EditorStyles.miniLabel);

        EditorGUILayout.Space(4f); EditorGUILayout.LabelField("Hydrology diagnostics", EditorStyles.boldLabel);
        _settings.MinimumSlopeDegrees = Mathf.Clamp(EditorGUILayout.FloatField("Min slope regularization (°)", _settings.MinimumSlopeDegrees), 0.01f, 10f);

        EditorGUILayout.Space(4f); EditorGUILayout.LabelField("Run-on / water availability", EditorStyles.boldLabel);
        _settings.RunOnLocalSupportAreaSquareMeters = Mathf.Max(1f, EditorGUILayout.FloatField("Local support area (m²)", _settings.RunOnLocalSupportAreaSquareMeters));
        _settings.RunOnResponseAreaSquareMeters = Mathf.Max(1f, EditorGUILayout.FloatField("Run-on response area (m²)", _settings.RunOnResponseAreaSquareMeters));
        _settings.RunOnWaterBoostStrength = EditorGUILayout.Slider("Run-on water boost", _settings.RunOnWaterBoostStrength, 0f, 1f);

        EditorGUILayout.Space(4f); EditorGUILayout.LabelField("Soil retention", EditorStyles.boldLabel);
        _settings.FullRetentionSlopeDegrees = EditorGUILayout.Slider("Full retention ≤ (°)", _settings.FullRetentionSlopeDegrees, 0f, 45f);
        _settings.NoRetentionSlopeDegrees = EditorGUILayout.Slider("No retention ≥ (°)", _settings.NoRetentionSlopeDegrees, Mathf.Max(_settings.FullRetentionSlopeDegrees + 0.1f, 1f), 89f);

        EditorGUILayout.Space(6f);
        using (new EditorGUI.DisabledScope(_generating || !hydrologyValid || !climateValid))
            if (GUILayout.Button("Generate Ground Conditions", GUILayout.Height(34f))) Generate(terrain, geography, hydrology, climate);
        using (new EditorGUI.DisabledScope(_lastData == null))
            if (GUILayout.Button("Inspect Ground Data", GUILayout.Height(24f))) GroundConditionDebugWindow.Open(_lastData);

        if (_lastData != null)
            EditorGUILayout.LabelField($"TWI p05 {_lastData.Percentile05WetnessIndex:0.###} | p95 {_lastData.Percentile95WetnessIndex:0.###}", EditorStyles.miniLabel);
        if (!string.IsNullOrEmpty(_lastTiming)) EditorGUILayout.LabelField(_lastTiming, EditorStyles.miniLabel);
        if (!string.IsNullOrEmpty(_lastScaleSummary)) EditorGUILayout.LabelField(_lastScaleSummary, EditorStyles.miniLabel);

        if (!hydrologyValid) EditorGUILayout.HelpBox("Hydrology is stale relative to Geography.", MessageType.Warning);
        else if (!climateValid) EditorGUILayout.HelpBox("Climate is stale relative to Geography.", MessageType.Warning);
        else if (_stale) EditorGUILayout.HelpBox("Ground Conditions belong to older Geography / Hydrology / Climate revisions. Regenerate them.", MessageType.Warning);
        else if (_cacheMissing && _lastAsset != null) EditorGUILayout.HelpBox("Ground metadata exists, but its derived cache is missing. Regenerate Ground Conditions.", MessageType.Warning);
        else EditorGUILayout.HelpBox(
            "V0.4 separates three water ideas: TWI remains a topographic diagnostic; Run-on Potential measures excess physical contributing area; Water Availability combines direct climatic supply with run-on. Flora consumes Water Availability, not TWI/wetness as an independent survival gate.", MessageType.Info);

        EditorGUILayout.EndVertical(); EditorGUILayout.EndScrollView();
    }

    private void Generate(Terrain terrain, GeographyAsset geographyAsset, HydrologyAsset hydrologyAsset, ClimateAsset climateAsset)
    {
        _generating = true;
        try
        {
            _settings.Validate();
            if (!hydrologyAsset.Matches(geographyAsset)) throw new System.InvalidOperationException("Hydrology is stale relative to Geography.");
            if (!climateAsset.Matches(geographyAsset)) throw new System.InvalidOperationException("Climate is stale relative to Geography.");
            if (!WorldEditorDataCache.TryGetHydrology(hydrologyAsset, out HydrologyData hydrologyData)) throw new System.InvalidOperationException("Hydrology cache is missing.");
            if (!WorldEditorDataCache.TryGetClimate(climateAsset, out ClimateData climateData)) throw new System.InvalidOperationException("Climate cache is missing.");

            var total = Stopwatch.StartNew(); var phase = Stopwatch.StartNew();
            GeographyData geographyData = WorldEditorDataCache.GetGeography(geographyAsset); long geographyMs = phase.ElapsedMilliseconds;
            phase.Restart(); _lastData = GroundConditionGenerator.Generate(geographyData, hydrologyData, climateData, _settings); long solveMs = phase.ElapsedMilliseconds;
            _lastScaleSummary = BuildScaleSummary(geographyData, _lastData);
            phase.Restart(); _lastAsset = PersistMetadata(terrain, geographyAsset, hydrologyAsset, climateAsset, _lastData); long metadataMs = phase.ElapsedMilliseconds;
            phase.Restart(); GroundConditionBinaryCache.Save(_lastAsset, _lastData); WorldEditorDataCache.Remember(_lastAsset, _lastData); long cacheMs = phase.ElapsedMilliseconds;
            LinkTerrain(terrain, _lastAsset); _stale = false; _cacheMissing = false;
            phase.Restart(); GroundConditionDebugWindow.Open(_lastData); long inspectorMs = phase.ElapsedMilliseconds; total.Stop();
            _lastTiming = $"Last build: {total.Elapsed.TotalSeconds:0.00}s (solve {solveMs / 1000f:0.00}s, cache {cacheMs / 1000f:0.00}s)";
            UnityEngine.Debug.Log($"[Infinity Ground] {_lastData.Resolution}² | landform radius {_settings.SlopeAnalysisRadiusMeters:0.##} m | TWI {_lastData.MinimumWetnessIndex:0.###}..{_lastData.MaximumWetnessIndex:0.###} | p05 {_lastData.Percentile05WetnessIndex:0.###}, p95 {_lastData.Percentile95WetnessIndex:0.###} | timings ms: geography {geographyMs:N0}, solve {solveMs:N0}, metadata {metadataMs:N0}, cache {cacheMs:N0}, inspector {inspectorMs:N0}, total {total.ElapsedMilliseconds:N0}");
            SceneView.RepaintAll();
        }
        catch (System.Exception e) { UnityEngine.Debug.LogException(e); }
        finally { _generating = false; }
    }

    private void SyncFromTerrain(Terrain terrain, GeographyAsset geography, HydrologyAsset hydrology, ClimateAsset climate)
    {
        if (_cachedTerrain == terrain) { if (_lastAsset != null) _stale = !_lastAsset.Matches(geography, hydrology, climate); return; }
        _cachedTerrain = terrain; _lastData = null; _lastAsset = null; _stale = false; _cacheMissing = false; _lastTiming = string.Empty; _lastScaleSummary = string.Empty;
        GroundConditionAsset asset = terrain.GetComponent<GroundConditionReference>()?.GroundConditions; if (asset == null || !asset.HasMetadata) return;
        _lastAsset = asset; _settings = asset.Settings.Clone();
        if (asset.ModelVersion < 4) _settings = new GroundConditionGenerationSettings();
        _stale = !asset.Matches(geography, hydrology, climate); if (_stale) return;
        if (!WorldEditorDataCache.TryGetGround(asset, out _lastData)) _cacheMissing = true;
    }

    private static string BuildScaleSummary(GeographyData geography, GroundConditionData ground)
    {
        int count = geography.Resolution * geography.Resolution, stride = Mathf.Max(1, count / 65536), sampled = 0;
        double waterSum = 0d; int localOver42 = 0, retentionAboveHalf = 0, runOnAboveHalf = 0;
        for (int i = 0; i < count; i += stride)
        {
            if (geography.SlopeDegrees[i] >= 42f) localOver42++;
            if (ground.SoilRetentionPotential[i] > 0.5f) retentionAboveHalf++;
            if (ground.RunOnPotential[i] > 0.5f) runOnAboveHalf++;
            waterSum += ground.WaterAvailabilityPotential[i]; sampled++;
        }
        if (sampled == 0) return string.Empty;
        return $"native slope>42° {100.0 * localOver42 / sampled:0.0}% | Ground retention>0.5 {100.0 * retentionAboveHalf / sampled:0.0}% | run-on>0.5 {100.0 * runOnAboveHalf / sampled:0.0}% | mean water {waterSum / sampled:0.000}";
    }

    private GroundConditionAsset PersistMetadata(Terrain terrain, GeographyAsset geography, HydrologyAsset hydrology, ClimateAsset climate, GroundConditionData data)
    {
        string folder = InfinityGeneratedPaths.Ensure("Ground");
        GroundConditionAsset asset = terrain.GetComponent<GroundConditionReference>()?.GroundConditions;
        if (asset == null) { asset = ScriptableObject.CreateInstance<GroundConditionAsset>(); AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{MakeSafeFileName(terrain.name)}_GroundConditions.asset")); }
        Undo.RecordObject(asset, "Store Infinity Ground Conditions Metadata"); asset.StoreMetadata(data, _settings, geography, hydrology, climate); EditorUtility.SetDirty(asset); AssetDatabase.SaveAssets(); return asset;
    }

    private static void LinkTerrain(Terrain terrain, GroundConditionAsset asset)
    {
        GroundConditionReference r = terrain.GetComponent<GroundConditionReference>(); if (r == null) r = Undo.AddComponent<GroundConditionReference>(terrain.gameObject);
        Undo.RecordObject(r, "Link Infinity Ground Conditions"); r.GroundConditions = asset; EditorUtility.SetDirty(r);
    }
    private static string MakeSafeFileName(string v) { foreach (char c in System.IO.Path.GetInvalidFileNameChars()) v = v.Replace(c, '_'); return string.IsNullOrWhiteSpace(v) ? "Terrain" : v; }
    private static Terrain GetActiveTerrain() { GameObject s = Selection.activeGameObject; return s != null ? s.GetComponent<Terrain>() : null; }
    private static void RepaintSceneViews() => SceneView.RepaintAll();
}
