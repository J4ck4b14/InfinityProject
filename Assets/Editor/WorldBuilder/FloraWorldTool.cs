using System.Diagnostics;
using InfinityProject.World.Climate;
using InfinityProject.World.Flora;
using InfinityProject.World.Geography;
using InfinityProject.World.Ground;
using InfinityProject.World.Hydrology;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;

[Overlay(typeof(SceneView), "Flora Tool", false)]
[Icon("d_Terrain Icon")]
public sealed class FloraWorldTool : IMGUIOverlay, ITransientOverlay
{
    public bool visible
    {
        get
        {
            Terrain terrain = GetActiveTerrain();
            if (terrain == null) return false;
            GeographyAsset geography = terrain.GetComponent<GeographyReference>()?.Geography;
            HydrologyAsset hydrology = terrain.GetComponent<HydrologyReference>()?.Hydrology;
            GroundConditionAsset ground = terrain.GetComponent<GroundConditionReference>()?.GroundConditions;
            ClimateAsset climate = terrain.GetComponent<ClimateReference>()?.Climate;
            return geography != null && geography.HasData && hydrology != null && hydrology.HasMetadata &&
                   ground != null && ground.HasMetadata && climate != null && climate.HasMetadata;
        }
    }

    private FloraGenerationSettings _settings = new();
    private FloraAsset _lastAsset;
    private FloraData _lastData;
    private Terrain _cachedTerrain;
    private int _selectedSpecies;
    private bool _stale;
    private bool _cacheMissing;
    private bool _generating;
    private string _lastTiming = string.Empty;
    private Vector2 _scroll;

    public override void OnCreated()
    {
        Selection.selectionChanged += RepaintSceneViews;
        minSize = new Vector2(270f, 220f);
        maxSize = new Vector2(650f, 860f);
        size = new Vector2(345f, 620f);
    }

    public override void OnWillBeDestroyed() => Selection.selectionChanged -= RepaintSceneViews;

