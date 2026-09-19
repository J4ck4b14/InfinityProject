using System.Diagnostics;
using InfinityProject.World.Climate;
using InfinityProject.World.Ecology;
using InfinityProject.World.Fauna;
using InfinityProject.World.Fauna.ECS;
using InfinityProject.World.Flora;
using InfinityProject.World.Geography;
using InfinityProject.World.Ground;
using InfinityProject.World.Hydrology;
using InfinityProject.World.Presentation;
using InfinityProject.World.Timekeeping;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;

[Overlay(typeof(SceneView), "Ecology Lab", false)]
[Icon("d_Terrain Icon")]
public sealed class EcologyWorldTool : IMGUIOverlay, ITransientOverlay
{
    public bool visible
    {
        get
        {
            Terrain terrain = GetActiveTerrain();
            if (terrain == null) return false;
            return terrain.GetComponent<GeographyReference>()?.Geography != null &&
                   terrain.GetComponent<FloraReference>()?.Flora != null;
        }
    }

    private Terrain _cachedTerrain;
    private GeographyAsset _geographyAsset;
    private GeographyData _geographyData;
    private int _geographyGenerationRevision = -1;

    private FloraAsset _floraAsset;
    private FloraData _floraPotential;
    private int _floraGenerationRevision = -1;

    private FloraStateAsset _floraStateAsset;
    private FloraSimulationData _floraState;
    private int _floraStateRevision = -1;
    private bool _floraStateCacheMissing;
    private FloraSimulationSettings _floraSimulationSettings = new();

    private FaunaAsset _faunaAsset;
    private FaunaSimulationData _faunaState;
    private int _faunaRevision = -1;
    private bool _faunaCacheMissing;
    private FaunaGenerationSettings _faunaSettings = new();
    private FaunaEcsMovementBackend _faunaEcsMovement;

    private int _selectedFloraSpecies;
    private float _disturbanceX;
    private float _disturbanceZ;
    private float _disturbanceRadius = 100f;
    private float _disturbanceFraction = 0.8f;
    private bool _drawFaunaInScene = true;
    private bool _busy;
    private string _lastTiming = string.Empty;
    private Vector2 _scroll;

    public override void OnCreated()
    {
        Selection.selectionChanged += RepaintSceneViews;
        minSize = new Vector2(290f, 260f);
        maxSize = new Vector2(720f, 980f);
        size = new Vector2(390f, 780f);
    }

    public override void OnWillBeDestroyed()
    {
        Selection.selectionChanged -= RepaintSceneViews;
        FaunaSceneDebug.Clear();
        FaunaEcsPreview.Stop();
        DisposeFaunaEcsMovement();
    }

