using System.Diagnostics;
using InfinityProject.World.Geography;
using InfinityProject.World.Hydrology;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;

[Overlay(typeof(SceneView), "Hydrology Tool", true)]
[Icon("d_PreMatCube")]
public sealed class HydrologyWorldTool : IMGUIOverlay, ITransientOverlay
{
    public bool visible
    {
        get
        {
            Terrain terrain = GetActiveTerrain();
            GeographyReference reference = terrain != null ? terrain.GetComponent<GeographyReference>() : null;
            return reference != null && reference.Geography != null && reference.Geography.HasData;
        }
    }

    private HydrologyGenerationSettings _settings = new();
    private HydrologyData _lastData;
    private HydrologyAsset _lastAsset;
    private Terrain _cachedTerrain;
    private bool _generating;
    private bool _stale;
    private bool _cacheMissing;
    private string _lastTiming = string.Empty;
    private Vector2 _scroll;

    public override void OnCreated()
    {
        Selection.selectionChanged += RepaintSceneViews;
        minSize = new Vector2(230f, 170f);
        maxSize = new Vector2(560f, 760f);
        size = new Vector2(290f, 430f);
    }

    public override void OnWillBeDestroyed()
        => Selection.selectionChanged -= RepaintSceneViews;

    public override void OnGUI()
    {
        Terrain terrain = GetActiveTerrain();
        if (terrain == null)
            return;

        GeographyReference geographyReference = terrain.GetComponent<GeographyReference>();
        if (geographyReference == null || geographyReference.Geography == null || !geographyReference.Geography.HasData)
            return;

        SyncFromTerrain(terrain, geographyReference.Geography);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.BeginVertical();
        EditorGUILayout.LabelField(terrain.name, EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Hydrology V0.2 — conditioned + flat routing", EditorStyles.miniLabel);
        EditorGUILayout.Space(6f);

        GeographyGenerationSettings geoSettings = geographyReference.Geography.Settings;
        EditorGUILayout.LabelField($"Source: seed {geoSettings.Seed}, {geoSettings.Resolution}²", EditorStyles.miniLabel);

        float squareKilometres = _settings.ChannelInitiationAreaSquareMeters / 1_000_000f;
        squareKilometres = Mathf.Max(0.000001f,
            EditorGUILayout.FloatField("Channel area (km²)", squareKilometres));
        _settings.ChannelInitiationAreaSquareMeters = squareKilometres * 1_000_000f;

        EditorGUILayout.Space(6f);
        using (new EditorGUI.DisabledScope(_generating))
        {
            if (GUILayout.Button("Generate Hydrology", GUILayout.Height(34f)))
                Generate(terrain, geographyReference.Geography);
        }

        using (new EditorGUI.DisabledScope(_lastData == null))
        {
            if (GUILayout.Button("Inspect Hydrology Data", GUILayout.Height(24f)))
                HydrologyDebugWindow.Open(_lastData, _settings.ChannelInitiationAreaSquareMeters);
        }

        if (_lastData != null)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField($"Outlet basins: {_lastData.BasinCount:N0}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Raw local sinks: {_lastData.RawSinkCount:N0}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Boundary outlets: {_lastData.OutletCount:N0}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Depression samples: {_lastData.DepressionSampleCount:N0}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Max spill depth: {_lastData.MaxDepressionDepthMeters:0.###} m", EditorStyles.miniLabel);
        }

        if (!string.IsNullOrEmpty(_lastTiming))
            EditorGUILayout.LabelField(_lastTiming, EditorStyles.miniLabel);

        if (_stale)
        {
            EditorGUILayout.HelpBox(
                "Stored hydrology metadata belongs to an older geography state. Regenerate before trusting it.",
                MessageType.Warning);
        }
        else if (_cacheMissing && _lastAsset != null)
        {
            EditorGUILayout.HelpBox(
                "Hydrology metadata exists, but its derived binary cache is missing (Library may have been cleared). Regenerate it.",
                MessageType.Warning);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "V0.3 keeps raw local minima as evidence, uses Priority-Flood for spill connectivity, resolves equal-height flats toward real spill/outlet cells, and distinguishes conditioned depression transit from exposed candidate channels. Geography is not modified.",
                MessageType.Info);
        }

        EditorGUILayout.EndVertical();
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

            GeographyGenerationSettings geoSettings = geographyAsset.Settings;
            var elevationInput = new HydrologyElevationInput(
                geoSettings.Resolution,
                geoSettings.WidthMeters,
                geoSettings.LengthMeters,
                geoSettings.MaxElevationMeters,
                geoSettings.Seed,
                geographyAsset.NormalizedHeight);
            long geographyMs = phase.ElapsedMilliseconds;

