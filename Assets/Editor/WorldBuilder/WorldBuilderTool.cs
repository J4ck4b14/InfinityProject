using UnityEditor;
using UnityEngine;

/// <summary>
/// Main editor window for the World Builder Tool.
/// Hosts three tabs—Map, Flora, and Structures—and delegates drawing and teardown.
/// </summary>
public class WorldBuilderTool : EditorWindow
{
    // Enumeration of available tabs in the window
    private enum Tab { Map, Flora, Structures }

    // Currently selected tab
    private Tab currentTab = Tab.Map;

    // Instances of each tab handler
    private HeightMapTab heightMapTab;
    private FloraTab floraTab;
    private StructureTab structureTab;

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
        heightMapTab = new HeightMapTab();
        floraTab = new FloraTab();
        structureTab = new StructureTab();
    }

    /// <summary>
    /// Renders the tab selector and the active tab’s GUI each frame.
    /// </summary>
    private void OnGUI()
    {
        DrawTabSelector();

        switch (currentTab)
        {
            case Tab.Map:
                heightMapTab?.Draw();
                break;
            case Tab.Flora:
                floraTab?.Draw();
                break;
            case Tab.Structures:
                structureTab?.Draw();
                break;
        }
    }

    /// <summary>
    /// Draws the three toggle buttons for switching between Map, Flora, and Structures.
    /// </summary>
    private void DrawTabSelector()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Toggle(currentTab == Tab.Map, "Map", "Button"))
            currentTab = Tab.Map;
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
        heightMapTab?.Cleanup();
        floraTab?.Cleanup();
        structureTab?.Cleanup();
    }
}