    public override void OnGUI()
    {
        Terrain terrain = GetActiveTerrain();
        if (terrain == null) return;
        GeographyAsset geography = terrain.GetComponent<GeographyReference>()?.Geography;
        HydrologyAsset hydrology = terrain.GetComponent<HydrologyReference>()?.Hydrology;
        GroundConditionAsset ground = terrain.GetComponent<GroundConditionReference>()?.GroundConditions;
        ClimateAsset climate = terrain.GetComponent<ClimateReference>()?.Climate;
        if (geography == null || hydrology == null || ground == null || climate == null) return;

        SyncFromTerrain(terrain, geography, hydrology, ground, climate);
        bool dependenciesValid = hydrology.Matches(geography) && ground.Matches(geography, hydrology, climate) && climate.Matches(geography);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.LabelField(terrain.name, EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Flora V1.1 — water availability + living biomass parameters", EditorStyles.miniLabel);
        EditorGUILayout.Space(5f);

        _settings.Validate();
        DrawSpeciesEditor();

        EditorGUILayout.Space(8f);
        using (new EditorGUI.DisabledScope(_generating || !dependenciesValid || _settings.Species.Count == 0))
        {
            if (GUILayout.Button("Generate Flora Potential", GUILayout.Height(34f)))
                Generate(terrain, geography, hydrology, ground, climate);
        }
        using (new EditorGUI.DisabledScope(_lastData == null || _lastData.SpeciesCount == 0))
        {
            if (GUILayout.Button("Inspect Flora Data", GUILayout.Height(24f)))
                FloraDebugWindow.Open(_lastData, _selectedSpecies);
        }

        if (!string.IsNullOrEmpty(_lastTiming)) EditorGUILayout.LabelField(_lastTiming, EditorStyles.miniLabel);
        if (_lastData != null && _lastData.SpeciesCount > 0)
            DrawSelectedSpeciesCoverage();
        if (!dependenciesValid)
            EditorGUILayout.HelpBox("One or more upstream datasets are stale. Regenerate Hydrology / Ground / Climate as indicated before Flora.", MessageType.Warning);
        else if (_stale)
            EditorGUILayout.HelpBox("Flora belongs to older upstream generation revisions. Regenerate it.", MessageType.Warning);
        else if (_cacheMissing && _lastAsset != null)
            EditorGUILayout.HelpBox("Flora metadata exists, but its derived cache is missing. Regenerate Flora.", MessageType.Warning);
        else
            EditorGUILayout.HelpBox("Flora V1.1 consumes Temperature + Ground Water Availability + Soil Retention. Precipitation and topographic wetness are no longer independent survival gates; current biomass remains separately authoritative in Ecology Lab.", MessageType.Info);

        EditorGUILayout.EndScrollView();
    }


    private void DrawSelectedSpeciesCoverage()
    {
        int index = Mathf.Clamp(_selectedSpecies, 0, _lastData.SpeciesCount - 1);
        FloraSpeciesData species = _lastData.Species[index];
        FloraSpeciesProfile profile = _settings.FindSpecies(species.StableId);
        float threshold = profile != null ? profile.EstablishmentThreshold : 0f;
        int viable = 0; double suitabilitySum = 0d; int[] limits = new int[5];
        for (int i = 0; i < species.EstablishmentSuitability.Length; i++)
        {
            float value = species.EstablishmentSuitability[i]; suitabilitySum += value; if (value > threshold) viable++;
            int factor = species.LimitingFactor[i]; if (factor >= 0 && factor < limits.Length) limits[factor]++;
        }
        int count = species.EstablishmentSuitability.Length; float viablePercent = count > 0 ? 100f * viable / count : 0f;
        float mean = count > 0 ? (float)(suitabilitySum / count) : 0f;
        EditorGUILayout.LabelField($"{species.DisplayName}: viable {viablePercent:0.0}% | mean suitability {mean:0.000} | threshold {threshold:0.00}", EditorStyles.miniLabel);
        if (count > 0)
            EditorGUILayout.LabelField($"limiters: temp {100f * limits[(int)FloraLimitingFactor.Temperature] / count:0.0}% | water {100f * limits[(int)FloraLimitingFactor.WaterAvailability] / count:0.0}% | retention {100f * limits[(int)FloraLimitingFactor.SoilRetention] / count:0.0}% | multiple {100f * limits[(int)FloraLimitingFactor.Multiple] / count:0.0}%", EditorStyles.miniLabel);
    }

    private void DrawSpeciesEditor()
    {
        EditorGUILayout.LabelField("Species response profiles", EditorStyles.boldLabel);
        if (_settings.Species.Count == 0)
        {
            EditorGUILayout.HelpBox("Add at least one species profile to generate Flora.", MessageType.Info);
            if (GUILayout.Button("Add Species"))
            {
                _settings.Species.Add(new FloraSpeciesProfile { StableId = "species_0", DisplayName = "New Species" });
                _selectedSpecies = 0;
            }
            return;
        }

        string[] names = new string[_settings.Species.Count];
        for (int i = 0; i < names.Length; i++) names[i] = _settings.Species[i]?.DisplayName ?? $"Species {i}";
        _selectedSpecies = Mathf.Clamp(_selectedSpecies, 0, _settings.Species.Count - 1);
        _selectedSpecies = EditorGUILayout.Popup("Profile", _selectedSpecies, names);
        FloraSpeciesProfile p = _settings.Species[_selectedSpecies];

        p.DisplayName = EditorGUILayout.TextField("Display name", p.DisplayName);
        p.StableId = EditorGUILayout.TextField("Stable ID", p.StableId);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Temperature (°C)", EditorStyles.boldLabel);
        p.MinimumTemperatureCelsius = EditorGUILayout.FloatField("Minimum", p.MinimumTemperatureCelsius);
        p.OptimalTemperatureCelsius = EditorGUILayout.FloatField("Optimal", p.OptimalTemperatureCelsius);
        p.MaximumTemperatureCelsius = EditorGUILayout.FloatField("Maximum", p.MaximumTemperatureCelsius);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Water availability potential", EditorStyles.boldLabel);
        p.MinimumWaterAvailabilityPotential = EditorGUILayout.Slider("Minimum", p.MinimumWaterAvailabilityPotential, 0f, 1f);
        p.OptimalWaterAvailabilityPotential = EditorGUILayout.Slider("Optimal", p.OptimalWaterAvailabilityPotential, 0f, 1f);
        p.MaximumWaterAvailabilityPotential = EditorGUILayout.Slider("Maximum", p.MaximumWaterAvailabilityPotential, 0f, 1f);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Soil retention", EditorStyles.boldLabel);
        p.MinimumSoilRetentionPotential = EditorGUILayout.Slider("Minimum", p.MinimumSoilRetentionPotential, 0f, 1f);
        p.FullSoilRetentionPotential = EditorGUILayout.Slider("Full response", p.FullSoilRetentionPotential, 0f, 1f);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Living biomass", EditorStyles.boldLabel);
        p.EstablishmentThreshold = EditorGUILayout.Slider("Establishment threshold", p.EstablishmentThreshold, 0f, 1f);
        p.InitialOccupancyFraction = EditorGUILayout.Slider("Initial occupancy", p.InitialOccupancyFraction, 0f, 1f);
        p.MaximumBiomassKgPerSquareMeter = EditorGUILayout.FloatField("Ideal K (kg/m²)", p.MaximumBiomassKgPerSquareMeter);
        p.IntrinsicGrowthRatePerDay = EditorGUILayout.FloatField("Growth rate / day", p.IntrinsicGrowthRatePerDay);
        p.SeedBankRecruitmentRatePerDay = EditorGUILayout.FloatField("Recruitment / day", p.SeedBankRecruitmentRatePerDay);
        p.StressMortalityRatePerDay = EditorGUILayout.FloatField("Stress mortality / day", p.StressMortalityRatePerDay);
        p.CompetitionStrength = EditorGUILayout.Slider("Competition strength", p.CompetitionStrength, 0f, 1f);
        p.Validate(_selectedSpecies);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Species"))
        {
            int index = _settings.Species.Count;
            _settings.Species.Add(new FloraSpeciesProfile { StableId = $"species_{index}", DisplayName = $"New Species {index + 1}" });
            _selectedSpecies = index;
        }
        using (new EditorGUI.DisabledScope(_settings.Species.Count <= 1))
        {
            if (GUILayout.Button("Remove Selected"))
            {
                _settings.Species.RemoveAt(_selectedSpecies);
                _selectedSpecies = Mathf.Clamp(_selectedSpecies, 0, _settings.Species.Count - 1);
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    private void Generate(
        Terrain terrain,
        GeographyAsset geographyAsset,
        HydrologyAsset hydrologyAsset,
        GroundConditionAsset groundAsset,
        ClimateAsset climateAsset)
    {
        _generating = true;
        try
        {
            _settings.Validate();
            if (!hydrologyAsset.Matches(geographyAsset)) throw new System.InvalidOperationException("Hydrology is stale relative to Geography.");
            if (!groundAsset.Matches(geographyAsset, hydrologyAsset, climateAsset)) throw new System.InvalidOperationException("Ground Conditions are stale relative to Geography/Hydrology/Climate.");
            if (!climateAsset.Matches(geographyAsset)) throw new System.InvalidOperationException("Climate is stale relative to Geography.");
            if (!WorldEditorDataCache.TryGetGround(groundAsset, out GroundConditionData groundData))
                throw new System.InvalidOperationException("Ground cache is missing. Regenerate Ground Conditions first.");
            if (!WorldEditorDataCache.TryGetClimate(climateAsset, out ClimateData climateData))
                throw new System.InvalidOperationException("Climate cache is missing. Regenerate Climate first.");

            var total = Stopwatch.StartNew();
            var phase = Stopwatch.StartNew();
            GeographyData geographyData = WorldEditorDataCache.GetGeography(geographyAsset);
            long geographyMs = phase.ElapsedMilliseconds;
            phase.Restart();
            _lastData = FloraGenerator.Generate(geographyData, groundData, climateData, _settings);
            long solveMs = phase.ElapsedMilliseconds;
            phase.Restart();
            _lastAsset = PersistMetadata(terrain, geographyAsset, hydrologyAsset, groundAsset, climateAsset, _lastData);
            long metadataMs = phase.ElapsedMilliseconds;
            phase.Restart();
            FloraBinaryCache.Save(_lastAsset, _lastData);
            WorldEditorDataCache.Remember(_lastAsset, _lastData);
            long cacheMs = phase.ElapsedMilliseconds;
            LinkTerrain(terrain, _lastAsset);
            _stale = false;
            _cacheMissing = false;
            phase.Restart();
            if (_lastData.SpeciesCount > 0) FloraDebugWindow.Open(_lastData, _selectedSpecies);
            long inspectorMs = phase.ElapsedMilliseconds;
            total.Stop();
            _lastTiming = $"Last build: {total.Elapsed.TotalSeconds:0.00}s (solve {solveMs / 1000f:0.00}s, cache {cacheMs / 1000f:0.00}s)";
            UnityEngine.Debug.Log($"[Infinity Flora] {_lastData.Resolution}² | {_lastData.SpeciesCount} species | timings ms: geography {geographyMs:N0}, solve {solveMs:N0}, metadata {metadataMs:N0}, cache {cacheMs:N0}, inspector {inspectorMs:N0}, total {total.ElapsedMilliseconds:N0}");
            SceneView.RepaintAll();
        }
        catch (System.Exception exception) { UnityEngine.Debug.LogException(exception); }
        finally { _generating = false; }
    }

    private void SyncFromTerrain(
        Terrain terrain,
        GeographyAsset geography,
        HydrologyAsset hydrology,
        GroundConditionAsset ground,
        ClimateAsset climate)
    {
        if (_cachedTerrain == terrain)
        {
            if (_lastAsset != null) _stale = !_lastAsset.Matches(geography, hydrology, ground, climate);
            return;
        }

        _cachedTerrain = terrain;
        _lastAsset = terrain.GetComponent<FloraReference>()?.Flora;
        _lastData = null;
        _stale = false;
        _cacheMissing = false;
        _lastTiming = string.Empty;
        if (_lastAsset == null || !_lastAsset.HasMetadata) return;
        _settings = _lastAsset.ModelVersion < 2 ? new FloraGenerationSettings() : _lastAsset.Settings.Clone();
        _settings.Validate();
        _selectedSpecies = Mathf.Clamp(_selectedSpecies, 0, Mathf.Max(0, _settings.Species.Count - 1));
        _stale = !_lastAsset.Matches(geography, hydrology, ground, climate);
        if (!_stale && !WorldEditorDataCache.TryGetFlora(_lastAsset, out _lastData)) _cacheMissing = true;
    }

    private FloraAsset PersistMetadata(
        Terrain terrain,
        GeographyAsset geography,
        HydrologyAsset hydrology,
        GroundConditionAsset ground,
        ClimateAsset climate,
        FloraData data)
    {
        string folder = InfinityGeneratedPaths.Ensure("Flora");
        FloraAsset asset = terrain.GetComponent<FloraReference>()?.Flora;
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<FloraAsset>();
            AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{MakeSafeFileName(terrain.name)}_Flora.asset"));
        }
        Undo.RecordObject(asset, "Store Infinity Flora Metadata");
        asset.StoreMetadata(data, _settings, geography, hydrology, ground, climate);
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        return asset;
    }

    private static void LinkTerrain(Terrain terrain, FloraAsset asset)
    {
        FloraReference reference = terrain.GetComponent<FloraReference>();
        if (reference == null) reference = Undo.AddComponent<FloraReference>(terrain.gameObject);
        Undo.RecordObject(reference, "Link Infinity Flora");
        reference.Flora = asset;
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
