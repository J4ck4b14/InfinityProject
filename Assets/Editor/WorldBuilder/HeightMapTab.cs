using UnityEngine;
using UnityEditor;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// Editor tab for multi‐tile, high‐performance procedural heightmap generation
/// with geographic features, seamless tile stitching via neighbor lookup,
/// and controllable ocean‐access edges.
///
/// Pipeline overview:
/// - Noise (Burst): fills a flat height buffer (index = y * res + x) with [0..1] heights.
/// - Main thread features: ridges, a simple river stamp, neighbor border stitching (Unity APIs).
/// - Hydraulic erosion (Burst): mass-conserving4-neighbor water flux + sediment capacity model.
/// - Thermal erosion (Burst): mass-conserving talus relaxation to reduce overly-steep slopes.
/// - Ocean/beach (Burst): optional edge fade toward `waterLevel`.
/// - Apply: convert to `float[,]` once and call `TerrainData.SetHeights`.
///
/// Notes on performance/realism:
/// - Hydraulic water transport is mass-conserving (explicit flux buffers).
/// - Sediment transport is approximated (no dedicated sediment flux buffers) to keep bandwidth low.
/// - Neighbor stitching uses `Terrain.SampleHeight` (main thread) and can be a hotspot with many tiles.
/// </summary>
public class HeightMapTab
{
    // ─────────────────────────────────────────────────────────────────────────────
    #region Inspector Fields

    [Header("Tile Grid Settings")]
    [Tooltip("Number of terrain tiles along the X axis")]
    private int tilesX = 2;
    [Tooltip("Number of terrain tiles along the Z axis")]
    private int tilesY = 2;
    [Tooltip("Index of the current tile along X (0-based)")]
    private int tileX = 0;
    [Tooltip("Index of the current tile along Z (0-based)")]
    private int tileY = 0;

    [Header("Heightmap Resolution & Seed")]
    [Tooltip("Number of noise samples per side of each tile")]
    private int[] mapSizes = { 128, 256, 512, 1024 };
    [Tooltip("Index into mapSizes array")]
    private int mapIndex = 1;
    [Tooltip("Global seed for seamless noise across tiles")]
    private float globalSeed = 1234f;

    [Header("Fractal Noise Parameters")]
    [Tooltip("Base frequency of the simplex noise")]
    private float baseRoughness = 1f;
    [Tooltip("Frequency multiplier each octave")]
    private float roughness = 2f;
    [Tooltip("Amplitude multiplier each octave")]
    private float persistence = 0.5f;
    [Tooltip("Number of octaves to sum")]
    private int octaves = 6;

    [Header("Geographic Features")]
    [Tooltip("Enable sharp ridges and cliffs")]
    private bool useRidges = true;
    [Tooltip("Ridge strength multiplier")]
    private float ridgeStrength = 2f;

    [Header("Hydraulic Erosion (Burst, Flux-based)")]
    [Tooltip("Enable hydraulic erosion (wet planet)")]
    private bool doHydraulicErode = true;
    [Tooltip("Hydraulic iterations (100-250 typical at 1024)")]
    private int hydraulicIterations = 160;
    [Tooltip("Rain added per iteration (water units)")]
    private float rainRate = 0.010f;
    [Tooltip("Fraction of water removed per iteration")]
    private float evaporation = 0.02f;
    [Tooltip("Flow factor (higher moves water faster; too high can destabilize)")]
    private float flowRate = 0.7f;
    [Tooltip("Sediment capacity coefficient")]
    private float sedimentCapacity = 4.0f;
    [Tooltip("Erode speed")]
    private float erodeSpeed = 0.35f;
    [Tooltip("Deposit speed")]
    private float depositSpeed = 0.35f;
    [Tooltip("Minimum slope used for capacity")]
    private float minSlope = 0.0005f;

    /*
     * Erosion parameter tuning cheatsheet (visual results depend on base noise scale + iterations):
     *
     * Hydraulic iterations:
     * - More iterations => deeper, more connected channels, more time to transport sediment.
     * - First knob to reduce if generation is too slow.
     *
     * Rain rate:
     * - Higher => more widespread erosion and larger river basins.
     * - Too high can wash out details unless evaporation also increases.
     *
     * Evaporation:
     * - Higher => less standing water, shorter streams, less overall erosion.
     * - Lower => water accumulates in basins; can create large carved drainage if iterations are high.
     *
     * Flow rate:
     * - Higher => water moves faster and can carve sharper gullies.
     * - Too high can introduce instability/over-erosion on steep terrain.
     *
     * Sediment capacity:
     * - Higher => water can carry more material => more erosion and drifting sediment.
     * - Lower => more deposition (alluvial fans/terraces) and gentler erosion.
     *
     * Erode vs deposit speed:
     * - Higher erodeSpeed => digs channels faster.
     * - Higher depositSpeed => fills valleys faster, smooths and builds up deltas.
     * - Keeping them roughly balanced is a good default; bias depending on style.
     *
     * Min slope:
     * - Prevents capacity from going to ~0 on flats.
     * - Too high can cause erosion/deposition even on nearly-flat areas (muddy look).
     *
     * Thermal passes / talus angle:
     * - Thermal is best as a finishing step: reduces spikes and overly-steep cliffs.
     * - More passes or lower talus => more smoothing/relaxation.
     */

    [Header("Thermal Erosion (Burst)")]
    [Tooltip("Enable thermal erosion")]
    private bool doThermalErode = true;
    [Tooltip("Thermal erosion passes")]
    private int erosionPasses = 20;
    [Tooltip("Talus slope threshold")]
    private float talusAngle = 0.02f;

    [Header("Rivers")]
    [Tooltip("Minimum water level for rivers")]
    private float waterLevel = 0.3f;

    [Header("Neighbor Terrains (Optional)")]
    [Tooltip("Drop your already‐placed neighboring Terrain tiles here")]
    [SerializeField] private Terrain neighborNW, neighborN, neighborNE;
    [SerializeField] private Terrain neighborW, neighborE;
    [SerializeField] private Terrain neighborSW, neighborS, neighborSE;

    [Tooltip("Which edges of this tile should gently slope into water")]
    [System.Flags]
    private enum OceanSides { None = 0, North = 1 << 0, South = 1 << 1, East = 1 << 2, West = 1 << 3, All = North | South | East | West }

    [Header("Ocean / Beach Access")]
    [Tooltip("Edges to carve toward water level")]
    private OceanSides oceanSides = OceanSides.None;
    [Tooltip("Fade distance inland (samples)")]
    private int beachFadeDistance = 8;

    [Header("Terrain Settings")]
    [Tooltip("Tile size in world units (X,Z)")]
    private float terrainSide = 1000f;
    [Tooltip("Maximum height (Y) in world units")]
    private float terrainHeight = 100f;
    [Tooltip("Target Terrain to apply heights to")]
    private Terrain targetTerrain;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────────
    #region Native Buffers / Job State

