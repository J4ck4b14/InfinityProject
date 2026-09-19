using InfinityProject.World.Ecology;
using InfinityProject.World.Fauna;
using InfinityProject.World.Flora;
using InfinityProject.World.Geography;
using InfinityProject.World.Ground;
using InfinityProject.World.Hydrology;
using InfinityProject.World.Observation;
using InfinityProject.World.Presentation;
using InfinityProject.World.Timekeeping;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class InfinityLiveEcologyBootstrap
{
    private const string TerrainMaterialPath = "Assets/Materials/Terrain/M_Terrain.mat";
    private const string RiverMaterialPath = "Assets/Materials/Water/M_River.mat";
    static InfinityLiveEcologyBootstrap()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
            EditorApplication.delayCall += Bootstrap;
        else if (state == PlayModeStateChange.ExitingPlayMode)
            FaunaEcsPreview.Stop();
    }

    private static void Bootstrap()
    {
        if (!Application.isPlaying) return;
        FaunaEcsPreview.Stop();

        Terrain[] terrains = Terrain.activeTerrains;
        for (int i = 0; i < terrains.Length; i++)
            TryAttach(terrains[i]);
    }

    private static void TryAttach(Terrain terrain)
    {
        if (terrain == null) return;

        InfinityTimeController time = terrain.GetComponent<InfinityTimeController>();
        if (time == null) return;

        GeographyAsset geographyAsset = terrain.GetComponent<GeographyReference>()?.Geography;
        FloraAsset floraAsset = terrain.GetComponent<FloraReference>()?.Flora;
        FloraStateAsset floraStateAsset = terrain.GetComponent<FloraStateReference>()?.FloraState;
        FaunaAsset faunaAsset = terrain.GetComponent<FaunaReference>()?.Fauna;
        GroundConditionAsset groundAsset = terrain.GetComponent<GroundConditionReference>()?.GroundConditions;
        HydrologyAsset hydrologyAsset = terrain.GetComponent<HydrologyReference>()?.Hydrology;

        if (geographyAsset == null || floraAsset == null || floraStateAsset == null || faunaAsset == null)
        {
            Debug.LogWarning("[Infinity Live Ecology] Terrain has a Time Controller but is missing Geography, Flora, living Flora, or Fauna references.");
            return;
        }

        GeographyData geography = WorldEditorDataCache.GetGeography(geographyAsset);
        if (geography == null || !WorldEditorDataCache.TryGetFlora(floraAsset, out FloraData floraPotential) ||
            !FloraStateBinaryCache.TryLoad(floraStateAsset, out FloraSimulationData floraState) ||
            !FaunaBinaryCache.TryLoad(faunaAsset, out FaunaSimulationData faunaState))
        {
            Debug.LogWarning("[Infinity Live Ecology] Could not load the current world state. Reopen Ecology Lab and regenerate/reinitialize any missing state.");
            return;
        }

        GameObject doePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DoePrefabBuilder.PrefabPath);
        if (doePrefab == null)
        {
            // Presentation is optional. Try to restore the prefab automatically, but never block simulation on it.
            try
            {
                doePrefab = DoePrefabBuilder.Build();
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"[Infinity Live Ecology] Doe prefab could not be rebuilt. Ecology will run without runtime deer presentation.\n{exception.Message}");
            }
        }

        InfinityLiveEcologyController controller = terrain.GetComponent<InfinityLiveEcologyController>();
        if (controller == null) controller = terrain.gameObject.AddComponent<InfinityLiveEcologyController>();

        controller.Initialize(
            terrain,
            time,
            geography,
            floraPotential,
            floraState,
            floraAsset.Settings,
            floraStateAsset.Settings,
            faunaState,
            faunaAsset.Settings,
            doePrefab);

        GroundConditionData ground = null;
        HydrologyData hydrology = null;
        if (groundAsset != null) WorldEditorDataCache.TryGetGround(groundAsset, out ground);
        if (hydrologyAsset != null) WorldEditorDataCache.TryGetHydrology(hydrologyAsset, out hydrology);

        EnsureFloraPrefabs();
        GameObject grassPrefab = FloraPrefabBuilder.LoadGrass();
        GameObject shrubPrefab = FloraPrefabBuilder.LoadShrub();
        GameObject coniferPrefab = FloraPrefabBuilder.LoadConifer();
        Texture2D soilTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/World/Terrain/TT_Brown Dirt.BMP");
        Texture2D grassTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/World/Terrain/TT_Green Grass.BMP");
        Texture2D rockTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/World/Terrain/TT_Cliff.jpg");
        Material terrainMaterial = AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);
        Material riverMaterial = AssetDatabase.LoadAssetAtPath<Material>(RiverMaterialPath);

        InfinityWorldPresentationController worldPresentation = terrain.GetComponent<InfinityWorldPresentationController>();
        if (worldPresentation == null) worldPresentation = terrain.gameObject.AddComponent<InfinityWorldPresentationController>();
        worldPresentation.Initialize(
            terrain,
            geography,
            ground,
            hydrology,
            floraState,
            grassPrefab,
            shrubPrefab,
            coniferPrefab,
            soilTexture,
            grassTexture,
            rockTexture,
            terrainMaterial,
            riverMaterial,
            controller);

        AttachObserverCamera();

        string presentation = doePrefab != null ? "with prefab presentation" : "headless";
        Debug.Log($"[Infinity Live Ecology] Running {faunaState.AliveCount}/{faunaState.AgentCount} deer from day {faunaState.SimulationTimeDays:0.00} ({presentation}).");
    }
    private static void EnsureFloraPrefabs()
    {
        try
        {
            FloraPrefabBuilder.EnsureCurrent();
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[Infinity Presentation] Flora prefabs could not be rebuilt.\n{exception.Message}");
        }
    }

    private static void AttachObserverCamera()
    {
        Camera camera = Camera.main;
        if (camera == null) camera = Object.FindFirstObjectByType<Camera>();
        if (camera == null) return;
        if (camera.GetComponent<InfinityObserverCamera>() == null)
            camera.gameObject.AddComponent<InfinityObserverCamera>();
    }

}
