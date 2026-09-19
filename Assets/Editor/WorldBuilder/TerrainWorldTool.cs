using InfinityProject.World.Geography;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;

[Overlay(typeof(SceneView), "Terrain Tool", true)]
[Icon("d_Terrain Icon")]
public class TerrainWorldTool : IMGUIOverlay, ITransientOverlay
{
    public bool visible => GetActiveTerrain() != null;

    private GeographyGenerationSettings _settings = new();
    private int _resolutionIndex = 2;
    private bool _generating;
    private GeographyData _lastData;
    private GeographyAsset _lastAsset;
    private Terrain _cachedTerrain;
    private Vector2 _scroll;

    private static readonly int[] Resolutions = { 128, 256, 512, 1024 };
    private static readonly string[] ResolutionLabels = { "128", "256", "512", "1024" };

    public override void OnCreated()
    {
        Selection.selectionChanged += RepaintSceneViews;
        _settings.Resolution = Resolutions[_resolutionIndex];
        minSize = new Vector2(230f, 180f);
        maxSize = new Vector2(560f, 900f);
        size = new Vector2(280f, 620f);
    }

    public override void OnWillBeDestroyed()
        => Selection.selectionChanged -= RepaintSceneViews;

    public override void OnGUI()
    {
        Terrain terrain = GetActiveTerrain();
        if (terrain == null)
            return;

        SyncFromTerrain(terrain);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.BeginVertical();

        EditorGUILayout.LabelField(terrain.name, EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Geography V0", EditorStyles.miniLabel);
        EditorGUILayout.Space(6f);

        DrawWorldSection();
        EditorGUILayout.Space(6f);
        DrawLandformSection();
        EditorGUILayout.Space(8f);
        DrawActions(terrain);

        EditorGUILayout.EndVertical();
        EditorGUILayout.EndScrollView();
    }

    private void DrawWorldSection()
    {
        EditorGUILayout.LabelField("World", EditorStyles.boldLabel);

        _settings.Seed = EditorGUILayout.IntField("Seed", _settings.Seed);
        _resolutionIndex = EditorGUILayout.Popup("Resolution", _resolutionIndex, ResolutionLabels);
        _settings.Resolution = Resolutions[Mathf.Clamp(_resolutionIndex, 0, Resolutions.Length - 1)];

        _settings.WidthMeters = Mathf.Max(1f, EditorGUILayout.FloatField("Width (m)", _settings.WidthMeters));
        _settings.LengthMeters = Mathf.Max(1f, EditorGUILayout.FloatField("Length (m)", _settings.LengthMeters));
        _settings.MaxElevationMeters = Mathf.Max(1f, EditorGUILayout.FloatField("Max Elevation (m)", _settings.MaxElevationMeters));

        float spacingX = _settings.WidthMeters / Mathf.Max(_settings.Resolution - 1, 1);
        float spacingZ = _settings.LengthMeters / Mathf.Max(_settings.Resolution - 1, 1);
        EditorGUILayout.LabelField($"Sample spacing: {spacingX:0.00} × {spacingZ:0.00} m", EditorStyles.miniLabel);
    }

    private void DrawLandformSection()
    {
        EditorGUILayout.LabelField("Landform", EditorStyles.boldLabel);

        _settings.BaseElevation = EditorGUILayout.Slider("Base Elevation", _settings.BaseElevation, 0f, 1f);
        _settings.MacroScaleMeters = EditorGUILayout.Slider("Macro Scale (m)", _settings.MacroScaleMeters, 250f, 5000f);
        _settings.MacroAmplitude = EditorGUILayout.Slider("Macro Amplitude", _settings.MacroAmplitude, 0f, 0.75f);

        EditorGUILayout.Space(3f);
        EditorGUILayout.LabelField("Mountain Structure", EditorStyles.miniBoldLabel);
        _settings.MountainScaleMeters = EditorGUILayout.Slider("Ridge Scale (m)", _settings.MountainScaleMeters, 100f, 3000f);
        _settings.MountainAmplitude = EditorGUILayout.Slider("Ridge Amplitude", _settings.MountainAmplitude, 0f, 0.8f);
        _settings.MountainThreshold = EditorGUILayout.Slider("Highland Threshold", _settings.MountainThreshold, 0f, 1f);
        _settings.MountainBlend = EditorGUILayout.Slider("Highland Blend", _settings.MountainBlend, 0.01f, 0.4f);

        EditorGUILayout.Space(3f);
        EditorGUILayout.LabelField("Shape & Detail", EditorStyles.miniBoldLabel);
        _settings.WarpScaleMeters = EditorGUILayout.Slider("Warp Scale (m)", _settings.WarpScaleMeters, 200f, 4000f);
        _settings.WarpStrengthMeters = EditorGUILayout.Slider("Warp Strength (m)", _settings.WarpStrengthMeters, 0f, 600f);
        _settings.DetailScaleMeters = EditorGUILayout.Slider("Detail Scale (m)", _settings.DetailScaleMeters, 25f, 1000f);
        _settings.DetailAmplitude = EditorGUILayout.Slider("Detail Amplitude", _settings.DetailAmplitude, 0f, 0.25f);

        EditorGUILayout.Space(3f);
        _settings.Octaves = EditorGUILayout.IntSlider("Octaves", _settings.Octaves, 1, 8);
        _settings.Persistence = EditorGUILayout.Slider("Persistence", _settings.Persistence, 0.1f, 0.9f);
        _settings.Lacunarity = EditorGUILayout.Slider("Lacunarity", _settings.Lacunarity, 1.1f, 4f);
    }

    private void DrawActions(Terrain terrain)
    {
        using (new EditorGUI.DisabledScope(_generating))
        {
            if (GUILayout.Button("Generate Geography", GUILayout.Height(34f)))
                Generate(terrain);
        }

        using (new EditorGUI.DisabledScope(_lastData == null))
        {
            if (GUILayout.Button("Inspect Geography Data", GUILayout.Height(24f)))
                GeographyDebugWindow.Open(_lastData);
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.HelpBox(
            "Terrain Tool now generates geography only. Water, coastlines and rivers belong to Hydrology rather than being painted into the heightmap here.",
            MessageType.Info);
    }

    private void Generate(Terrain terrain)
    {
        _generating = true;
        try
        {
            _settings.Validate();
            Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Generate Infinity Geography");

            _lastData = GeographyGenerator.Generate(_settings);
            GeographyAsset asset = PersistGeography(terrain, _lastData);
            _lastAsset = asset;
            LinkTerrainToGeography(terrain, asset);
            UnityTerrainPresenter.Apply(terrain, _lastData);

            GeographyDebugWindow.Open(_lastData);
            SceneView.RepaintAll();
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            _generating = false;
        }
    }

    private void SyncFromTerrain(Terrain terrain)
    {
        if (_cachedTerrain == terrain)
            return;

        _cachedTerrain = terrain;
        _lastData = null;
        _lastAsset = null;

        GeographyReference reference = terrain.GetComponent<GeographyReference>();
        GeographyAsset asset = reference != null ? reference.Geography : null;
        if (asset == null || !asset.HasData)
            return;

        _lastAsset = asset;
        _lastData = WorldEditorDataCache.GetGeography(asset);
        _settings = asset.Settings.Clone();

        int closest = 0;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < Resolutions.Length; i++)
        {
            int distance = Mathf.Abs(Resolutions[i] - _settings.Resolution);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                closest = i;
            }
        }
        _resolutionIndex = closest;
    }

    private GeographyAsset PersistGeography(Terrain terrain, GeographyData data)
    {
        string geographyFolder = InfinityGeneratedPaths.Ensure("Geography");

        GeographyReference reference = terrain.GetComponent<GeographyReference>();
        GeographyAsset asset = reference != null ? reference.Geography : null;

        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<GeographyAsset>();
            string safeName = MakeSafeFileName(terrain.name);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{geographyFolder}/{safeName}_Geography.asset");
            AssetDatabase.CreateAsset(asset, path);
        }

        Undo.RecordObject(asset, "Store Infinity Geography");
        asset.Store(data, _settings);
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        WorldEditorDataCache.Remember(asset, data);
        return asset;
    }

    private static void LinkTerrainToGeography(Terrain terrain, GeographyAsset asset)
    {
        GeographyReference reference = terrain.GetComponent<GeographyReference>();
        if (reference == null)
            reference = Undo.AddComponent<GeographyReference>(terrain.gameObject);

        Undo.RecordObject(reference, "Link Infinity Geography");
        reference.Geography = asset;
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

    private static void RepaintSceneViews()
        => SceneView.RepaintAll();
}