    /// <summary>
    /// Main height buffer for the tile.
    /// Flat layout: index = y * res + x, values are normalized [0..1].
    /// </summary>
    private NativeArray<float> _heights;

    /// <summary>
    /// Scratch buffer used for any step requiring double-buffering (write output separately).
    /// Avoids race conditions and keeps jobs parallel-safe.
    /// </summary>
    private NativeArray<float> _scratch;

    // Hydraulic state (double-buffered so each iteration can be parallelized safely)
    // - water: amount of water on each cell (arbitrary units)
    // - sediment: transported material carried by the water (arbitrary units)
    private NativeArray<float> _waterA, _waterB;
    private NativeArray<float> _sedA, _sedB;

    // Hydraulic outgoing water flux per cell toward each neighbor (E/W/N/S).
    // Computed in one job (read-only inputs), applied in a second job (mass-conserving update).
    private NativeArray<float> _fluxE, _fluxW, _fluxN, _fluxS;

    // Thermal erosion outgoing "material flow" per cell (E/W/N/S). Same2-phase pattern as hydraulic.
    private NativeArray<float> _outE, _outW, _outN, _outS;

    /// <summary>
    /// Handle representing the currently scheduled stage of the pipeline.
    /// The editor polls this to keep the UI responsive.
    /// </summary>
    private JobHandle _pipelineHandle;

    /// <summary>
    /// True when the pipeline is active (jobs scheduled, editor polling enabled).
    /// </summary>
    private bool _isJobScheduled;

    /// <summary>
    /// Resolution for the currently running pipeline. Needed because we release control to editor update.
    /// </summary>
    private int _pendingRes;

    /// <summary>
    /// Cancellation requested from UI.
    /// Jobs also check a shared native flag for early-out.
    /// </summary>
    private bool _cancelRequested;

    /// <summary>
    /// Cancellation flag passed into jobs:0 = run,1 = cancel.
    /// Jobs check it and return early to reduce wasted work.
    /// </summary>
    private NativeArray<int> _jobCancelFlag;

    // Simple pipeline state machine.
    private enum Phase { None, Noise, Hydraulic, Thermal, Ocean, Apply }
    private Phase _phase = Phase.None;

    // Iteration counters used for progress display.
    private int _hydroIter;
    private int _thermalIter;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────────
    #region Favorites

    private const int MaxFavorites = 5;
    private List<SavedHeightmap> favorites = new List<SavedHeightmap>(MaxFavorites);
    private int selectedFavoriteIndex = -1;
    private const string FavoriteFolder = "Assets/WorldBuilderSaved";

    #endregion

    // ─────────────────────────────────────────────────────────────────────────────
    #region UI

    public HeightMapTab() => LoadFavorites();

