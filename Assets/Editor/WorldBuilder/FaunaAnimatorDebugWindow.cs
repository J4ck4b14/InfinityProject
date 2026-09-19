using InfinityProject.World.Fauna.Presentation;
using UnityEditor;
using UnityEngine;

public sealed class FaunaAnimatorDebugWindow : EditorWindow
{
    private GUIStyle _nodeStyle;
    private GUIStyle _activeNodeStyle;
    private Texture2D _activeNodeBackground;

    [MenuItem("Infinity/Fauna/Animator Debug")]
    public static void Open()
    {
        GetWindow<FaunaAnimatorDebugWindow>("Fauna Animator").Show();
    }

    private void OnEnable()
    {
        Selection.selectionChanged += Repaint;
        EditorApplication.update += Repaint;
    }

    private void OnDisable()
    {
        Selection.selectionChanged -= Repaint;
        EditorApplication.update -= Repaint;
        if (_activeNodeBackground != null) DestroyImmediate(_activeNodeBackground);
        _activeNodeBackground = null;
        _nodeStyle = null;
        _activeNodeStyle = null;
    }

    private void OnGUI()
    {
        EnsureStyles();
        FaunaPresentationAgent agent = FindSelectedAgent();
        if (agent == null)
        {
            EditorGUILayout.HelpBox("Select a live Doe_* presentation object in the Scene or Hierarchy to inspect its procedural animation state.", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField($"Doe #{agent.AgentId:0000}", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            $"Herd {agent.HerdId} | {agent.AnimationState} | {agent.SpeedMetersPerSecond:0.00} m/s | gait {agent.GaitPhase01:0.00}",
            EditorStyles.miniLabel);
        EditorGUILayout.Space(8f);

        Rect graph = GUILayoutUtility.GetRect(420f, 245f, GUILayout.ExpandWidth(true));
        DrawGraph(graph, agent.AnimationState);

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Physiology", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Hunger {agent.Hunger01:0.000}");
        EditorGUILayout.LabelField($"Energy {agent.Energy01:0.000}");
        EditorGUILayout.LabelField($"Health {agent.Health01:0.000}");
        EditorGUILayout.LabelField($"Consumed {agent.CumulativeFoodConsumedKg:0.00} kg");
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Presentation", EditorStyles.boldLabel);
        EditorGUILayout.Vector3Field("Target", agent.TargetWorldPosition);
        EditorGUILayout.Vector3Field("Heading", agent.HeadingWorld);
        EditorGUILayout.HelpBox(
            "This graph is diagnostic only. The states are lightweight procedural presentation states; there is no per-animal Mecanim Animator or baked clip dependency.",
            MessageType.None);
    }

    private void DrawGraph(Rect rect, DoeAnimationState active)
    {
        GUI.Box(rect, GUIContent.none);
        float w = 105f;
        float h = 42f;

        Rect idle = Node(rect, 0.14f, 0.63f, w, h);
        Rect move = Node(rect, 0.47f, 0.20f, w, h);
        Rect eat = Node(rect, 0.72f, 0.63f, w, h);
        Rect sleep = Node(rect, 0.47f, 0.66f, w, h);
        Rect dead = Node(rect, 0.77f, 0.15f, w, h);

        Handles.BeginGUI();
        Handles.color = new Color(0.65f, 0.65f, 0.65f, 0.9f);
        DrawArrow(Center(idle), Center(move));
        DrawArrow(Center(move), Center(idle));
        DrawArrow(Center(idle), Center(eat));
        DrawArrow(Center(eat), Center(idle));
        DrawArrow(Center(idle), Center(sleep));
        DrawArrow(Center(sleep), Center(idle));
        DrawArrow(Center(move), Center(dead));
        DrawArrow(Center(eat), Center(dead));
        DrawArrow(Center(idle), Center(dead));
        Handles.EndGUI();

        DrawNode(idle, "Idle", active == DoeAnimationState.Idle);
        DrawNode(move, "Move", active == DoeAnimationState.Move);
        DrawNode(eat, "Eat", active == DoeAnimationState.Eat);
        DrawNode(sleep, "Sleep\n(reserved)", active == DoeAnimationState.Sleep);
        DrawNode(dead, "Dead\n(reserved)", active == DoeAnimationState.Dead);
    }

    private static Rect Node(Rect graph, float x01, float y01, float width, float height)
    {
        return new Rect(
            graph.x + graph.width * x01 - width * 0.5f,
            graph.y + graph.height * y01 - height * 0.5f,
            width,
            height);
    }

    private static Vector3 Center(Rect rect) => new(rect.center.x, rect.center.y, 0f);

    private static void DrawArrow(Vector3 a, Vector3 b)
    {
        Vector3 dir = (b - a).normalized;
        Vector3 start = a + dir * 26f;
        Vector3 end = b - dir * 34f;
        Handles.DrawAAPolyLine(2f, start, end);
        Vector3 side = new(-dir.y, dir.x, 0f);
        Handles.DrawAAConvexPolygon(end, end - dir * 9f + side * 4f, end - dir * 9f - side * 4f);
    }

    private void DrawNode(Rect rect, string label, bool active)
    {
        GUI.Box(rect, label, active ? _activeNodeStyle : _nodeStyle);
    }

    private void EnsureStyles()
    {
        if (_nodeStyle != null) return;
        _nodeStyle = new GUIStyle(EditorStyles.helpBox)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        _activeNodeStyle = new GUIStyle(_nodeStyle);
        _activeNodeBackground = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        _activeNodeBackground.SetPixel(0, 0, new Color(0.33f, 0.62f, 0.38f, 1f));
        _activeNodeBackground.Apply();
        _activeNodeStyle.normal.background = _activeNodeBackground;
        _activeNodeStyle.normal.textColor = Color.white;
    }

    private static FaunaPresentationAgent FindSelectedAgent()
    {
        GameObject selected = Selection.activeGameObject;
        return selected != null ? selected.GetComponentInParent<FaunaPresentationAgent>() : null;
    }
}

[CustomEditor(typeof(FaunaPresentationAgent))]
public sealed class FaunaPresentationAgentEditor : Editor
{
    public override void OnInspectorGUI()
    {
        FaunaPresentationAgent agent = (FaunaPresentationAgent)target;
        EditorGUILayout.LabelField($"Doe #{agent.AgentId:0000}", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Herd", agent.HerdId.ToString());
        EditorGUILayout.LabelField("Animation", agent.AnimationState.ToString());
        EditorGUILayout.LabelField("Speed", $"{agent.SpeedMetersPerSecond:0.00} m/s");
        EditorGUILayout.LabelField("Gait phase", agent.GaitPhase01.ToString("0.000"));
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Hunger", agent.Hunger01.ToString("0.000"));
        EditorGUILayout.LabelField("Energy", agent.Energy01.ToString("0.000"));
        EditorGUILayout.LabelField("Health", agent.Health01.ToString("0.000"));
        EditorGUILayout.LabelField("Consumed", $"{agent.CumulativeFoodConsumedKg:0.00} kg");
        EditorGUILayout.Space(6f);
        if (GUILayout.Button("Open Procedural Animator Debug")) FaunaAnimatorDebugWindow.Open();
    }
}
