using System.Diagnostics;
using InfinityProject.World.Climate;
using InfinityProject.World.Geography;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;

[Overlay(typeof(SceneView), "Climate Tool", false)]
[Icon("d_Terrain Icon")]
public sealed class ClimateWorldTool : IMGUIOverlay, ITransientOverlay
{
    public bool visible
    {
        get
        {
            Terrain terrain = GetActiveTerrain();
            GeographyAsset geography = terrain != null ? terrain.GetComponent<GeographyReference>()?.Geography : null;
            return geography != null && geography.HasData;
        }
    }

    private ClimateGenerationSettings _settings = new();
    private ClimateAsset _lastAsset;
    private ClimateData _lastData;
    private Terrain _cachedTerrain;
    private bool _stale;
    private bool _cacheMissing;
    private bool _generating;
    private string _lastTiming = string.Empty;
    private Vector2 _scroll;

    public override void OnCreated()
    {
        Selection.selectionChanged += RepaintSceneViews;
        minSize = new Vector2(245f, 180f);
        maxSize = new Vector2(560f, 760f);
        size = new Vector2(310f, 480f);
    }

    public override void OnWillBeDestroyed() => Selection.selectionChanged -= RepaintSceneViews;

    public override void OnGUI()
    {
        Terrain terrain = GetActiveTerrain();
        if (terrain == null) return;
        GeographyAsset geography = terrain.GetComponent<GeographyReference>()?.Geography;
        if (geography == null || !geography.HasData) return;

        SyncFromTerrain(terrain, geography);
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.LabelField(terrain.name, EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Climate V0 — static environmental fields", EditorStyles.miniLabel);
        EditorGUILayout.Space(5f);

        EditorGUILayout.LabelField("Temperature", EditorStyles.boldLabel);
        _settings.BaseTemperatureCelsius = EditorGUILayout.FloatField("Base at 0 m (°C)", _settings.BaseTemperatureCelsius);
        _settings.LapseRateCelsiusPerKilometre = EditorGUILayout.Slider("Lapse rate (°C/km)", _settings.LapseRateCelsiusPerKilometre, 0f, 15f);
        _settings.SouthToNorthTemperatureDeltaCelsius = EditorGUILayout.Slider("South → north Δ (°C)", _settings.SouthToNorthTemperatureDeltaCelsius, -20f, 20f);

        EditorGUILayout.Space(5f);
        EditorGUILayout.LabelField("Precipitation potential", EditorStyles.boldLabel);
        _settings.BackgroundPrecipitationPotential = EditorGUILayout.Slider("Background", _settings.BackgroundPrecipitationPotential, 0f, 1f);
        _settings.MoistureWindFromDegrees = EditorGUILayout.Slider("Wind from bearing (°)", _settings.MoistureWindFromDegrees, 0f, 360f);
        _settings.WindwardBoost = EditorGUILayout.Slider("Windward boost", _settings.WindwardBoost, 0f, 1f);
        _settings.BroadOrographicBoost = EditorGUILayout.Slider("Broad uplift boost", _settings.BroadOrographicBoost, 0f, 1f);
        _settings.RainShadowStrength = EditorGUILayout.Slider("Rain-shadow strength", _settings.RainShadowStrength, 0f, 1f);
        _settings.UpwindSampleRangeMeters = Mathf.Max(1f, EditorGUILayout.FloatField("Upwind range (m)", _settings.UpwindSampleRangeMeters));
        _settings.UpwindSampleCount = EditorGUILayout.IntSlider("Upwind samples", _settings.UpwindSampleCount, 2, 32);
        _settings.OrographicReliefScaleMeters = Mathf.Max(1f, EditorGUILayout.FloatField("Relief scale (m)", _settings.OrographicReliefScaleMeters));

        EditorGUILayout.Space(6f);
        using (new EditorGUI.DisabledScope(_generating))
        {
            if (GUILayout.Button("Generate Climate", GUILayout.Height(34f))) Generate(terrain, geography);
        }
        using (new EditorGUI.DisabledScope(_lastData == null))
        {
            if (GUILayout.Button("Inspect Climate Data", GUILayout.Height(24f))) ClimateDebugWindow.Open(_lastData);
        }

        if (!string.IsNullOrEmpty(_lastTiming)) EditorGUILayout.LabelField(_lastTiming, EditorStyles.miniLabel);
        if (_stale)
            EditorGUILayout.HelpBox("Climate belongs to an older Geography revision. Regenerate it.", MessageType.Warning);
        else if (_cacheMissing && _lastAsset != null)
            EditorGUILayout.HelpBox("Climate metadata exists, but its derived cache is missing. Regenerate Climate.", MessageType.Warning);
        else
            EditorGUILayout.HelpBox("V0 is static climate context: temperature + precipitation potential. No seasons, weather fronts or rainfall water budget yet.", MessageType.Info);

        EditorGUILayout.EndScrollView();
    }

    private void Generate(Terrain terrain, GeographyAsset geographyAsset)
    {
        _generating = true;
        try
        {
            _settings.Validate();
            var total = Stopwatch.StartNew();
            var phase = Stopwatch.StartNew();
            GeographyData geographyData = WorldEditorDataCache.GetGeography(geographyAsset);
            long geographyMs = phase.ElapsedMilliseconds;
            phase.Restart();
            _lastData = ClimateGenerator.Generate(geographyData, _settings);
            long solveMs = phase.ElapsedMilliseconds;
            phase.Restart();
            _lastAsset = PersistMetadata(terrain, geographyAsset, _lastData);
            long metadataMs = phase.ElapsedMilliseconds;
            phase.Restart();
            ClimateBinaryCache.Save(_lastAsset, _lastData);
            WorldEditorDataCache.Remember(_lastAsset, _lastData);
            long cacheMs = phase.ElapsedMilliseconds;
            LinkTerrain(terrain, _lastAsset);
            _stale = false;
            _cacheMissing = false;
            phase.Restart();
            ClimateDebugWindow.Open(_lastData);
            long inspectorMs = phase.ElapsedMilliseconds;
            total.Stop();
            _lastTiming = $"Last build: {total.Elapsed.TotalSeconds:0.00}s (solve {solveMs / 1000f:0.00}s, cache {cacheMs / 1000f:0.00}s)";
            UnityEngine.Debug.Log($"[Infinity Climate] {_lastData.Resolution}² | temp {_lastData.MinimumTemperatureCelsius:0.0}..{_lastData.MaximumTemperatureCelsius:0.0} °C | precip {_lastData.MinimumPrecipitationPotential:0.000}..{_lastData.MaximumPrecipitationPotential:0.000} | timings ms: geography {geographyMs:N0}, solve {solveMs:N0}, metadata {metadataMs:N0}, cache {cacheMs:N0}, inspector {inspectorMs:N0}, total {total.ElapsedMilliseconds:N0}");
            SceneView.RepaintAll();
        }
        catch (System.Exception exception) { UnityEngine.Debug.LogException(exception); }
        finally { _generating = false; }
    }

    private void SyncFromTerrain(Terrain terrain, GeographyAsset geography)
    {
        if (_cachedTerrain == terrain)
        {
            if (_lastAsset != null) _stale = !_lastAsset.Matches(geography);
            return;
        }
        _cachedTerrain = terrain;
        _lastAsset = terrain.GetComponent<ClimateReference>()?.Climate;
        _lastData = null;
        _stale = false;
        _cacheMissing = false;
        _lastTiming = string.Empty;
        if (_lastAsset == null || !_lastAsset.HasMetadata) return;
        _settings = _lastAsset.Settings.Clone();
        _stale = !_lastAsset.Matches(geography);
        if (!_stale && !WorldEditorDataCache.TryGetClimate(_lastAsset, out _lastData)) _cacheMissing = true;
    }

    private ClimateAsset PersistMetadata(Terrain terrain, GeographyAsset geography, ClimateData data)
    {
        string folder = InfinityGeneratedPaths.Ensure("Climate");
        ClimateAsset asset = terrain.GetComponent<ClimateReference>()?.Climate;
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<ClimateAsset>();
            string safe = MakeSafeFileName(terrain.name);
            AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{safe}_Climate.asset"));
        }
        Undo.RecordObject(asset, "Store Infinity Climate Metadata");
        asset.StoreMetadata(data, _settings, geography);
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        return asset;
    }

    private static void LinkTerrain(Terrain terrain, ClimateAsset asset)
    {
        ClimateReference reference = terrain.GetComponent<ClimateReference>();
        if (reference == null) reference = Undo.AddComponent<ClimateReference>(terrain.gameObject);
        Undo.RecordObject(reference, "Link Infinity Climate");
        reference.Climate = asset;
        EditorUtility.SetDirty(reference);
    }

    private static string MakeSafeFileName(string value)
    {
        foreach (char invalid in System.IO.Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(value) ? "Terrain" : value;
    }

    private static Terrain GetActiveTerrain()
    {
        GameObject selected = Selection.activeGameObject;
        return selected != null ? selected.GetComponent<Terrain>() : null;
    }

    private static void RepaintSceneViews() => SceneView.RepaintAll();
}