    public void Draw()
    {
        EditorGUILayout.LabelField("Tile Grid Settings", EditorStyles.boldLabel);
        tilesX = EditorGUILayout.IntField("Tiles X", tilesX);
        tilesY = EditorGUILayout.IntField("Tiles Y", tilesY);
        EditorGUILayout.BeginHorizontal();
        tileX = EditorGUILayout.IntSlider("Tile X", tileX, 0, Mathf.Max(0, tilesX - 1));
        tileY = EditorGUILayout.IntSlider("Tile Z", tileY, 0, Mathf.Max(0, tilesY - 1));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Heightmap Resolution & Seed", EditorStyles.boldLabel);
        mapIndex = EditorGUILayout.Popup("Samples Per Side", mapIndex, System.Array.ConvertAll(mapSizes, s => s.ToString()));
        globalSeed = EditorGUILayout.FloatField("Global Seed", globalSeed);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Fractal Noise Parameters", EditorStyles.boldLabel);
        baseRoughness = EditorGUILayout.Slider("Base Roughness", baseRoughness, 0.1f, 5f);
        roughness = EditorGUILayout.Slider("Roughness", roughness, 1f, 10f);
        persistence = EditorGUILayout.Slider("Persistence", persistence, 0f, 1f);
        octaves = EditorGUILayout.IntSlider("Octaves", octaves, 1, 8);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Geographic Features", EditorStyles.boldLabel);

        useRidges = EditorGUILayout.Toggle("Enable Ridges/Cliffs", useRidges);
        if (useRidges)
            ridgeStrength = EditorGUILayout.Slider("Ridge Strength", ridgeStrength, 1f, 5f);

        doHydraulicErode = EditorGUILayout.Toggle("Enable Hydraulic Erosion (Wet)", doHydraulicErode);
        if (doHydraulicErode)
        {
            hydraulicIterations = EditorGUILayout.IntSlider("Hydraulic Iterations", hydraulicIterations, 20, 400);
            rainRate = EditorGUILayout.Slider("Rain Rate", rainRate, 0f, 0.05f);
            evaporation = EditorGUILayout.Slider("Evaporation", evaporation, 0f, 0.1f);
            flowRate = EditorGUILayout.Slider("Flow Rate", flowRate, 0.05f, 1.0f);
            sedimentCapacity = EditorGUILayout.Slider("Sediment Capacity", sedimentCapacity, 0.1f, 12f);
            erodeSpeed = EditorGUILayout.Slider("Erode Speed", erodeSpeed, 0f, 1f);
            depositSpeed = EditorGUILayout.Slider("Deposit Speed", depositSpeed, 0f, 1f);
            minSlope = EditorGUILayout.Slider("Min Slope", minSlope, 0f, 0.02f);
        }

        doThermalErode = EditorGUILayout.Toggle("Enable Thermal Erosion", doThermalErode);
        if (doThermalErode)
        {
            erosionPasses = EditorGUILayout.IntSlider("Thermal Passes", erosionPasses, 1, 120);
            talusAngle = EditorGUILayout.Slider("Talus Angle", talusAngle, 0.001f, 0.1f);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Neighbor Terrains (Optional)", EditorStyles.boldLabel);
        neighborNW = (Terrain)EditorGUILayout.ObjectField("North-West", neighborNW, typeof(Terrain), true);
        neighborN = (Terrain)EditorGUILayout.ObjectField("North", neighborN, typeof(Terrain), true);
        neighborNE = (Terrain)EditorGUILayout.ObjectField("North-East", neighborNE, typeof(Terrain), true);
        neighborW = (Terrain)EditorGUILayout.ObjectField("West", neighborW, typeof(Terrain), true);
        neighborE = (Terrain)EditorGUILayout.ObjectField("East", neighborE, typeof(Terrain), true);
        neighborSW = (Terrain)EditorGUILayout.ObjectField("South-West", neighborSW, typeof(Terrain), true);
        neighborS = (Terrain)EditorGUILayout.ObjectField("South", neighborS, typeof(Terrain), true);
        neighborSE = (Terrain)EditorGUILayout.ObjectField("South-East", neighborSE, typeof(Terrain), true);

        if (GUILayout.Button("Autofill From Selected Terrain"))
            AutofillFromSelectedTerrain();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Ocean / Beach Access", EditorStyles.boldLabel);
        oceanSides = (OceanSides)EditorGUILayout.EnumFlagsField("Edges with Ocean", oceanSides);
        if (oceanSides != OceanSides.None)
            beachFadeDistance = EditorGUILayout.IntSlider("Beach Fade Distance", beachFadeDistance, 0, mapSizes[mapIndex] / 2);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Terrain Settings", EditorStyles.boldLabel);
        terrainSide = EditorGUILayout.FloatField("Tile Size (X,Z)", terrainSide);
        terrainHeight = EditorGUILayout.FloatField("Max Height (Y)", terrainHeight);
        targetTerrain = (Terrain)EditorGUILayout.ObjectField("Target Terrain", targetTerrain, typeof(Terrain), true);

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();

        using (new EditorGUI.DisabledScope(_isJobScheduled))
        {
            if (GUILayout.Button("Generate Tile (Burst Erosion)"))
            {
                _cancelRequested = false;
                GenerateTileFast();
            }
        }

        using (new EditorGUI.DisabledScope(!_isJobScheduled))
        {
            if (GUILayout.Button("Cancel"))
                RequestCancel();
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        DrawFavoritesUI();
    }

    private void AutofillFromSelectedTerrain()
    {
        var go = Selection.activeGameObject;
        if (go == null)
        {
            EditorUtility.DisplayDialog("Autofill Failed", "No GameObject selected. Select a Terrain in the Hierarchy.", "OK");
            return;
        }

        var t = go.GetComponent<Terrain>();
        if (t == null || t.terrainData == null)
        {
            EditorUtility.DisplayDialog("Autofill Failed", "Selected GameObject is not a Terrain with TerrainData.", "OK");
            return;
        }

        targetTerrain = t;
        terrainSide = t.terrainData.size.x;
        terrainHeight = t.terrainData.size.y;

        float sizeX = t.terrainData.size.x;
        float sizeZ = t.terrainData.size.z;
        Vector3 origin = t.transform.position;

        neighborN = FindNeighborAtOffset(origin, 0, 1, sizeX, sizeZ);
        neighborS = FindNeighborAtOffset(origin, 0, -1, sizeX, sizeZ);
        neighborE = FindNeighborAtOffset(origin, 1, 0, sizeX, sizeZ);
        neighborW = FindNeighborAtOffset(origin, -1, 0, sizeX, sizeZ);
        neighborNE = FindNeighborAtOffset(origin, 1, 1, sizeX, sizeZ);
        neighborNW = FindNeighborAtOffset(origin, -1, 1, sizeX, sizeZ);
        neighborSE = FindNeighborAtOffset(origin, 1, -1, sizeX, sizeZ);
        neighborSW = FindNeighborAtOffset(origin, -1, -1, sizeX, sizeZ);

        EditorUtility.SetDirty((Object)targetTerrain);
        SceneView.RepaintAll();
    }

    private Terrain FindNeighborAtOffset(Vector3 origin, int dx, int dz, float sizeX, float sizeZ)
    {
        Vector3 expected = origin + new Vector3(dx * sizeX, 0f, dz * sizeZ);
        foreach (var t in Terrain.activeTerrains)
        {
            if (Vector3.Distance(t.transform.position, expected) < 1e-2f)
                return t;
        }
        return null;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────────
    #region Generation Pipeline

    /// <summary>
    /// Starts generation: allocates/reuses buffers, clears hydraulic state, and schedules the initial noise job.
    /// Subsequent steps are scheduled by <see cref="PollPipeline"/>.
    /// </summary>
    private void GenerateTileFast()
    {
        if (targetTerrain == null)
        {
            if (EditorUtility.DisplayDialog("No Terrain Selected", "Create a new Terrain GameObject?", "Yes", "Cancel"))
                CreateNewTerrain();
            else
                return;
        }

        int res = mapSizes[mapIndex];
        int total = res * res;

        EnsureNative(ref _heights, total);
        EnsureNative(ref _scratch, total);

        EnsureNative(ref _waterA, total);
        EnsureNative(ref _waterB, total);
        EnsureNative(ref _sedA, total);
        EnsureNative(ref _sedB, total);

        EnsureNative(ref _fluxE, total);
        EnsureNative(ref _fluxW, total);
        EnsureNative(ref _fluxN, total);
        EnsureNative(ref _fluxS, total);

        EnsureNative(ref _outE, total);
        EnsureNative(ref _outW, total);
        EnsureNative(ref _outN, total);
        EnsureNative(ref _outS, total);

        EnsureCancelFlag();
        _jobCancelFlag[0] = 0;

        // Clear hydraulic state
        _pipelineHandle = new ClearJob { data = _waterA, cancelFlag = _jobCancelFlag }.Schedule(total,256);
        _pipelineHandle = new ClearJob { data = _waterB, cancelFlag = _jobCancelFlag }.Schedule(total,256, _pipelineHandle);
        _pipelineHandle = new ClearJob { data = _sedA, cancelFlag = _jobCancelFlag }.Schedule(total,256, _pipelineHandle);
        _pipelineHandle = new ClearJob { data = _sedB, cancelFlag = _jobCancelFlag }.Schedule(total,256, _pipelineHandle);

        var noise = new HeightmapJob
        {
            width = res,
            height = res,
            baseRoughness = baseRoughness,
            roughness = roughness,
            persistence = persistence,
            octaves = octaves,
            offset = new float2(globalSeed + tileX * res, globalSeed + tileY * res),
            heights = _heights,
            cancelFlag = _jobCancelFlag
        };

        // Base noise is the first heavy stage. It runs in Burst and fills `_heights`.
        _pipelineHandle = noise.Schedule(total,64, _pipelineHandle);

        // We don't block here; we poll completion in EditorApplication.update.
        _phase = Phase.Noise;
        _isJobScheduled = true;
        _pendingRes = res;
        _cancelRequested = false;

        _hydroIter =0;
        _thermalIter =0;

        EditorApplication.update -= PollPipeline;
        EditorApplication.update += PollPipeline;
    }

    /// <summary>
    /// Signals cancellation. Burst jobs will observe <see cref="_jobCancelFlag"/> and return early.
    /// </summary>
    private void RequestCancel()
    {
        _cancelRequested = true;
        if (_jobCancelFlag.IsCreated)
            _jobCancelFlag[0] = 1;
    }

    /// <summary>
    /// Editor update callback.
    /// Keeps the editor responsive by:
    /// - returning while jobs are running
    /// - completing jobs only when done
    /// - scheduling the next pipeline stage based on <see cref="_phase"/>
    ///
    /// Note: some steps must run on the main thread because they touch Unity APIs:
    /// - river carving uses UnityEngine.Random
    /// - neighbor stitching uses Terrain.SampleHeight
    /// </summary>
    private void PollPipeline()
    {
        if (!_isJobScheduled) return;

        if (_cancelRequested)
        {
            _pipelineHandle.Complete();
            FinishPipeline("Heightmap generation cancelled.");
            return;
        }

        if (!_pipelineHandle.IsCompleted)
        {
            DrawPipelineProgress();
            return;
        }

        _pipelineHandle.Complete();

        int res = _pendingRes;
        int total = res * res;

        switch (_phase)
        {
            case Phase.Noise:
            {
                // CPU light operations
                EditorUtility.DisplayProgressBar("Generating Heightmap", "Applying ridges...", 0.2f);
                if (useRidges) ApplyRidgeNoise(_heights, ridgeStrength);

                EditorUtility.DisplayProgressBar("Generating Heightmap", "Carving rivers...", 0.28f);
                CarveRiver(res, _heights);

                EditorUtility.DisplayProgressBar("Generating Heightmap", "Blending with neighbors...", 0.35f);
                BlendWithNeighbors(res, _heights);

                if (doHydraulicErode && hydraulicIterations > 0)
                {
                    ScheduleHydraulicIteration(res, total);
                    return;
                }

                if (doThermalErode && erosionPasses > 0)
                {
                    ScheduleThermalPass(res, total);
                    return;
                }

                ScheduleOceanOrApply(res, total);
                return;
            }

            case Phase.Hydraulic:
            {
                _hydroIter++;
                if (_hydroIter < hydraulicIterations)
                {
                    ScheduleHydraulicIteration(res, total);
                    return;
                }

                if (doThermalErode && erosionPasses > 0)
                {
                    ScheduleThermalPass(res, total);
                    return;
                }

                ScheduleOceanOrApply(res, total);
                return;
            }

            case Phase.Thermal:
            {
                _thermalIter++;
                if (_thermalIter < erosionPasses)
                {
                    ScheduleThermalPass(res, total);
                    return;
                }

                ScheduleOceanOrApply(res, total);
                return;
            }

            case Phase.Ocean:
            {
                ApplyAndFinish(res);
                return;
            }
        }
    }

    private void DrawPipelineProgress()
    {
        float p = 0.1f;
        string msg = "Working...";

        switch (_phase)
        {
            case Phase.Noise:
                p = 0.1f;
                msg = "Computing noise (Burst)...";
                break;

            case Phase.Hydraulic:
                p = 0.40f + 0.40f * (_hydroIter / Mathf.Max(1f, hydraulicIterations));
                msg = $"Hydraulic erosion (Burst flux) {_hydroIter + 1}/{Mathf.Max(1, hydraulicIterations)}...";
                break;

            case Phase.Thermal:
                p = 0.82f + 0.10f * (_thermalIter / Mathf.Max(1f, erosionPasses));
                msg = $"Thermal erosion (Burst) {_thermalIter + 1}/{Mathf.Max(1, erosionPasses)}...";
                break;

            case Phase.Ocean:
                p = 0.95f;
                msg = "Ocean/beach carving (Burst)...";
                break;
        }

        EditorUtility.DisplayProgressBar("Generating Heightmap", msg, p);
    }

    /// <summary>
    /// Schedules one hydraulic erosion iteration:
    ///1) Rain+evap modifies water.
    ///2) Compute outgoing water fluxes.
    ///3) Apply fluxes (mass-conserving) and do capacity-based erosion/deposition.
    /// After scheduling, buffers are swapped (double-buffering).
    /// </summary>
    private void ScheduleHydraulicIteration(int res, int total)
    {
        // Rain + evaporation
        var rain = new RainEvapJob
        {
            rainRate = rainRate,
            evaporation = evaporation,
            water = _waterA,
            cancelFlag = _jobCancelFlag
        }.Schedule(total, 256);

        // Compute outgoing fluxes (mass conserving)
        var flux = new ComputeFluxJob
        {
            res = res,
            flowRate = flowRate,
            heights = _heights,
            water = _waterA,
            fluxE = _fluxE,
            fluxW = _fluxW,
            fluxN = _fluxN,
            fluxS = _fluxS,
            cancelFlag = _jobCancelFlag
        }.Schedule(total, 128, rain);

        // Apply fluxes to update water + perform erosion/deposition into scratch buffers
        var apply = new ApplyFluxAndErodeJob
        {
            res = res,
            minSlope = minSlope,
            capacityK = sedimentCapacity,
            erodeSpeed = erodeSpeed,
            depositSpeed = depositSpeed,

            heightsIn = _heights,
            heightsOut = _scratch,

            waterIn = _waterA,
            waterOut = _waterB,

            sedimentIn = _sedA,
            sedimentOut = _sedB,

            fluxE = _fluxE,
            fluxW = _fluxW,
            fluxN = _fluxN,
            fluxS = _fluxS,

            cancelFlag = _jobCancelFlag
        }.Schedule(total, 128, flux);

        _pipelineHandle = apply;
        _phase = Phase.Hydraulic;

        Swap(ref _heights, ref _scratch);
        Swap(ref _waterA, ref _waterB);
        Swap(ref _sedA, ref _sedB);
    }

    /// <summary>
    /// Schedules one thermal erosion pass (talus relaxation):
    /// - outflow stage computes how much material leaves toward lower neighbors
    /// - apply stage conserves material via inSum/outSum update
    /// </summary>
    private void ScheduleThermalPass(int res, int total)
    {
        var outflow = new ThermalOutflowJob
        {
            res = res,
            talus = talusAngle,
            heights = _heights,
            outE = _outE,
            outW = _outW,
            outN = _outN,
            outS = _outS,
            cancelFlag = _jobCancelFlag
        }.Schedule(total, 128);

        var apply = new ThermalApplyJob
        {
            res = res,
            heightsIn = _heights,
            heightsOut = _scratch,
            outE = _outE,
            outW = _outW,
            outN = _outN,
            outS = _outS,
            cancelFlag = _jobCancelFlag
        }.Schedule(total, 128, outflow);

        _pipelineHandle = apply;
        _phase = Phase.Thermal;

        Swap(ref _heights, ref _scratch);
    }

    private void ScheduleOceanOrApply(int res, int total)
    {
        if (oceanSides == OceanSides.None || beachFadeDistance <= 0)
        {
            ApplyAndFinish(res);
            return;
        }

        var ocean = new OceanAccessJob
        {
            res = res,
            waterH = waterLevel,
            sidesMask = (int)oceanSides,
            fadeDist = beachFadeDistance,
            heights = _heights,
            cancelFlag = _jobCancelFlag
        };

        _pipelineHandle = ocean.Schedule(total, 256);
        _phase = Phase.Ocean;
    }

    private void ApplyAndFinish(int res)
    {
        EditorUtility.DisplayProgressBar("Generating Heightmap", "Applying to Terrain...", 0.99f);
        ApplyBufferToTerrainFlat(_heights, res);
        FinishPipeline(null);
    }

    private void FinishPipeline(string logMessage)
    {
        _isJobScheduled = false;
        _pendingRes = 0;
        _phase = Phase.None;

        EditorApplication.update -= PollPipeline;
        EditorUtility.ClearProgressBar();

        if (_jobCancelFlag.IsCreated)
            _jobCancelFlag[0] = 0;

        if (!string.IsNullOrEmpty(logMessage))
            Debug.Log(logMessage);

        SceneView.RepaintAll();
    }

    private void CreateNewTerrain()
    {
        var go = new GameObject($"Tile_{tileX}_{tileY}");
        go.transform.position = new Vector3(tileX * terrainSide, 0, tileY * terrainSide);
        targetTerrain = go.AddComponent<Terrain>();
        targetTerrain.terrainData = new TerrainData();
        var col = go.AddComponent<TerrainCollider>();
        col.terrainData = targetTerrain.terrainData;
        Undo.RegisterCreatedObjectUndo(go, "Create Terrain Tile");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────────
    #region Apply to Terrain (convert once)

    /// <summary>
    /// Applies the height buffer to the target terrain.
    /// Unity requires a (res+1)x(res+1) height array. We pad the last row/column
    /// by repeating the border values.
    /// </summary>
    private void ApplyBufferToTerrainFlat(NativeArray<float> buf, int res)
    {
        Undo.RecordObject(targetTerrain.terrainData, "Apply Heights");
        var td = targetTerrain.terrainData;

        int hmRes = res + 1;
        td.heightmapResolution = hmRes;
        td.size = new Vector3(terrainSide, terrainHeight, terrainSide);

        var arr = new float[hmRes, hmRes];

        for (int y = 0; y < res; y++)
        {
            int row = y * res;
            for (int x = 0; x < res; x++)
                arr[y, x] = buf[row + x];

            arr[y, res] = arr[y, res - 1];
        }

        for (int x = 0; x < hmRes; x++)
            arr[res, x] = arr[res - 1, x];

        td.SetHeights(0, 0, arr);
        EditorUtility.SetDirty(td);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────────
    #region Neighbor Blending (unchanged)

    /// <summary>
    /// Copies border heights from already-placed neighbor terrains so edges match seamlessly.
    /// This uses Terrain.SampleHeight (Unity API) to handle differing heightmap resolutions,
    /// but it must run on the main thread and can be a performance hotspot.
    /// </summary>
    private void BlendWithNeighbors(int res, NativeArray<float> buf)
    {
        float SampleNeighborNormalizedHeight(Terrain neigh, float u, float v)
        {
            if (neigh == null) return 0f;
            var nd = neigh.terrainData;
            Vector3 worldPos = neigh.transform.position + new Vector3(u * nd.size.x, 0f, v * nd.size.z);
            float worldH = neigh.SampleHeight(worldPos);
            float normalized = nd.size.y > 0f ? worldH / nd.size.y : 0f;
            return Mathf.Clamp01(normalized);
        }

        if (neighborN != null)
            for (int x = 0; x < res; x++)
                buf[(res - 1) * res + x] = SampleNeighborNormalizedHeight(neighborN, (res == 1) ? 0f : (float)x / (res - 1), 0f);

        if (neighborS != null)
            for (int x = 0; x < res; x++)
                buf[x] = SampleNeighborNormalizedHeight(neighborS, (res == 1) ? 0f : (float)x / (res - 1), 1f);

        if (neighborE != null)
            for (int y = 0; y < res; y++)
                buf[y * res + (res - 1)] = SampleNeighborNormalizedHeight(neighborE, 0f, (res == 1) ? 0f : (float)y / (res - 1));

        if (neighborW != null)
            for (int y = 0; y < res; y++)
                buf[y * res] = SampleNeighborNormalizedHeight(neighborW, 1f, (res == 1) ? 0f : (float)y / (res - 1));

        if (neighborNE != null) buf[(res - 1) * res + (res - 1)] = SampleNeighborNormalizedHeight(neighborNE, 0f, 0f);
        if (neighborNW != null) buf[(res - 1) * res + 0] = SampleNeighborNormalizedHeight(neighborNW, 1f, 0f);
        if (neighborSE != null) buf[(res - 1)] = SampleNeighborNormalizedHeight(neighborSE, 0f, 1f);
        if (neighborSW != null) buf[0] = SampleNeighborNormalizedHeight(neighborSW, 1f, 1f);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────────
    #region CPU Light Features

    private void ApplyRidgeNoise(NativeArray<float> buf, float strength)
    {
        for (int i = 0; i < buf.Length; i++)
        {
            float v = buf[i];
            buf[i] = math.abs(1f - 2f * v) * v * strength;
        }
    }

    private void CarveRiver(int res, NativeArray<float> buf)
    {
        int x = UnityEngine.Random.Range(0, res), y = 0;
        for (int s = 0; s < res * 2; s++)
        {
            int idx = y * res + x;
            buf[idx] = waterLevel;

            float best = buf[idx];
            int2 dir = int2.zero;

            Consider(1, 0);
            Consider(-1, 0);
            Consider(0, 1);
            Consider(0, -1);

            x += dir.x; y += dir.y;

            void Consider(int dx, int dy)
            {
                int nx = math.clamp(x + dx, 0, res - 1);
                int ny = math.clamp(y + dy, 0, res - 1);
                float v = buf[ny * res + nx];
                if (v < best) { best = v; dir = new int2(dx, dy); }
            }
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────────
    #region Burst Jobs (Hydraulic Flux + Thermal + Ocean)

    [BurstCompile]
    private struct ClearJob : IJobParallelFor
    {
        public NativeArray<float> data;
        [ReadOnly] public NativeArray<int> cancelFlag;

        public void Execute(int index)
        {
            // cooperative cancellation
            if (cancelFlag.IsCreated && cancelFlag[0] !=0) return;
            data[index] =0f;
        }
    }

    [BurstCompile]
    private struct RainEvapJob : IJobParallelFor
    {
        public float rainRate;
        public float evaporation;
        public NativeArray<float> water;
        [ReadOnly] public NativeArray<int> cancelFlag;

        public void Execute(int index)
        {
            if (cancelFlag.IsCreated && cancelFlag[0] != 0) return;
            float w = (water[index] + rainRate) * (1f - evaporation);
            water[index] = math.max(0f, w);
        }
    }

    [BurstCompile]
    private struct ComputeFluxJob : IJobParallelFor
    {
        public int res;
        public float flowRate; // 0..1-ish

        [ReadOnly] public NativeArray<float> heights;
        [ReadOnly] public NativeArray<float> water;

        [WriteOnly] public NativeArray<float> fluxE;
        [WriteOnly] public NativeArray<float> fluxW;
        [WriteOnly] public NativeArray<float> fluxN;
        [WriteOnly] public NativeArray<float> fluxS;

        [ReadOnly] public NativeArray<int> cancelFlag;

        public void Execute(int idx)
        {
            if (cancelFlag.IsCreated && cancelFlag[0] != 0) return;

            int x = idx % res;
            int y = idx / res;

            if (x == 0 || y == 0 || x == res - 1 || y == res - 1)
            {
                fluxE[idx] = 0f; fluxW[idx] = 0f; fluxN[idx] = 0f; fluxS[idx] = 0f;
                return;
            }

            float w = water[idx];
            if (w <= 0f)
            {
                fluxE[idx] = 0f; fluxW[idx] = 0f; fluxN[idx] = 0f; fluxS[idx] = 0f;
                return;
            }

            float surf = heights[idx] + w;

            float dE = math.max(0f, surf - (heights[idx + 1] + water[idx + 1]));
            float dW = math.max(0f, surf - (heights[idx - 1] + water[idx - 1]));
            float dN = math.max(0f, surf - (heights[idx + res] + water[idx + res]));
            float dS = math.max(0f, surf - (heights[idx - res] + water[idx - res]));

            float sum = dE + dW + dN + dS;
            if (sum <= 1e-6f)
            {
                fluxE[idx] = 0f; fluxW[idx] = 0f; fluxN[idx] = 0f; fluxS[idx] = 0f;
                return;
            }

            // Move some water proportional to downhill differences
            float move = w * flowRate;
            float fE = move * (dE / sum);
            float fW = move * (dW / sum);
            float fN = move * (dN / sum);
            float fS = move * (dS / sum);

            float outSum = fE + fW + fN + fS;
            if (outSum > w)
            {
                float scale = w / math.max(outSum, 1e-6f);
                fE *= scale; fW *= scale; fN *= scale; fS *= scale;
            }

            fluxE[idx] = fE;
            fluxW[idx] = fW;
            fluxN[idx] = fN;
            fluxS[idx] = fS;
        }
    }

    [BurstCompile]
    private struct ApplyFluxAndErodeJob : IJobParallelFor
    {
        public int res;

        public float minSlope;
        public float capacityK;
        public float erodeSpeed;
        public float depositSpeed;

        [ReadOnly] public NativeArray<float> heightsIn;
        [WriteOnly] public NativeArray<float> heightsOut;

        [ReadOnly] public NativeArray<float> waterIn;
        [WriteOnly] public NativeArray<float> waterOut;

        [ReadOnly] public NativeArray<float> sedimentIn;
        [WriteOnly] public NativeArray<float> sedimentOut;

        [ReadOnly] public NativeArray<float> fluxE;
        [ReadOnly] public NativeArray<float> fluxW;
        [ReadOnly] public NativeArray<float> fluxN;
        [ReadOnly] public NativeArray<float> fluxS;

        [ReadOnly] public NativeArray<int> cancelFlag;

        public void Execute(int idx)
        {
            if (cancelFlag.IsCreated && cancelFlag[0] != 0) return;

            int x = idx % res;
            int y = idx / res;

            if (x == 0 || y == 0 || x == res - 1 || y == res - 1)
            {
                heightsOut[idx] = heightsIn[idx];
                waterOut[idx] = waterIn[idx];
                sedimentOut[idx] = sedimentIn[idx];
                return;
            }

            float h = heightsIn[idx];
            float w = waterIn[idx];
            float s = sedimentIn[idx];

            float outSum = fluxE[idx] + fluxW[idx] + fluxN[idx] + fluxS[idx];

            // Inflow from neighbors toward this cell
            float inSum =
                fluxE[idx - 1] +       // west neighbor -> east
                fluxW[idx + 1] +       // east neighbor -> west
                fluxN[idx - res] +     // south neighbor -> north
                fluxS[idx + res];      // north neighbor -> south

            float wNew = math.max(0f, w - outSum + inSum);

            // Advect sediment proportional to water moved:
            // Keep it simple: move sediment with the same fraction as water outflow.
            float fracOut = (w > 1e-6f) ? math.saturate(outSum / w) : 0f;
            float sOut = s * fracOut;
            float sRemain = s - sOut;

            // In-sediment from neighbors (approx: their outgoing sediment share directed to us).
            // We don't have sediment flux buffers; approximate by moving a share proportional to neighbor water flux into us.
            // This is a pragmatic balance: realistic enough, still fast.
            float sIn =
                SedInFromNeighbor(idx - 1, fluxE[idx - 1], waterIn[idx - 1]) +
                SedInFromNeighbor(idx + 1, fluxW[idx + 1], waterIn[idx + 1]) +
                SedInFromNeighbor(idx - res, fluxN[idx - res], waterIn[idx - res]) +
                SedInFromNeighbor(idx + res, fluxS[idx + res], waterIn[idx + res]);

            float sNew = math.max(0f, sRemain + sIn);

            // Capacity based on slope + water amount
            // Use local water surface diffs approximated by outgoing flux magnitude.
            float slope = math.max(minSlope, outSum);
            float cap = capacityK * wNew * slope;

            if (sNew > cap)
            {
                float amount = (sNew - cap) * depositSpeed;
                sNew -= amount;
                h += amount;
            }
            else
            {
                float amount = (cap - sNew) * erodeSpeed;
                amount = math.min(amount, h);
                h -= amount;
                sNew += amount;
            }

            heightsOut[idx] = math.clamp(h, 0f, 1f);
            waterOut[idx] = wNew;
            sedimentOut[idx] = sNew;
        }

        private float SedInFromNeighbor(int nIdx, float waterFluxToUs, float neighWater)
        {
            // proportional sediment advection
            if (neighWater <= 1e-6f) return 0f;
            float frac = math.saturate(waterFluxToUs / neighWater);
            return sedimentIn[nIdx] * frac;
        }
    }

    [BurstCompile]
    private struct ThermalOutflowJob : IJobParallelFor
    {
        public int res;
        public float talus;

        [ReadOnly] public NativeArray<float> heights;

        [WriteOnly] public NativeArray<float> outE;
        [WriteOnly] public NativeArray<float> outW;
        [WriteOnly] public NativeArray<float> outN;
        [WriteOnly] public NativeArray<float> outS;

        [ReadOnly] public NativeArray<int> cancelFlag;

        public void Execute(int idx)
        {
            if (cancelFlag.IsCreated && cancelFlag[0] != 0) return;

            int x = idx % res;
            int y = idx / res;

            if (x == 0 || y == 0 || x == res - 1 || y == res - 1)
            {
                outE[idx] = 0f; outW[idx] = 0f; outN[idx] = 0f; outS[idx] = 0f;
                return;
            }

            float h = heights[idx];

            float e = heights[idx + 1];
            float w = heights[idx - 1];
            float n = heights[idx + res];
            float s = heights[idx - res];

            float de = h - e;
            float dw = h - w;
            float dn = h - n;
            float ds = h - s;

            float oe = (de > talus) ? (de - talus) * 0.25f : 0f;
            float ow = (dw > talus) ? (dw - talus) * 0.25f : 0f;
            float on = (dn > talus) ? (dn - talus) * 0.25f : 0f;
            float os = (ds > talus) ? (ds - talus) * 0.25f : 0f;

            float outSum = oe + ow + on + os;
            if (outSum > h)
            {
                float scale = h / math.max(outSum, 1e-6f);
                oe *= scale; ow *= scale; on *= scale; os *= scale;
            }

            outE[idx] = oe;
            outW[idx] = ow;
            outN[idx] = on;
            outS[idx] = os;
        }
    }

    [BurstCompile]
    private struct ThermalApplyJob : IJobParallelFor
    {
        public int res;

        [ReadOnly] public NativeArray<float> heightsIn;
        [WriteOnly] public NativeArray<float> heightsOut;

        [ReadOnly] public NativeArray<float> outE;
        [ReadOnly] public NativeArray<float> outW;
        [ReadOnly] public NativeArray<float> outN;
        [ReadOnly] public NativeArray<float> outS;

        [ReadOnly] public NativeArray<int> cancelFlag;

        public void Execute(int idx)
        {
            if (cancelFlag.IsCreated && cancelFlag[0] != 0) return;

            int x = idx % res;
            int y = idx / res;

            if (x == 0 || y == 0 || x == res - 1 || y == res - 1)
            {
                heightsOut[idx] = heightsIn[idx];
                return;
            }

            float h = heightsIn[idx];
            float outSum = outE[idx] + outW[idx] + outN[idx] + outS[idx];

            float inSum =
                outE[idx - 1] +
                outW[idx + 1] +
                outN[idx - res] +
                outS[idx + res];

            heightsOut[idx] = math.clamp(h - outSum + inSum, 0f, 1f);
        }
    }

    [BurstCompile]
    private struct OceanAccessJob : IJobParallelFor
    {
        public int res;
        public float waterH;
        public int sidesMask;
        public int fadeDist;

        public NativeArray<float> heights;
        [ReadOnly] public NativeArray<int> cancelFlag;

        public void Execute(int idx)
        {
            if (cancelFlag.IsCreated && cancelFlag[0] != 0) return;

            int x = idx % res;
            int y = idx / res;

            int denom = math.max(1, fadeDist);
            int edgeMax = res - 1;

            float t = 1f;

            if ((sidesMask & (int)OceanSides.North) != 0)
            {
                int dist = edgeMax - y;
                if (dist <= fadeDist) t = math.min(t, dist / (float)denom);
            }
            if ((sidesMask & (int)OceanSides.South) != 0)
            {
                int dist = y;
                if (dist <= fadeDist) t = math.min(t, dist / (float)denom);
            }
            if ((sidesMask & (int)OceanSides.East) != 0)
            {
                int dist = edgeMax - x;
                if (dist <= fadeDist) t = math.min(t, dist / (float)denom);
            }
            if ((sidesMask & (int)OceanSides.West) != 0)
            {
                int dist = x;
                if (dist <= fadeDist) t = math.min(t, dist / (float)denom);
            }

            heights[idx] = math.lerp(waterH, heights[idx], t);
        }
    }

    [BurstCompile]
    private struct HeightmapJob : IJobParallelFor
    {
        public int width, height, octaves;
        public float baseRoughness, roughness, persistence;
        public float2 offset;

        [WriteOnly] public NativeArray<float> heights;
        [ReadOnly] public NativeArray<int> cancelFlag;

        public void Execute(int idx)
        {
            if (cancelFlag.IsCreated && cancelFlag[0] != 0)
                return;

            int x = idx % width;
            int y = idx / width;

            float amp = 1f, freq = baseRoughness, sum = 0f, wsum = 0f;
            for (int o = 0; o < octaves; o++)
            {
                if (cancelFlag.IsCreated && cancelFlag[0] != 0)
                    return;

                float sx = (x - width * 0.5f + offset.x) / width * freq;
                float sy = (y - height * 0.5f + offset.y) / height * freq;
                float n = noise.snoise(new float2(sx, sy)) * 0.5f + 0.5f;

                sum += n * amp;
                wsum += amp;
                amp *= persistence;
                freq *= roughness;
            }

            heights[idx] = sum / wsum;
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────────
    #region Native Helpers / Cleanup

    /// <summary>
    /// Ensures a NativeArray exists with the desired length, reallocating if required.
    /// Uses Allocator.Persistent because this is editor tooling and we want to reuse buffers.
    /// </summary>
    private static void EnsureNative<T>(ref NativeArray<T> arr, int length) where T : struct
    {
        if (arr.IsCreated && arr.Length == length) return;
        if (arr.IsCreated) arr.Dispose();
        arr = new NativeArray<T>(length, Allocator.Persistent);
    }

    private void EnsureCancelFlag()
    {
        if (_jobCancelFlag.IsCreated) return;
        _jobCancelFlag = new NativeArray<int>(1, Allocator.Persistent);
        _jobCancelFlag[0] = 0;
    }

    /// <summary>
    /// Swaps two native buffers (used for double-buffered stages).
    /// </summary>
    private static void Swap(ref NativeArray<float> a, ref NativeArray<float> b)
    {
        var t = a; a = b; b = t;
    }

    public void Cleanup()
    {
        if (_isJobScheduled)
        {
            _pipelineHandle.Complete();
            _isJobScheduled = false;
            EditorApplication.update -= PollPipeline;
        }

        if (_heights.IsCreated) _heights.Dispose();
        if (_scratch.IsCreated) _scratch.Dispose();

        if (_waterA.IsCreated) _waterA.Dispose();
        if (_waterB.IsCreated) _waterB.Dispose();
        if (_sedA.IsCreated) _sedA.Dispose();
        if (_sedB.IsCreated) _sedB.Dispose();

        if (_fluxE.IsCreated) _fluxE.Dispose();
        if (_fluxW.IsCreated) _fluxW.Dispose();
        if (_fluxN.IsCreated) _fluxN.Dispose();
        if (_fluxS.IsCreated) _fluxS.Dispose();

        if (_outE.IsCreated) _outE.Dispose();
        if (_outW.IsCreated) _outW.Dispose();
        if (_outN.IsCreated) _outN.Dispose();
        if (_outS.IsCreated) _outS.Dispose();

        if (_jobCancelFlag.IsCreated) _jobCancelFlag.Dispose();

        EditorUtility.ClearProgressBar();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────────
    #region Saves / Favorites (unchanged)

    private void PromptNameAndSaveFavorite()
    {
        if (!_heights.IsCreated)
        {
            EditorUtility.DisplayDialog("No Heightmap Data",
                "Please generate a heightmap first before saving to favorites.",
                "Got it");
            return;
        }

        string defaultName = $"Tile_{tileX}_{tileY}_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        FavoriteNameWindow.Show((name) => OnSaveFavorite(name), defaultName);
    }

    private void OnSaveFavorite(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            EditorUtility.DisplayDialog("Invalid Name", "Please provide a non-empty name.", "OK");
            return;
        }

        // Ensure favorites folder exists
        if (!AssetDatabase.IsValidFolder(FavoriteFolder))
        {
            AssetDatabase.CreateFolder("Assets", "WorldBuilderSaved");
        }

        // Refresh favorites list from disk
        LoadFavorites();

        // Manage max favorites: confirm overwrite if full
        if (favorites.Count >= MaxFavorites)
        {
            var olderAsset = favorites[favorites.Count - 1];
            string message = $"You have {MaxFavorites} favorites saved.\n\n" +
                             $"Do you want to replace the oldest favorite?\n{olderAsset.displayName}";
            if (!EditorUtility.DisplayDialog("Overwrite Favorite?",
                                             message,
                                             "Replace", "Cancel"))
                return;

            // Remove the oldest asset file
            string oldPath = AssetDatabase.GetAssetPath(olderAsset);
            AssetDatabase.DeleteAsset(oldPath);
            favorites.RemoveAt(favorites.Count - 1);
        }

        // Create and configure the new SavedHeightmap asset
        SavedHeightmap saved = ScriptableObject.CreateInstance<SavedHeightmap>();
        saved.displayName = name;
        saved.terrainSide = terrainSide;
        saved.terrainHeight = terrainHeight;
        saved.terrainPosition = targetTerrain != null ? targetTerrain.transform.position : Vector3.zero;

        // heights array
        int res = mapSizes[mapIndex];
        int hmRes = res + 1;
        saved.heightmapResolution = hmRes;
        saved.heights = new float[res * res];
        NativeArray<float>.Copy(_heights, saved.heights, res * res);

        // record creation time
        saved.createdTime = System.DateTime.Now.ToOADate();

        // Save the asset to the project
        string assetPath = Path.Combine(FavoriteFolder, saved.displayName + ".asset");
        AssetDatabase.CreateAsset(saved, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Add to favorites list
        favorites.Insert(0, saved);
        if (favorites.Count > MaxFavorites)
            favorites.RemoveRange(MaxFavorites, favorites.Count - MaxFavorites);

        Debug.Log($"Saved current heightmap to favorites: {saved.displayName}");
    }

    private void LoadFavorites()
    {
        favorites.Clear();

        // Ensure the favorites folder exists
        if (!AssetDatabase.IsValidFolder(FavoriteFolder))
            return;

        // Load all SavedHeightmap assets in the favorites folder
        string[] guids = AssetDatabase.FindAssets("t:SavedHeightmap", new[] { FavoriteFolder });
        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            SavedHeightmap heightmap = AssetDatabase.LoadAssetAtPath<SavedHeightmap>(assetPath);
            if (heightmap != null)
                favorites.Add(heightmap);
        }

        // Sort favorites by creation time (newest first)
        favorites.Sort((a, b) => b.createdTime.CompareTo(a.createdTime));

        Debug.Log($"Loaded {favorites.Count} favorites.");
    }

    private void LoadFavoriteIntoTerrain(SavedHeightmap saved)
    {
        // Create new terrain if targetTerrain is not assigned
        if (targetTerrain == null)
        {
            CreateNewTerrain();
        }

        // Dump heights into a new array
        int res = saved.heightmapResolution - 1;
        int hmRes = saved.heightmapResolution;
        var arr = new float[hmRes, hmRes];
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
                arr[y, x] = saved.heights[y * res + x];
        // pad right column
        for (int y = 0; y < res; y++)
            arr[y, res] = arr[y, res - 1];
        // pad top row
        for (int x = 0; x < hmRes; x++)
            arr[res, x] = arr[res - 1, x];

        // Apply to terrain
        var td = targetTerrain.terrainData;
        td.heightmapResolution = hmRes;
        td.size = new Vector3(saved.terrainSide, saved.terrainHeight, saved.terrainSide);
        td.SetHeights(0, 0, arr);

        // Move terrain object
        targetTerrain.transform.position = saved.terrainPosition;

        EditorUtility.SetDirty(td);

        Debug.Log($"Loaded favorite heightmap: {saved.displayName}");
    }

    private void DeleteFavorite(int index)
    {
        if (index < 0 || index >= favorites.Count)
            return;

        // Remove the asset from the project
        string assetPath = AssetDatabase.GetAssetPath(favorites[index]);
        AssetDatabase.DeleteAsset(assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Remove from favorites list
        favorites.RemoveAt(index);

        Debug.Log($"Deleted favorite at index {index}");
    }

    private class FavoriteNameWindow : EditorWindow
    {
        private System.Action<string> _onSave;
        private string _name = "";

        public static void Show(System.Action<string> onSave, string defaultName)
        {
            var w = CreateInstance<FavoriteNameWindow>();
            w._onSave = onSave;
            w._name = defaultName;
            w.titleContent = new GUIContent("Name Favorite");
            w.position = new Rect(Screen.width / 2, Screen.height / 2, 420, 70);
            w.ShowModalUtility();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Enter a name for this favorite:", EditorStyles.wordWrappedLabel);
            _name = EditorGUILayout.TextField(_name);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save"))
            {
                _onSave?.Invoke(_name);
                Close();
            }
            if (GUILayout.Button("Cancel"))
                Close();
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawFavoritesUI()
    {
        EditorGUILayout.LabelField("Favorites", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Save Favorite"))
            PromptNameAndSaveFavorite();
        if (GUILayout.Button("Refresh"))
            LoadFavorites();
        EditorGUILayout.EndHorizontal();

        if (favorites == null || favorites.Count == 0)
        {
            EditorGUILayout.LabelField("No favorites saved.");
            return;
        }

        for (int i = 0; i < favorites.Count; i++)
        {
            var f = favorites[i];
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(f.displayName);
            if (GUILayout.Button("Load"))
                LoadFavoriteIntoTerrain(f);
            if (GUILayout.Button("Delete"))
            {
                DeleteFavorite(i);
                break;
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    #endregion
}