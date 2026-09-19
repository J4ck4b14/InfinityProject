using System.Collections.Generic;
using InfinityProject.World.Fauna;
using InfinityProject.World.Fauna.ECS;
using InfinityProject.World.Fauna.Presentation;
using InfinityProject.World.Geography;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class FaunaEcsPreview
{
    private sealed class DoePresentation
    {
        public GameObject GameObject;
        public DoePresentationRig Rig;
        public readonly MaterialPropertyBlock MaterialBlock = new();
    }

    private static readonly Dictionary<int, DoePresentation> Presentations = new();
    private static readonly Stack<DoePresentation> Pool = new();
    private static readonly HashSet<int> AliveIds = new();
    private static readonly List<int> ReleaseIds = new();

    private static FaunaEcsSnapshotBridge _bridge;
    private static bool _ownsBridge;
    private static GameObject _root;

    public static bool IsActive => _bridge != null && _bridge.IsCreated;
    public static int EntityCount => _bridge?.EntityCount ?? 0;
    public static int PresentationCount => Presentations.Count;
    public static int PooledCount => Pool.Count;
    public static bool HasDoePrefab => AssetDatabase.LoadAssetAtPath<GameObject>(DoePrefabBuilder.PrefabPath) != null;

    static FaunaEcsPreview()
    {
        AssemblyReloadEvents.beforeAssemblyReload += Stop;
        EditorApplication.quitting += Stop;
    }

    public static void StartOrSync(
        Terrain terrain,
        GeographyData geography,
        FaunaSimulationData fauna,
        FaunaEcsSnapshotBridge externalBridge = null)
    {
        if (terrain == null || geography == null || fauna == null) return;

        GameObject doePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DoePrefabBuilder.PrefabPath);
        if (doePrefab == null)
        {
            Debug.LogWarning($"Doe prefab was not found at '{DoePrefabBuilder.PrefabPath}'. Rebuild it from the Ecology Lab or Infinity/Fauna menu.");
            return;
        }

        if (externalBridge != null)
        {
            if (_ownsBridge) _bridge?.Dispose();
            _bridge = externalBridge;
            _ownsBridge = false;
        }
        else if (_bridge == null || !_bridge.IsCreated)
        {
            _bridge = new FaunaEcsSnapshotBridge();
            _ownsBridge = true;
        }

        _bridge.Sync(fauna);
        EnsureRoot();

        AliveIds.Clear();
        for (int i = 0; i < fauna.Agents.Length; i++)
        {
            int id = fauna.Agents[i].Id;
            if (!_bridge.TryReadAgent(id, out FaunaEcsAgentSnapshot ecs)) continue;

            if (ecs.Physiology.Alive == 0)
            {
                ReleasePresentation(id);
                continue;
            }

            AliveIds.Add(id);
            DoePresentation presentation = AcquirePresentation(id, doePrefab);
            ApplyTransform(presentation, terrain, geography, ecs);
            ApplyDiagnosticTint(presentation, ecs.Physiology.Hunger01);
        }

        ReleaseIds.Clear();
        foreach (KeyValuePair<int, DoePresentation> pair in Presentations)
        {
            if (!AliveIds.Contains(pair.Key)) ReleaseIds.Add(pair.Key);
        }
        for (int i = 0; i < ReleaseIds.Count; i++) ReleasePresentation(ReleaseIds[i]);

        SceneView.RepaintAll();
    }

    public static void Stop()
    {
        foreach (DoePresentation presentation in Presentations.Values)
            DestroyPresentation(presentation);
        Presentations.Clear();

        while (Pool.Count > 0) DestroyPresentation(Pool.Pop());
        AliveIds.Clear();
        ReleaseIds.Clear();

        if (_root != null) Object.DestroyImmediate(_root);
        _root = null;

        if (_ownsBridge) _bridge?.Dispose();
        _bridge = null;
        _ownsBridge = false;
        SceneView.RepaintAll();
    }

    private static void EnsureRoot()
    {
        if (_root != null) return;
        _root = new GameObject("[Infinity] Fauna ECS V1 Preview")
        {
            hideFlags = HideFlags.DontSave
        };
    }

    private static DoePresentation AcquirePresentation(int agentId, GameObject doePrefab)
    {
        if (Presentations.TryGetValue(agentId, out DoePresentation existing)) return existing;

        DoePresentation presentation;
        if (Pool.Count > 0)
        {
            presentation = Pool.Pop();
        }
        else
        {
            GameObject go = Object.Instantiate(doePrefab, _root.transform);
            go.hideFlags = HideFlags.DontSave;
            DoePresentationRig rig = go.GetComponent<DoePresentationRig>();
            if (rig == null)
            {
                Object.DestroyImmediate(go);
                throw new System.InvalidOperationException("Doe prefab is missing DoePresentationRig. Rebuild the prefab.");
            }

            presentation = new DoePresentation
            {
                GameObject = go,
                Rig = rig
            };
        }

        presentation.GameObject.name = $"Doe_ECS_{agentId:0000}";
        presentation.GameObject.transform.SetParent(_root.transform, false);
        presentation.GameObject.SetActive(true);
        Presentations.Add(agentId, presentation);
        return presentation;
    }

    private static void ReleasePresentation(int agentId)
    {
        if (!Presentations.TryGetValue(agentId, out DoePresentation presentation)) return;

        Presentations.Remove(agentId);
        if (presentation.GameObject == null) return;

        presentation.GameObject.SetActive(false);
        Pool.Push(presentation);
    }

    private static void DestroyPresentation(DoePresentation presentation)
    {
        if (presentation?.GameObject != null) Object.DestroyImmediate(presentation.GameObject);
    }

    private static void ApplyTransform(
        DoePresentation presentation,
        Terrain terrain,
        GeographyData geography,
        FaunaEcsAgentSnapshot ecs)
    {
        float2 localMeters = ecs.Position.Meters;
        float elevation = geography.SampleElevationMeters(localMeters);
        Vector3 terrainLocal = new(localMeters.x, elevation, localMeters.y);
        Vector3 worldPosition = terrain.transform.TransformPoint(terrainLocal);

        Vector3 localHeading = new(ecs.Heading.Value.x, 0f, ecs.Heading.Value.y);
        if (localHeading.sqrMagnitude <= 0.000001f) localHeading = Vector3.forward;

        Vector3 worldHeading = terrain.transform.TransformDirection(localHeading.normalized);
        worldHeading = Vector3.ProjectOnPlane(worldHeading, terrain.transform.up).normalized;
        if (worldHeading.sqrMagnitude <= 0.000001f) worldHeading = terrain.transform.forward;

        Quaternion desiredForward = Quaternion.LookRotation(worldHeading, terrain.transform.up);
        Quaternion modelForward = Quaternion.LookRotation(presentation.Rig.ModelForwardLocal, Vector3.up);
        presentation.GameObject.transform.SetPositionAndRotation(
            worldPosition + terrain.transform.up * presentation.Rig.GroundOffset,
            desiredForward * Quaternion.Inverse(modelForward));
    }

    private static void ApplyDiagnosticTint(DoePresentation presentation, float hunger01)
    {
        Color tint = Color.Lerp(Color.white, new Color(1f, 0.45f, 0.25f, 1f), Mathf.Clamp01(hunger01));
        Renderer[] renderers = presentation.Rig.Renderers;
        if (renderers == null) return;

        MaterialPropertyBlock block = presentation.MaterialBlock;
        block.SetColor("_BaseColor", tint);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null) renderers[i].SetPropertyBlock(block);
        }
    }
}