    public override void OnGUI()
    {
        Terrain terrain = GetActiveTerrain();
        if (terrain == null) return;

        GeographyAsset geography = terrain.GetComponent<GeographyReference>()?.Geography;
        HydrologyAsset hydrology = terrain.GetComponent<HydrologyReference>()?.Hydrology;
        GroundConditionAsset ground = terrain.GetComponent<GroundConditionReference>()?.GroundConditions;
        ClimateAsset climate = terrain.GetComponent<ClimateReference>()?.Climate;
        FloraAsset flora = terrain.GetComponent<FloraReference>()?.Flora;
        if (geography == null || flora == null) return;

        bool staticFloraCurrent = hydrology != null && ground != null && climate != null &&
                                  flora.Matches(geography, hydrology, ground, climate);
        SyncFromTerrain(terrain, geography, flora);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.LabelField(terrain.name, EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Living ecology — Flora V1 + Fauna ECS V1", EditorStyles.miniLabel);
        EditorGUILayout.Space(5f);
        DrawTimeSection(terrain);
        EditorGUILayout.Space(10f);

        if (!staticFloraCurrent)
        {
            EditorGUILayout.HelpBox("Static Flora potential is stale. Regenerate its upstream dependencies and Flora before advancing living ecology.", MessageType.Warning);
            EditorGUILayout.EndScrollView();
            FaunaSceneDebug.Clear();
            return;
        }
        if (_floraPotential == null)
        {
            EditorGUILayout.HelpBox("Static Flora metadata exists but its cache could not be loaded. Regenerate Flora.", MessageType.Warning);
            EditorGUILayout.EndScrollView();
            FaunaSceneDebug.Clear();
            return;
        }

        DrawFloraSection(terrain, geography, flora);
        EditorGUILayout.Space(10f);
        DrawFaunaSection(terrain, geography, flora);
        EditorGUILayout.Space(10f);
        DrawFaunaEcsSection(terrain);

        if (!string.IsNullOrEmpty(_lastTiming))
        {
            EditorGUILayout.Space(5f);
            EditorGUILayout.LabelField(_lastTiming, EditorStyles.miniLabel);
        }

        EditorGUILayout.EndScrollView();

        if (_drawFaunaInScene && _faunaState != null && _geographyData != null)
            FaunaSceneDebug.Set(terrain, _geographyData, _faunaState, true);
        else
            FaunaSceneDebug.Clear();

        if (Application.isPlaying)
        {
            if (FaunaEcsPreview.IsActive) FaunaEcsPreview.Stop();
        }
        else if (FaunaEcsPreview.IsActive && _faunaState != null && _geographyData != null)
        {
            FaunaEcsMovementBackend ecs = RequireFaunaEcsMovement();
            FaunaEcsPreview.StartOrSync(terrain, _geographyData, _faunaState, ecs.Bridge);
        }
    }

    private void DrawTimeSection(Terrain terrain)
    {
        EditorGUILayout.LabelField("Time V1", EditorStyles.boldLabel);
        InfinityTimeController time = terrain.GetComponent<InfinityTimeController>();
        if (time == null)
        {
            EditorGUILayout.HelpBox("Simulation time and world time are separate. Add the controller before ECS movement starts using the new clock.", MessageType.Info);
            if (GUILayout.Button("Add Time Controller", GUILayout.Height(26f)))
            {
                time = Undo.AddComponent<InfinityTimeController>(terrain.gameObject);
                EditorUtility.SetDirty(terrain.gameObject);
            }
            return;
        }

        float speed = EditorGUILayout.FloatField("Simulation speed", time.SimulationSpeed);
        float dayMinutes = EditorGUILayout.FloatField("World day (sim minutes)", time.WorldDayDurationMinutes);
        float startHour = EditorGUILayout.Slider("Start hour", time.StartingWorldHour, 0f, 23.999f);
        bool paused = EditorGUILayout.Toggle(Application.isPlaying ? "Paused" : "Start paused", Application.isPlaying ? time.IsPaused : time.StartPaused);

        speed = Mathf.Max(0f, speed);
        dayMinutes = Mathf.Max(0.01f, dayMinutes);
        if (!Mathf.Approximately(speed, time.SimulationSpeed) ||
            !Mathf.Approximately(dayMinutes, time.WorldDayDurationMinutes) ||
            !Mathf.Approximately(startHour, time.StartingWorldHour) ||
            (!Application.isPlaying && paused != time.StartPaused) ||
            (Application.isPlaying && paused != time.IsPaused))
        {
            Undo.RecordObject(time, "Change Infinity Time Settings");
            time.SetSimulationSpeed(speed);
            time.SetWorldDayDurationMinutes(dayMinutes);
            time.SetStartingWorldHour(startHour);
            if (Application.isPlaying) time.SetPaused(paused);
            else time.SetStartPaused(paused);
            EditorUtility.SetDirty(time);
        }

        if (Application.isPlaying)
        {
            EditorGUILayout.LabelField(
                $"Simulation {time.Simulation.ElapsedSeconds:0.00}s | World day {time.World.DayIndex} | {time.World.Hour:00.00}h",
                EditorStyles.miniLabel);

            InfinityProject.World.Ecology.InfinityLiveEcologyController live = terrain.GetComponent<InfinityProject.World.Ecology.InfinityLiveEcologyController>();
            if (live != null && live.IsInitialized)
            {
                EditorGUILayout.LabelField(
                    $"Live ecology: {live.AliveCount}/{live.AgentCount} alive | hunger {live.MeanHunger01:0.00} | health {live.MeanHealth01:0.00} | eaten {live.ConsumedFoodKg:N1} kg",
                    EditorStyles.miniLabel);
                EditorGUILayout.LabelField(
                    $"Processed fauna {live.ProcessedFaunaDays:0.000}d | flora {live.ProcessedFloraDays:0.000}d | {live.PresentationCount} prefabs",
                    EditorStyles.miniLabel);

                InfinityWorldPresentationController worldPresentation = terrain.GetComponent<InfinityWorldPresentationController>();
                if (worldPresentation != null)
                {
                    EditorGUILayout.LabelField(
                        $"World presentation: {worldPresentation.FloraInstances:N0} flora instances / {worldPresentation.FloraBatches} batches | {worldPresentation.RiverSegments:N0} river segments / {worldPresentation.RiverChunks} chunks",
                        EditorStyles.miniLabel);

                    EditorGUILayout.BeginHorizontal();
                    bool terrainPresentation = GUILayout.Toggle(worldPresentation.TerrainPresentationEnabled, "Terrain", "Button");
                    bool riverPresentation = GUILayout.Toggle(worldPresentation.RiverPresentationEnabled, "Rivers", "Button");
                    bool floraPresentation = GUILayout.Toggle(worldPresentation.FloraPresentationEnabled, "Flora", "Button");
                    EditorGUILayout.EndHorizontal();

                    if (terrainPresentation != worldPresentation.TerrainPresentationEnabled)
                        worldPresentation.SetTerrainPresentationEnabled(terrainPresentation);
                    if (riverPresentation != worldPresentation.RiverPresentationEnabled)
                        worldPresentation.SetRiverPresentationEnabled(riverPresentation);
                    if (floraPresentation != worldPresentation.FloraPresentationEnabled)
                        worldPresentation.SetFloraPresentationEnabled(floraPresentation);
                }
            }
        }

        EditorGUILayout.HelpBox("Simulation speed affects physical time. World-day length only maps simulation time to the calendar/schedule clock.", MessageType.None);
    }

    private void DrawFloraSection(Terrain terrain, GeographyAsset geography, FloraAsset flora)
    {
        EditorGUILayout.LabelField("Flora V1 — living biomass", EditorStyles.boldLabel);
        _floraSimulationSettings.Validate();
        _floraSimulationSettings.MaximumSubstepDays = EditorGUILayout.Slider("Max biomass step (days)", _floraSimulationSettings.MaximumSubstepDays, 0.05f, 2f);
        _floraSimulationSettings.MinimumCompetitionCapacityFraction = EditorGUILayout.Slider("Competition floor", _floraSimulationSettings.MinimumCompetitionCapacityFraction, 0.05f, 1f);

        bool stateExists = _floraStateAsset != null && _floraState != null;
        bool stateCurrent = stateExists && _floraStateAsset.Matches(flora);
        bool canRebind = stateExists && !stateCurrent && _floraStateAsset.CanRebind(flora);

        if (_floraStateCacheMissing)
            EditorGUILayout.HelpBox("Living Flora metadata exists, but its binary state cache is missing or incompatible. Reinitialize living Flora; no stale biomass is being displayed.", MessageType.Warning);
        else if (!stateExists)
            EditorGUILayout.HelpBox("No authoritative living biomass state exists yet. Initializing copies generated initial biomass into a separate mutable state.", MessageType.Info);
        else if (!stateCurrent)
            EditorGUILayout.HelpBox(canRebind
                ? "Living Flora is bound to an older Flora-potential revision. Rebind to preserve biomass/history and let the new environment cause recovery or dieback."
                : "Living Flora cannot safely rebind because Geography changed. Reset it against the new physical world.", MessageType.Warning);
        else
            EditorGUILayout.LabelField($"Day {_floraState.SimulationTimeDays:0.00} | {_floraState.SpeciesCount} species", EditorStyles.miniLabel);

        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(_busy))
        {
            if (GUILayout.Button(stateExists ? "Reset Living Flora" : "Initialize Living Flora", GUILayout.Height(28f)))
                InitializeFloraState(terrain, flora);
        }
        using (new EditorGUI.DisabledScope(_busy || !canRebind))
        {
            if (GUILayout.Button("Rebind / Preserve", GUILayout.Height(28f)))
                RebindFloraState(terrain, geography, flora);
        }
        EditorGUILayout.EndHorizontal();

        using (new EditorGUI.DisabledScope(!stateCurrent || _busy))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Flora +1d")) StepFlora(1f);
            if (GUILayout.Button("+10d")) StepFlora(10f);
            if (GUILayout.Button("+30d")) StepFlora(30f);
            EditorGUILayout.EndHorizontal();
        }

