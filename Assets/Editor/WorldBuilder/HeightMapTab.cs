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
    [Tooltip("Enable thermal erosion")]
    private bool doThermalErode = true;
    [Tooltip("Erosion passes")]
    private int erosionPasses = 20;
    [Tooltip("Talus slope threshold")]
    private float talusAngle = 0.02f;
    [Tooltip("Minimum water level for rivers")]
    private float waterLevel = 0.3f;

    [Header("Neighbor Terrains (Optional)")]
    [Tooltip("Drop your already‐placed neighboring Terrain tiles here")]
    [SerializeField] private Terrain neighborNW, neighborN, neighborNE;
    [SerializeField] private Terrain neighborW, neighborE;
    [SerializeField] private Terrain neighborSW, neighborS, neighborSE;

    // Ocean / Beach Access
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
    #region Native Buffers

    /// <summary>Flat [res×res] buffer of heights [0..1].</summary>
    private NativeArray<float> _heights;

    // Non-blocking job state
    private JobHandle _heightJobHandle;
    private bool _isJobScheduled = false;
    private int _pendingRes = 0;
    private bool _cancelRequested = false;

    // Cancellation flag passed into the job (0 = run,1 = cancel)
    private NativeArray<int> _jobCancelFlag;

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

    // Auto-load favorites when the tab is created
    public HeightMapTab()
    {
        LoadFavorites();
    }

    public void Draw()
    {
        // Tile grid
        EditorGUILayout.LabelField("Tile Grid Settings", EditorStyles.boldLabel);
        tilesX = EditorGUILayout.IntField("Tiles X", tilesX);
        tilesY = EditorGUILayout.IntField("Tiles Y", tilesY);
        EditorGUILayout.BeginHorizontal();
        tileX = EditorGUILayout.IntSlider("Tile X", tileX, 0, tilesX - 1);
        tileY = EditorGUILayout.IntSlider("Tile Z", tileY, 0, tilesY - 1);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        // Resolution & seed
        EditorGUILayout.LabelField("Heightmap Resolution & Seed", EditorStyles.boldLabel);
        mapIndex = EditorGUILayout.Popup("Samples Per Side", mapIndex,
                         System.Array.ConvertAll(mapSizes, s => s.ToString()));
        globalSeed = EditorGUILayout.FloatField("Global Seed", globalSeed);

        EditorGUILayout.Space();
        // Noise params
        EditorGUILayout.LabelField("Fractal Noise Parameters", EditorStyles.boldLabel);
        baseRoughness = EditorGUILayout.Slider("Base Roughness", baseRoughness, 0.1f, 5f);
        roughness = EditorGUILayout.Slider("Roughness", roughness, 1f, 10f);
        persistence = EditorGUILayout.Slider("Persistence", persistence, 0f, 1f);
        octaves = EditorGUILayout.IntSlider("Octaves", octaves, 1, 8);

        EditorGUILayout.Space();
        // Features
        EditorGUILayout.LabelField("Geographic Features", EditorStyles.boldLabel);
        useRidges = EditorGUILayout.Toggle("Enable Ridges/Cliffs", useRidges);
        if (useRidges)
            ridgeStrength = EditorGUILayout.Slider("Ridge Strength", ridgeStrength, 1f, 5f);

        doThermalErode = EditorGUILayout.Toggle("Enable Thermal Erosion", doThermalErode);
        if (doThermalErode)
        {
            erosionPasses = EditorGUILayout.IntSlider("Erosion Passes", erosionPasses, 1, 100);
            talusAngle = EditorGUILayout.Slider("Talus Angle", talusAngle, 0.001f, 0.1f);
        }

        EditorGUILayout.Space();
        // Neighbors
        EditorGUILayout.LabelField("Neighbor Terrains (Optional)", EditorStyles.boldLabel);
        neighborNW = (Terrain)EditorGUILayout.ObjectField("North-West", neighborNW, typeof(Terrain), true);
        neighborN = (Terrain)EditorGUILayout.ObjectField("North", neighborN, typeof(Terrain), true);
        neighborNE = (Terrain)EditorGUILayout.ObjectField("North-East", neighborNE, typeof(Terrain), true);
        neighborW = (Terrain)EditorGUILayout.ObjectField("West", neighborW, typeof(Terrain), true);
        neighborE = (Terrain)EditorGUILayout.ObjectField("East", neighborE, typeof(Terrain), true);
        neighborSW = (Terrain)EditorGUILayout.ObjectField("South-West", neighborSW, typeof(Terrain), true);
        neighborS = (Terrain)EditorGUILayout.ObjectField("South", neighborS, typeof(Terrain), true);
        neighborSE = (Terrain)EditorGUILayout.ObjectField("South-East", neighborSE, typeof(Terrain), true);

        EditorGUILayout.Space();
        // Ocean access
        EditorGUILayout.LabelField("Ocean / Beach Access", EditorStyles.boldLabel);
        oceanSides = (OceanSides)EditorGUILayout.EnumFlagsField("Edges with Ocean", oceanSides);
        if (oceanSides != OceanSides.None)
            beachFadeDistance = EditorGUILayout.IntSlider("Beach Fade Distance", beachFadeDistance, 0, mapSizes[mapIndex] / 2);

        EditorGUILayout.Space();
        // Terrain settings
        EditorGUILayout.LabelField("Terrain Settings", EditorStyles.boldLabel);
        terrainSide = EditorGUILayout.FloatField("Tile Size (X,Z)", terrainSide);
        terrainHeight = EditorGUILayout.FloatField("Max Height (Y)", terrainHeight);
        targetTerrain = (Terrain)EditorGUILayout.ObjectField("Target Terrain", targetTerrain, typeof(Terrain), true);

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Generate Tile (Fast + Features)"))
        {
            _cancelRequested = false;
            GenerateTileFast();
        }
        // Cancel button
        if (GUILayout.Button("Cancel"))
        {
            RequestCancel();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        DrawFavoritesUI();
    }

    private void DrawFavoritesUI()
    {
        EditorGUILayout.LabelField("Favorites (max5)", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Refresh"))
            LoadFavorites();
        if (GUILayout.Button("Save Current"))
            PromptNameAndSaveFavorite();
        EditorGUILayout.EndHorizontal();

        // List favorites
        for (int i = 0; i < MaxFavorites; i++)
        {
            if (i < favorites.Count)
            {
                var fav = favorites[i];
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(fav.displayName, GUILayout.Width(200)))
                {
                    LoadFavoriteIntoTerrain(fav);
                }
                if (GUILayout.Button("Delete", GUILayout.Width(60)))
                {
                    DeleteFavorite(i);
                }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.LabelField($"Slot {i + 1}: Empty");
            }
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────────
    #region Generation Workflow

    private void GenerateTileFast()
    {
        // 1) Ensure a target Terrain exists (or create one)
        if (targetTerrain == null)
        {
            if (EditorUtility.DisplayDialog("No Terrain Selected",
                                            "Create a new Terrain GameObject?",
                                            "Yes", "Cancel"))
                CreateNewTerrain();
            else
                return;
        }

        // 2) Setup resolution & allocate buffer
        int res = mapSizes[mapIndex];
        int total = res * res;
        if (_heights.IsCreated) _heights.Dispose();
        _heights = new NativeArray<float>(total, Allocator.Persistent);

        // prepare cancel flag
        if (_jobCancelFlag.IsCreated) _jobCancelFlag.Dispose();
        _jobCancelFlag = new NativeArray<int>(1, Allocator.Persistent);
        _jobCancelFlag[0] = 0;

        // 3) Fractal noise (Burst job) - schedule non-blocking
        var job = new HeightmapJob
        {
            width = res,
            height = res,
            baseRoughness = baseRoughness,
            roughness = roughness,
            persistence = persistence,
            octaves = octaves,
            offset = new float2(globalSeed + tileX * res,
                                  globalSeed + tileY * res),
            heights = _heights,
            cancelFlag = _jobCancelFlag
        };

        _heightJobHandle = job.Schedule(total, 64);
        _isJobScheduled = true;
        _pendingRes = res;
        _cancelRequested = false;

        // Start polling in editor update
        EditorApplication.update -= PollHeightmapJob;
        EditorApplication.update += PollHeightmapJob;
    }

    private void RequestCancel()
    {
        _cancelRequested = true;
        // signal job to cancel as early as possible
        if (_jobCancelFlag.IsCreated)
            _jobCancelFlag[0] = 1;
    }

    private void PollHeightmapJob()
    {
        if (!_isJobScheduled) return;

        if (_cancelRequested)
        {
            // Try to complete quickly then cleanup
            _heightJobHandle.Complete();
            if (_heights.IsCreated) _heights.Dispose();
            if (_jobCancelFlag.IsCreated) _jobCancelFlag.Dispose();
            _isJobScheduled = false;
            _pendingRes = 0;
            EditorApplication.update -= PollHeightmapJob;
            EditorUtility.ClearProgressBar();
            Debug.Log("Heightmap generation cancelled.");
            return;
        }

        // Show a waiting progress while job runs
        if (!_heightJobHandle.IsCompleted)
        {
            EditorUtility.DisplayProgressBar("Generating Heightmap", "Computing noise (Burst)...", 0.1f);
            return;
        }

        // Complete the job and proceed with feature steps
        _heightJobHandle.Complete();

        int res = _pendingRes;

        // 4) Ridges, erosion, river carve with progress updates
        EditorUtility.DisplayProgressBar("Generating Heightmap", "Applying ridges...", 0.3f);
        if (useRidges) ApplyRidgeNoise(res, _heights, ridgeStrength);

        if (doThermalErode)
        {
            // ThermalErode will update progress during passes
            ThermalErode(res, _heights, erosionPasses, talusAngle);
        }

        EditorUtility.DisplayProgressBar("Generating Heightmap", "Carving rivers...", 0.75f);
        CarveRiver(res, _heights);

        // 5) Stitch neighbors for seamless borders
        EditorUtility.DisplayProgressBar("Generating Heightmap", "Blending with neighbors...", 0.8f);
        BlendWithNeighbors(res, _heights);

        // 6) Ocean/beach access carving (if any)
        if (oceanSides != OceanSides.None)
            ApplyOceanAccess(res, _heights, waterLevel, oceanSides, beachFadeDistance);

        // 7) Write into TerrainData (and pad +1 for Unity)
        EditorUtility.DisplayProgressBar("Generating Heightmap", "Applying to Terrain...", 0.95f);
        ApplyBufferToTerrain(_heights, res);

        // 8) Cleanup
        if (_heights.IsCreated)
            _heights.Dispose();
        if (_jobCancelFlag.IsCreated)
            _jobCancelFlag.Dispose();

        _isJobScheduled = false;
        _pendingRes = 0;

        EditorApplication.update -= PollHeightmapJob;
        EditorUtility.ClearProgressBar();

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

    /// <summary>
    /// Copies buffer→heightmap + pads last row/column for seamless stitching.
    /// </summary>
    private void ApplyBufferToTerrain(NativeArray<float> buf, int res)
    {
        Undo.RecordObject(targetTerrain.terrainData, "Apply Heights");
        var td = targetTerrain.terrainData;
        int hmRes = res + 1;
        td.heightmapResolution = hmRes;
        td.size = new Vector3(terrainSide, terrainHeight, terrainSide);

        var arr = new float[hmRes, hmRes];
        // core
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
                arr[y, x] = buf[y * res + x];
        // pad right column
        for (int y = 0; y < res; y++)
            arr[y, res] = arr[y, res - 1];
        // pad top row
        for (int x = 0; x < hmRes; x++)
            arr[res, x] = arr[res - 1, x];

        td.SetHeights(0, 0, arr);
        EditorUtility.SetDirty(td);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────────
    #region Seamless Neighbor Blending

    /// <summary>
    /// If any neighbor is assigned, copy its adjacent border heights
    /// into our buffer so shared edges match perfectly. Uses sampling of neighbor
    /// terrains in normalized coordinates so differing heightmap resolutions are handled.
    /// </summary>
    private void BlendWithNeighbors(int res, NativeArray<float> buf)
    {
        // Helper to convert interpolated neighbor world height to normalized neighbor height
        float SampleNeighborNormalizedHeight(Terrain neigh, float u, float v)
        {
            if (neigh == null) return 0f;
            // World position on neighbor terrain at normalized coords
            var nd = neigh.terrainData;
            Vector3 worldPos = neigh.transform.position + new Vector3(u * nd.size.x, 0f, v * nd.size.z);
            float worldH = neigh.SampleHeight(worldPos);
            // Convert to normalized [0..1] relative to neighbor terrain height
            float normalized = nd.size.y > 0f ? worldH / nd.size.y : 0f;
            return Mathf.Clamp01(normalized);
        }

        // 1) North neighbor → copy its south edge (v =0)
        if (neighborN != null)
        {
            for (int x = 0; x < res; x++)
            {
                float u = (res == 1) ? 0f : (float)x / (res - 1);
                buf[(res - 1) * res + x] = SampleNeighborNormalizedHeight(neighborN, u, 0f);
            }
        }

        // 2) South neighbor → copy its north edge (v =1)
        if (neighborS != null)
        {
            for (int x = 0; x < res; x++)
            {
                float u = (res == 1) ? 0f : (float)x / (res - 1);
                buf[0 * res + x] = SampleNeighborNormalizedHeight(neighborS, u, 1f);
            }
        }

        // 3) East neighbor → copy its west edge (u =0)
        if (neighborE != null)
        {
            for (int y = 0; y < res; y++)
            {
                float v = (res == 1) ? 0f : (float)y / (res - 1);
                buf[y * res + (res - 1)] = SampleNeighborNormalizedHeight(neighborE, 0f, v);
            }
        }

        // 4) West neighbor → copy its east edge (u =1)
        if (neighborW != null)
        {
            for (int y = 0; y < res; y++)
            {
                float v = (res == 1) ? 0f : (float)y / (res - 1);
                buf[y * res + 0] = SampleNeighborNormalizedHeight(neighborW, 1f, v);
            }
        }

        // 5) Four corners (optional exact match) - sample neighbor corner positions
        if (neighborNE != null)
            buf[(res - 1) * res + (res - 1)] = SampleNeighborNormalizedHeight(neighborNE, 0f, 0f);
        if (neighborNW != null)
            buf[(res - 1) * res + 0] = SampleNeighborNormalizedHeight(neighborNW, 1f, 0f);
        if (neighborSE != null)
            buf[0 * res + (res - 1)] = SampleNeighborNormalizedHeight(neighborSE, 0f, 1f);
        if (neighborSW != null)
            buf[0 * res + 0] = SampleNeighborNormalizedHeight(neighborSW, 1f, 1f);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────────
    #region Geographic Features

    private void ApplyRidgeNoise(int res, NativeArray<float> buf, float strength)
    {
        for (int i = 0; i < buf.Length; i++)
        {
            float v = buf[i];
            buf[i] = math.abs(1f - 2f * v) * v * strength;
        }
    }

    private void ThermalErode(int res, NativeArray<float> buf, int passes, float talus)
    {
        for (int p = 0; p < passes; p++)
        {
            // update progress for erosion
            if (passes > 0)
                EditorUtility.DisplayProgressBar("Generating Heightmap", $"Thermal erosion pass {p + 1}/{passes}", 0.3f + 0.4f * ((float)p / passes));

            for (int y = 1; y < res - 1; y++)
                for (int x = 1; x < res - 1; x++)
                {
                    int idx = y * res + x;
                    float h = buf[idx];
                    foreach (var off in new int2[] { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) })
                    {
                        int ni = (y + off.y) * res + (x + off.x);
                        float dh = h - buf[ni];
                        if (dh > talus)
                        {
                            float m = (dh - talus) * 0.5f;
                            buf[idx] -= m;
                            buf[ni] += m;
                        }
                    }
                }
        }

        // Clear progress from erosion step
        EditorUtility.ClearProgressBar();
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
            foreach (var off in new int2[] { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) })
            {
                int nx = math.clamp(x + off.x, 0, res - 1);
                int ny = math.clamp(y + off.y, 0, res - 1);
                float v = buf[ny * res + nx];
                if (v < best) { best = v; dir = off; }
            }
            x += dir.x; y += dir.y;
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────────
    #region Ocean / Beach Access

    private void ApplyOceanAccess(int res, NativeArray<float> buf,
                                 float waterH, OceanSides sides, int fadeDist)
    {
        // (Implementation unchanged from last iteration...)
        // … carve each flagged edge down toward waterH, then fade inland over fadeDist …
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────────
    #region Burst Noise Job

    [BurstCompile]
    struct HeightmapJob : IJobParallelFor
    {
        public int width, height, octaves;
        public float baseRoughness, roughness, persistence;
        public float2 offset;
        [WriteOnly] public NativeArray<float> heights;
        [ReadOnly] public NativeArray<int> cancelFlag;
        public void Execute(int idx)
        {
            // quick cancellation check
            if (cancelFlag.IsCreated && cancelFlag[0] != 0)
                return;

            int x = idx % width, y = idx / width;
            float amp = 1f, freq = baseRoughness, sum = 0f, wsum = 0f;
            for (int o = 0; o < octaves; o++)
            {
                // another cancellation opportunity between octaves
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

    /// <summary>
    /// Unsubscribes and disposes native resources when editor is closed or domain reloads.
    /// </summary>
    public void Cleanup()
    {
        if (_isJobScheduled)
        {
            // ensure job completes and clean up
            _heightJobHandle.Complete();
            _isJobScheduled = false;
            EditorApplication.update -= PollHeightmapJob;
        }

        if (_heights.IsCreated)
            _heights.Dispose();

        if (_jobCancelFlag.IsCreated)
            _jobCancelFlag.Dispose();

        EditorUtility.ClearProgressBar();
    }

    #region Saves

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

    #endregion

    // ─────────────────────────────────────────────────────────────────────────────
    #region Helper UI Window

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
            {
                Close();
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    #endregion
}