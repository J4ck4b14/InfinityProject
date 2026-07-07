using UnityEditor;
using UnityEngine;

/// <summary>
/// Main editor window for the World Builder Tool.
/// Hosts three tabs-Map, Flora, and Structures-and delegates drawing and teardown.
/// </summary>
public class WorldBuilderTool : EditorWindow
{
    // Enumeration of available tabs in the window
    private enum Tab { Flora, Structures }

    // Currently selected tab
    private Tab currentTab = Tab.Flora;

    // Instances of each tab handler
    private FloraTab floraTab;
    private StructureTab structureTab;

    // Horizontal UI scale for the tab content (replaces horizontal scrolling)
    private float horizontalScale =1.0f;

    // Vertical scroll position for the tab content
    private Vector2 scrollPos = Vector2.zero;

    /// <summary>
    /// Opens the World Builder window via the Tools menu.
    /// </summary>
    [MenuItem("Tools/World Builder")]
    public static void ShowWindow()
    {
        GetWindow<WorldBuilderTool>("World Builder");
    }

    /// <summary>
    /// Called when the window is enabled or scripts are recompiled.
    /// Instantiates each tab’s controller.
    /// </summary>
    private void OnEnable()
    {
        floraTab = new FloraTab();
        structureTab = new StructureTab();
    }

    /// <summary>
    /// Renders the tab selector and the active tab’s GUI each frame.
    /// </summary>
    private void OnGUI()
    {
        DrawTabSelector();

        // Horizontal scale control (acts like a "scale box" for content width)
        GUILayout.Space(6);

        // Apply horizontal scale to subsequent GUI drawing by modifying GUI.matrix.
        // Scale around the left-top corner of the window.
        var oldMatrix = GUI.matrix;
        // Compute pivot in pixels (top-left of client area)
        Vector2 pivot = new Vector2(0,0);
        var scaleMatrix = Matrix4x4.TRS(new Vector3(pivot.x, pivot.y,0), Quaternion.identity, new Vector3(horizontalScale,1f,1f));
        GUI.matrix = scaleMatrix * oldMatrix;

        // Start vertical scroll area so content can scroll if taller than window
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.ExpandHeight(true));

        // Draw the active tab content (scaled horizontally)
        switch (currentTab)
        {
            case Tab.Flora:
                floraTab?.Draw();
                break;
            case Tab.Structures:
                structureTab?.Draw();
                break;
        }

        EditorGUILayout.EndScrollView();

        // Restore GUI matrix so other editor UI is unaffected
        GUI.matrix = oldMatrix;
    }

    /// <summary>
    /// Draws the three toggle buttons for switching between Map, Flora, and Structures.
    /// </summary>
    private void DrawTabSelector()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Toggle(currentTab == Tab.Flora, "Flora", "Button"))
            currentTab = Tab.Flora;
        if (GUILayout.Toggle(currentTab == Tab.Structures, "Structures", "Button"))
            currentTab = Tab.Structures;
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(10);
    }

    /// <summary>
    /// Called when the window is closed or disabled.
    /// Ensures each tab unsubscribes from any SceneView or editor callbacks.
    /// </summary>
    private void OnDisable()
    {
        floraTab?.Cleanup();
        structureTab?.Cleanup();
    }
}
