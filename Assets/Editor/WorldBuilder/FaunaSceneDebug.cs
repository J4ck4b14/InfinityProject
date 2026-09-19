using InfinityProject.World.Fauna;
using InfinityProject.World.Geography;
using UnityEditor;
using UnityEngine;

internal static class FaunaSceneDebug
{
    private static Terrain _terrain;
    private static GeographyData _geography;
    private static FaunaSimulationData _fauna;
    private static bool _enabled;

    static FaunaSceneDebug()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    public static void Set(Terrain terrain, GeographyData geography, FaunaSimulationData fauna, bool enabled)
    {
        _terrain = terrain;
        _geography = geography;
        _fauna = fauna;
        _enabled = enabled;
        SceneView.RepaintAll();
    }

    public static void Clear()
    {
        _terrain = null;
        _geography = null;
        _fauna = null;
        _enabled = false;
        SceneView.RepaintAll();
    }

    private static void OnSceneGUI(SceneView view)
    {
        if (!_enabled || _terrain == null || _geography == null || _fauna == null) return;
        Color previousColor = Handles.color;
        for (int i = 0; i < _fauna.Agents.Length; i++)
        {
            FaunaAgentState agent = _fauna.Agents[i];
            if (!agent.Alive) continue;
            Vector2 local = agent.PositionLocalMeters;
            float y = _geography.SampleElevationMeters(new Unity.Mathematics.float2(local.x, local.y));
            Vector3 world = _terrain.transform.TransformPoint(new Vector3(local.x, y + 2f, local.y));
            float size = HandleUtility.GetHandleSize(world) * 0.04f;
            Color color = Color.Lerp(new Color(1f, 0.9f, 0.1f, 0.95f), new Color(1f, 0.05f, 0.02f, 0.95f), agent.Hunger01);
            Handles.color = color;
            Handles.DrawSolidDisc(world, _terrain.transform.up, size);
        }
        Handles.color = previousColor;
    }
}
