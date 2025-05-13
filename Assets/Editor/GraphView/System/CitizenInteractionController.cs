using UnityEngine;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using System;
using System.Collections.Generic;

/// <summary>
/// Manages interaction flow between selected citizen and right-clicked target.
/// </summary>
public class CitizenInteractionController
{
    private CitizenNode selectedInitiator;
    private Action<MockCitizen, MockCitizen, string> onActionChosen;

    private readonly Dictionary<string, List<string>> actionMap = new()
    {
        { "Very Good", new List<string> { "Help", "Compliment" } },
        { "Good", new List<string> { "Encourage", "Talk" } },
        { "Mid", new List<string> { "Greet", "Ask Question" } },
        { "Bad", new List<string> { "Ignore", "Insult" } },
        { "Very Bad", new List<string> { "Hit", "Rob" } }
    };

    public void SetInitiator(CitizenNode node)
    {
        selectedInitiator = node;
    }

    public void ClearInitiator()
    {
        selectedInitiator = null;
    }

    public void TryShowContextMenu(CitizenNode targetNode, Vector2 screenPos)
    {
        if (selectedInitiator == null || selectedInitiator == targetNode)
            return;

        var menu = new GenericMenu();

        menu.AddDisabledItem(new GUIContent("Actions"));

        foreach (var category in actionMap.Keys)
        {
            foreach (var action in actionMap[category])
            {
                string label = $"{category}/{action}";
                menu.AddItem(new GUIContent(label), false, () =>
                {
                    onActionChosen?.Invoke(selectedInitiator.mockData, targetNode.mockData, action);
                });
            }
        }

        menu.ShowAsContext();
    }

    public void SetOnActionChosen(Action<MockCitizen, MockCitizen, string> callback)
    {
        onActionChosen = callback;
    }
}

#region Mock Memory Popup

public class MockMemoryImpactPopup : EditorWindow
{
    private static MockCitizen initiator;
    private static MockCitizen target;
    private static string action;
    private static System.Action<MockCitizen, MockCitizen, string, int> onConfirmed;

    private int importance = 5; // Default mid-point

    public static void Show(MockCitizen source, MockCitizen recipient, string actionName, System.Action<MockCitizen, MockCitizen, string, int> callback)
    {
        initiator = source;
        target = recipient;
        action = actionName;
        onConfirmed = callback;

        var window = CreateInstance<MockMemoryImpactPopup>();
        window.titleContent = new GUIContent("Set Memory Importance");
        window.position = new Rect(Screen.width / 2f, Screen.height / 2f, 300, 100);
        window.ShowUtility();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("How much importance will this action hold?", EditorStyles.boldLabel);
        importance = EditorGUILayout.IntSlider("Importance", importance, 1, 10);

        GUILayout.Space(10);
        if (GUILayout.Button("Confirm"))
        {
            onConfirmed?.Invoke(initiator, target, action, importance);
            Close();
        }

        if (GUILayout.Button("Cancel"))
        {
            Close();
        }
    }
}
#endregion