            phase.Restart();
            _lastData = HydrologyGenerator.Generate(elevationInput, _settings);
            long solveMs = phase.ElapsedMilliseconds;

            phase.Restart();
            _lastAsset = PersistHydrologyMetadata(terrain, geographyAsset, _lastData);
            long metadataMs = phase.ElapsedMilliseconds;

            phase.Restart();
            HydrologyBinaryCache.Save(_lastAsset, _lastData);
            WorldEditorDataCache.Remember(_lastAsset, _lastData);
            long cacheMs = phase.ElapsedMilliseconds;

            LinkTerrainToHydrology(terrain, _lastAsset);
            _stale = false;
            _cacheMissing = false;

            phase.Restart();
            HydrologyDebugWindow.Open(_lastData, _settings.ChannelInitiationAreaSquareMeters);
            long inspectorMs = phase.ElapsedMilliseconds;
            total.Stop();

            _lastTiming = $"Last build: {total.Elapsed.TotalSeconds:0.00}s (solve {solveMs / 1000f:0.00}s, cache {cacheMs / 1000f:0.00}s)";

            UnityEngine.Debug.Log(
                $"[Infinity Hydrology] {_lastData.Resolution}² | basins {_lastData.BasinCount:N0} | " +
                $"raw sinks {_lastData.RawSinkCount:N0} | outlets {_lastData.OutletCount:N0} | " +
                $"depression samples {_lastData.DepressionSampleCount:N0} | max spill {_lastData.MaxDepressionDepthMeters:0.###} m | " +
                $"timings ms: geography {geographyMs:N0}, solve {solveMs:N0}, metadata {metadataMs:N0}, cache {cacheMs:N0}, inspector {inspectorMs:N0}, total {total.ElapsedMilliseconds:N0}");

            SceneView.RepaintAll();
        }
        catch (System.Exception exception)
        {
            UnityEngine.Debug.LogException(exception);
        }
        finally
        {
            _generating = false;
        }
    }

    private void SyncFromTerrain(Terrain terrain, GeographyAsset geographyAsset)
    {
        if (_cachedTerrain == terrain)
        {
            if (_lastAsset != null)
                _stale = !_lastAsset.Matches(geographyAsset);
            return;
        }

        _cachedTerrain = terrain;
        _lastData = null;
        _lastAsset = null;
        _stale = false;
        _cacheMissing = false;
        _lastTiming = string.Empty;

        HydrologyReference reference = terrain.GetComponent<HydrologyReference>();
        HydrologyAsset asset = reference != null ? reference.Hydrology : null;
        if (asset == null || !asset.HasMetadata)
            return;

        _lastAsset = asset;
        _settings = asset.Settings.Clone();
        _stale = !asset.Matches(geographyAsset);
        if (_stale)
            return;

        if (!WorldEditorDataCache.TryGetHydrology(asset, out _lastData))
            _cacheMissing = true;
    }

    private HydrologyAsset PersistHydrologyMetadata(Terrain terrain, GeographyAsset geographyAsset, HydrologyData data)
    {
        string hydrologyFolder = InfinityGeneratedPaths.Ensure("Hydrology");

        HydrologyReference reference = terrain.GetComponent<HydrologyReference>();
        HydrologyAsset asset = reference != null ? reference.Hydrology : null;

        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<HydrologyAsset>();
            string safeName = MakeSafeFileName(terrain.name);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{hydrologyFolder}/{safeName}_Hydrology.asset");
            AssetDatabase.CreateAsset(asset, path);
        }

        Undo.RecordObject(asset, "Store Infinity Hydrology Metadata");
        asset.StoreMetadata(data, _settings, geographyAsset);
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        return asset;
    }

    private static void LinkTerrainToHydrology(Terrain terrain, HydrologyAsset asset)
    {
        HydrologyReference reference = terrain.GetComponent<HydrologyReference>();
        if (reference == null)
            reference = Undo.AddComponent<HydrologyReference>(terrain.gameObject);

        Undo.RecordObject(reference, "Link Infinity Hydrology");
        reference.Hydrology = asset;
        EditorUtility.SetDirty(reference);
    }

    private static string MakeSafeFileName(string value)
    {
        foreach (char invalid in System.IO.Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(value) ? "Terrain" : value;
    }

    private static Terrain GetActiveTerrain()
    {
        GameObject selected = Selection.activeGameObject;
        return selected != null ? selected.GetComponent<Terrain>() : null;
    }

    private static void RepaintSceneViews() => SceneView.RepaintAll();
}
