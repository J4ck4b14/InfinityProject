using UnityEditor;
using UnityEngine;

public class WorldBuilderTool : EditorWindow
{
    private enum Tab { Map, Flora, Structures }
    private Tab currentTab = Tab.Map;

    private HeightMapTab heightMapTab;
    private FloraTab floraTab;
    private StructureTab structureTab;

    [MenuItem("Tools/World Builder")]
    public static void ShowWindow()
    {
        GetWindow<WorldBuilderTool>("World Builder");
    }

    private void OnEnable()
    {
        heightMapTab = new HeightMapTab();
        floraTab = new FloraTab();
        structureTab = new StructureTab();
    }

    private void OnGUI()
    {
        DrawTabSelector();

        switch (currentTab)
        {
            case Tab.Map: heightMapTab.Draw(); break;
            case Tab.Flora: floraTab.Draw(); break;
            case Tab.Structures: structureTab.Draw(); break;
        }
    }

    private void DrawTabSelector()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Toggle(currentTab == Tab.Map, "Map", "Button")) currentTab = Tab.Map;
        if (GUILayout.Toggle(currentTab == Tab.Flora, "Flora", "Button")) currentTab = Tab.Flora;
        if (GUILayout.Toggle(currentTab == Tab.Structures, "Structures", "Button")) currentTab = Tab.Structures;
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(10);
    }

    private void OnDisable()
    {
        // Tell each tab to unsubscribe
        floraTab.Cleanup();
        structureTab.Cleanup();
    }
}