        if (_floraState != null && _floraState.SpeciesCount > 0)
        {
            string[] names = new string[_floraState.SpeciesCount];
            for (int i = 0; i < names.Length; i++) names[i] = _floraState.Species[i].DisplayName;
            _selectedFloraSpecies = EditorGUILayout.Popup("Disturb species", Mathf.Clamp(_selectedFloraSpecies, 0, names.Length - 1), names);
            _disturbanceX = EditorGUILayout.Slider("Center X (m)", _disturbanceX, 0f, _floraState.WidthMeters);
            _disturbanceZ = EditorGUILayout.Slider("Center Z (m)", _disturbanceZ, 0f, _floraState.LengthMeters);
            _disturbanceRadius = EditorGUILayout.Slider("Radius (m)", _disturbanceRadius, 5f, Mathf.Max(10f, Mathf.Min(_floraState.WidthMeters, _floraState.LengthMeters) * 0.5f));
            _disturbanceFraction = EditorGUILayout.Slider("Remove fraction", _disturbanceFraction, 0f, 1f);
            using (new EditorGUI.DisabledScope(!stateCurrent || _busy || _disturbanceFraction <= 0f))
            {
                if (GUILayout.Button("Inject Biomass Removal")) DisturbFlora();
            }

            if (GUILayout.Button("Inspect Living Flora"))
                FloraStateDebugWindow.Open(_floraPotential, _floraState, _selectedFloraSpecies);
        }
    }

    private void DrawFaunaSection(Terrain terrain, GeographyAsset geography, FloraAsset flora)
    {
        EditorGUILayout.LabelField("Fauna ECS V1 — deer consumer", EditorStyles.boldLabel);
        _faunaSettings.Validate();
        FaunaSpeciesProfile p = _faunaSettings.Species;
        _faunaSettings.Seed = EditorGUILayout.IntField("Seed", _faunaSettings.Seed);
        p.AgentCount = EditorGUILayout.IntSlider("Deer", p.AgentCount, 1, 300);
        p.HerdSize = EditorGUILayout.IntSlider("Herd size", p.HerdSize, 1, Mathf.Max(1, p.AgentCount));
        p.MaxTraversableSlopeDegrees = EditorGUILayout.Slider("Max slope (°)", p.MaxTraversableSlopeDegrees, 5f, 60f);
        p.MoveSpeedMetersPerDay = EditorGUILayout.FloatField("Move speed (m/day)", p.MoveSpeedMetersPerDay);
        p.ForagePerceptionRadiusMeters = EditorGUILayout.FloatField("Forage perception (m)", p.ForagePerceptionRadiusMeters);
        p.BrowseRadiusMeters = EditorGUILayout.FloatField("Browse radius (m)", p.BrowseRadiusMeters);
        p.ExplorationWeight = EditorGUILayout.Slider("Exploration weight", p.ExplorationWeight, 0f, 0.5f);
        p.DailyFoodRequirementKg = EditorGUILayout.FloatField("Food need (kg/day)", p.DailyFoodRequirementKg);
        _faunaSettings.MaximumMovementSubstepDays = EditorGUILayout.Slider("Movement substep (days)", _faunaSettings.MaximumMovementSubstepDays, 0.005f, 0.1f);
        p.Validate();

        bool floraCurrent = _floraStateAsset != null && _floraState != null && _floraStateAsset.Matches(flora);
        bool faunaExists = _faunaAsset != null && _faunaState != null;
        bool faunaCurrent = faunaExists && _faunaAsset.Matches(geography, flora);
        bool faunaCanRebind = faunaExists && !faunaCurrent && _faunaAsset.CanRebind(geography, flora);

        if (_faunaCacheMissing)
            EditorGUILayout.HelpBox("Fauna metadata exists, but its binary agent-state cache is missing or incompatible. Reinitialize Deer; no stale agents are being displayed.", MessageType.Warning);
        if (faunaExists)
            EditorGUILayout.LabelField($"Day {_faunaState.SimulationTimeDays:0.00} | alive {_faunaState.AliveCount}/{_faunaState.AgentCount} | hunger {_faunaState.MeanHunger01:0.00} | eaten {_faunaState.CumulativeFoodConsumedKg:N1} kg", EditorStyles.miniLabel);
        if (faunaExists && !faunaCurrent)
            EditorGUILayout.HelpBox(faunaCanRebind
                ? "Fauna belongs to an older Flora-potential revision. Rebind metadata after living Flora is rebound; agent history can be preserved."
                : "Fauna cannot safely rebind because Geography changed; reset agents.", MessageType.Warning);

        using (new EditorGUI.DisabledScope(_busy || !floraCurrent))
        {
            if (GUILayout.Button(faunaExists ? "Reset Deer" : "Initialize Deer", GUILayout.Height(28f)))
                InitializeFauna(terrain, geography, flora);
        }

        bool clocksAligned = faunaExists && _floraState != null && System.Math.Abs(_faunaState.SimulationTimeDays - _floraState.SimulationTimeDays) <= EcologySimulator.NumericalClockRepairToleranceDays;
        using (new EditorGUI.DisabledScope(_busy || !floraCurrent || !faunaCurrent || !clocksAligned))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Ecology +1d")) StepEcology(geography, 1f);
            if (GUILayout.Button("+10d")) StepEcology(geography, 10f);
            if (GUILayout.Button("+30d")) StepEcology(geography, 30f);
            EditorGUILayout.EndHorizontal();
        }
        if (faunaExists && !clocksAligned)
            EditorGUILayout.HelpBox("Flora and Fauna clocks differ because Flora was advanced alone. Reset Deer to the current Flora day before running the coupled loop.", MessageType.Info);

        _drawFaunaInScene = EditorGUILayout.Toggle("Draw deer in Scene", _drawFaunaInScene);
        using (new EditorGUI.DisabledScope(!faunaExists))
        {
            if (GUILayout.Button("Inspect Fauna / Grazing"))
                FaunaDebugWindow.Open(_floraPotential, _floraState, _faunaState, _faunaSettings);
        }

        EditorGUILayout.HelpBox("Coupled stepping uses ECS-owned movement, managed feeding/physiology, then Flora growth. State time remains coordinated explicitly.", MessageType.None);
    }

    private void DrawFaunaEcsSection(Terrain terrain)
    {
        EditorGUILayout.LabelField("Fauna ECS V1 — movement authority", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("ECS owns position/heading; V0 still owns feeding and physiology", EditorStyles.miniLabel);

        if (!FaunaEcsPreview.HasDoePrefab)
        {
            EditorGUILayout.HelpBox($"Doe prefab is missing at {DoePrefabBuilder.PrefabPath}.", MessageType.Warning);
            if (GUILayout.Button("Build Doe Prefab")) DoePrefabBuilder.Build();
        }

        bool canPreview = !_busy && _faunaState != null && _geographyData != null && FaunaEcsPreview.HasDoePrefab;
        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(!canPreview))
        {
            if (GUILayout.Button(FaunaEcsPreview.IsActive ? "Refresh ECS Preview" : "Start ECS Preview", GUILayout.Height(26f)))
            {
                FaunaEcsMovementBackend ecs = RequireFaunaEcsMovement();
                FaunaEcsPreview.StartOrSync(terrain, _geographyData, _faunaState, ecs.Bridge);
            }
        }
        using (new EditorGUI.DisabledScope(!FaunaEcsPreview.IsActive))
        {
            if (GUILayout.Button("Stop", GUILayout.Width(64f), GUILayout.Height(26f)))
                FaunaEcsPreview.Stop();
        }
        EditorGUILayout.EndHorizontal();

        if (FaunaEcsPreview.IsActive)
            EditorGUILayout.LabelField($"ECS entities {FaunaEcsPreview.EntityCount} | visible {FaunaEcsPreview.PresentationCount} | pooled {FaunaEcsPreview.PooledCount}", EditorStyles.miniLabel);
        if (_faunaEcsMovement != null)
            EditorGUILayout.LabelField($"Last ECS step: {_faunaEcsMovement.LastSubstepCount} substeps | {_faunaEcsMovement.LastDirtyForageSamples} forage samples refreshed", EditorStyles.miniLabel);

        if (Application.isPlaying && GUILayout.Button("Open Procedural Animator Debug"))
            FaunaAnimatorDebugWindow.Open();

        EditorGUILayout.HelpBox(
            "Movement is ECS-owned. Feeding and physiology currently remain on the managed simulation path.",
            MessageType.Info);
    }

    private void InitializeFloraState(Terrain terrain, FloraAsset flora)
    {
        RunBusy("Initialize living Flora", () =>
        {
            _floraState = FloraSimulationData.CreateInitial(_floraPotential);
            _floraStateAsset = PersistFloraStateAsset(terrain, flora, _floraState);
            FloraStateBinaryCache.Save(_floraStateAsset, _floraState);
            _floraStateRevision = _floraStateAsset.SimulationRevision;
            _floraStateCacheMissing = false;
            if (_floraState.SpeciesCount > 0) FloraStateDebugWindow.Open(_floraPotential, _floraState, _selectedFloraSpecies);
        });
    }

    private void RebindFloraState(Terrain terrain, GeographyAsset geography, FloraAsset flora)
    {
        RunBusy("Rebind living Flora", () =>
        {
            if (_floraStateAsset == null || _floraState == null || !_floraStateAsset.CanRebind(flora))
                throw new System.InvalidOperationException("Living Flora cannot safely rebind to the current physical world.");
            _floraState = FloraSimulationData.RebindPreservingBiomass(_floraState, _floraPotential);
            _floraStateAsset.StoreMetadata(_floraState, _floraSimulationSettings, flora);
            EditorUtility.SetDirty(_floraStateAsset);
            AssetDatabase.SaveAssets();
            FloraStateBinaryCache.Save(_floraStateAsset, _floraState);
            _floraStateRevision = _floraStateAsset.SimulationRevision;
            _floraStateCacheMissing = false;

            if (_faunaAsset != null && _faunaState != null && _faunaAsset.CanRebind(geography, flora))
            {
                _faunaAsset.StoreMetadata(_faunaState, _faunaSettings, geography, flora);
                EditorUtility.SetDirty(_faunaAsset);
                AssetDatabase.SaveAssets();
                FaunaBinaryCache.Save(_faunaAsset, _faunaState);
                _faunaRevision = _faunaAsset.SimulationRevision;
                _faunaCacheMissing = false;
            }
        });
    }

    private void StepFlora(float days)
    {
        RunBusy($"Flora +{days:0}d", () =>
        {
            FloraSimulator.Step(_floraPotential, _floraAsset.Settings, _floraState, _floraSimulationSettings, days);
            SaveFloraState();
            FloraStateDebugWindow.Open(_floraPotential, _floraState, _selectedFloraSpecies);
        });
    }

    private void DisturbFlora()
    {
        RunBusy("Biomass disturbance", () =>
        {
            float removed = FloraSimulator.RemoveFractionInCircle(
                _floraState,
                _selectedFloraSpecies,
                new Vector2(_disturbanceX, _disturbanceZ),
                _disturbanceRadius,
                _disturbanceFraction);
            SaveFloraState();
            UnityEngine.Debug.Log($"[Infinity Flora V1] Disturbance removed {removed:N1} kg from {_floraState.Species[_selectedFloraSpecies].DisplayName}.");
            FloraStateDebugWindow.Open(_floraPotential, _floraState, _selectedFloraSpecies);
        });
    }

    private void InitializeFauna(Terrain terrain, GeographyAsset geography, FloraAsset flora)
    {
        RunBusy("Initialize Fauna", () =>
        {
            DisposeFaunaEcsMovement();
            GeographyData geographyData = RequireGeographyData();
            _faunaSettings.Validate();
            _faunaState = FaunaSimulator.Initialize(geographyData, _floraPotential, _floraState, _faunaSettings);
            _faunaAsset = PersistFaunaAsset(terrain, geography, flora, _faunaState);
            FaunaBinaryCache.Save(_faunaAsset, _faunaState);
            _faunaRevision = _faunaAsset.SimulationRevision;
            _faunaCacheMissing = false;
            FaunaDebugWindow.Open(_floraPotential, _floraState, _faunaState, _faunaSettings);
        });
    }

    private void StepEcology(GeographyAsset geography, float days)
    {
        RunBusy($"Ecology +{days:0}d", () =>
        {
            GeographyData geographyData = RequireGeographyData();
            EcologySimulator.StepEcsMovement(
                geographyData,
                _floraPotential,
                _floraAsset.Settings,
                _floraState,
                _floraSimulationSettings,
                _faunaState,
                _faunaSettings,
                RequireFaunaEcsMovement(),
                days);
            SaveFloraState();
            SaveFaunaState(geography);
            FaunaDebugWindow.Open(_floraPotential, _floraState, _faunaState, _faunaSettings);
        });
    }

    private void SaveFloraState()
    {
        _floraStateAsset.StoreMetadata(_floraState, _floraSimulationSettings, _floraAsset);
        EditorUtility.SetDirty(_floraStateAsset);
        AssetDatabase.SaveAssets();
        FloraStateBinaryCache.Save(_floraStateAsset, _floraState);
        _floraStateRevision = _floraStateAsset.SimulationRevision;
        _floraStateCacheMissing = false;
    }

    private void SaveFaunaState(GeographyAsset geography)
    {
        _faunaAsset.StoreMetadata(_faunaState, _faunaSettings, geography, _floraAsset);
        EditorUtility.SetDirty(_faunaAsset);
        AssetDatabase.SaveAssets();
        FaunaBinaryCache.Save(_faunaAsset, _faunaState);
        _faunaRevision = _faunaAsset.SimulationRevision;
        _faunaCacheMissing = false;
    }

    private FloraStateAsset PersistFloraStateAsset(Terrain terrain, FloraAsset flora, FloraSimulationData data)
    {
        string folder = InfinityGeneratedPaths.Ensure("FloraState");
        FloraStateAsset asset = terrain.GetComponent<FloraStateReference>()?.FloraState;
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<FloraStateAsset>();
            AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{MakeSafeFileName(terrain.name)}_FloraState.asset"));
        }
        Undo.RecordObject(asset, "Store Infinity Living Flora Metadata");
        asset.StoreMetadata(data, _floraSimulationSettings, flora);
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        FloraStateReference reference = terrain.GetComponent<FloraStateReference>();
        if (reference == null) reference = Undo.AddComponent<FloraStateReference>(terrain.gameObject);
        Undo.RecordObject(reference, "Link Infinity Living Flora");
        reference.FloraState = asset;
        EditorUtility.SetDirty(reference);
        return asset;
    }

    private FaunaAsset PersistFaunaAsset(Terrain terrain, GeographyAsset geography, FloraAsset flora, FaunaSimulationData data)
    {
        string folder = InfinityGeneratedPaths.Ensure("Fauna");
        FaunaAsset asset = terrain.GetComponent<FaunaReference>()?.Fauna;
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<FaunaAsset>();
            AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{MakeSafeFileName(terrain.name)}_Fauna.asset"));
        }
        Undo.RecordObject(asset, "Store Infinity Fauna Metadata");
        asset.StoreMetadata(data, _faunaSettings, geography, flora);
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        FaunaReference reference = terrain.GetComponent<FaunaReference>();
        if (reference == null) reference = Undo.AddComponent<FaunaReference>(terrain.gameObject);
        Undo.RecordObject(reference, "Link Infinity Fauna");
        reference.Fauna = asset;
        EditorUtility.SetDirty(reference);
        return asset;
    }

    private void SyncFromTerrain(Terrain terrain, GeographyAsset geography, FloraAsset flora)
    {
        if (_cachedTerrain != terrain)
        {
            DisposeFaunaEcsMovement();
            _cachedTerrain = terrain;
            _geographyAsset = null;
            _geographyData = null;
            _geographyGenerationRevision = -1;
            _floraAsset = null;
            _floraPotential = null;
            _floraGenerationRevision = -1;
            _floraStateAsset = null;
            _floraState = null;
            _floraStateRevision = -1;
            _floraStateCacheMissing = false;
            _faunaAsset = null;
            _faunaState = null;
            _faunaRevision = -1;
            _faunaCacheMissing = false;
            _lastTiming = string.Empty;
            _disturbanceX = terrain.terrainData.size.x * 0.5f;
            _disturbanceZ = terrain.terrainData.size.z * 0.5f;
        }

        if (_geographyAsset != geography || _geographyGenerationRevision != geography.GenerationRevision)
        {
            _geographyAsset = geography;
            _geographyGenerationRevision = geography.GenerationRevision;
            _geographyData = WorldEditorDataCache.GetGeography(geography);
        }

        if (_floraAsset != flora || _floraGenerationRevision != flora.GenerationRevision)
        {
            _floraAsset = flora;
            _floraGenerationRevision = flora.GenerationRevision;
            _floraPotential = null;
            WorldEditorDataCache.TryGetFlora(flora, out _floraPotential);
        }

        FloraStateAsset stateAsset = terrain.GetComponent<FloraStateReference>()?.FloraState;
        if (_floraStateAsset != stateAsset || (stateAsset != null && _floraStateRevision != stateAsset.SimulationRevision))
        {
            _floraStateAsset = stateAsset;
            _floraStateRevision = stateAsset != null ? stateAsset.SimulationRevision : -1;
            _floraState = null;
            _floraStateCacheMissing = false;
            if (stateAsset != null && stateAsset.HasMetadata)
            {
                _floraSimulationSettings = stateAsset.Settings.Clone();
                _floraStateCacheMissing = !FloraStateBinaryCache.TryLoad(stateAsset, out _floraState);
            }
        }

        FaunaAsset faunaAsset = terrain.GetComponent<FaunaReference>()?.Fauna;
        if (_faunaAsset != faunaAsset || (faunaAsset != null && _faunaRevision != faunaAsset.SimulationRevision))
        {
            _faunaAsset = faunaAsset;
            _faunaRevision = faunaAsset != null ? faunaAsset.SimulationRevision : -1;
            _faunaState = null;
            _faunaCacheMissing = false;
            if (faunaAsset != null && faunaAsset.HasMetadata)
            {
                _faunaSettings = faunaAsset.Settings.Clone();
                _faunaCacheMissing = !FaunaBinaryCache.TryLoad(faunaAsset, out _faunaState);
            }
        }
    }


    private FaunaEcsMovementBackend RequireFaunaEcsMovement()
    {
        _faunaEcsMovement ??= new FaunaEcsMovementBackend();
        return _faunaEcsMovement;
    }

    private void DisposeFaunaEcsMovement()
    {
        _faunaEcsMovement?.Dispose();
        _faunaEcsMovement = null;
    }

    private GeographyData RequireGeographyData()
    {
        if (_geographyData == null)
            throw new System.InvalidOperationException("Geography data is unavailable. Regenerate Geography before running ecology.");
        return _geographyData;
    }

    private void RunBusy(string label, System.Action action)
    {
        _busy = true;
        var watch = Stopwatch.StartNew();
        try
        {
            action();
            watch.Stop();
            _lastTiming = $"{label}: {watch.Elapsed.TotalSeconds:0.00}s";
            SceneView.RepaintAll();
        }
        catch (System.Exception exception)
        {
            watch.Stop();
            UnityEngine.Debug.LogException(exception);
            _lastTiming = $"{label} failed after {watch.Elapsed.TotalSeconds:0.00}s";
        }
        finally { _busy = false; }
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
