using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using UnityEditor.UIElements;

/// <summary>
/// Custom Unity Editor tool for visualizing the social structure of villages and citizens.
/// Built using GraphView to support zooming, panning, and edge-based navigation.
/// </summary>
public class SocialGraphWindow : EditorWindow
{
    [SerializeField] private MockSocialData mockData;

    private SocialGraphView graphView;

    // Adds a new menu item to Unity under Tools > Social Graph
    [MenuItem("Tools/Social Graph")]
    public static void OpenWindow()
    {
        var window = GetWindow<SocialGraphWindow>();
        window.titleContent = new GUIContent("Social Graph");
    }

    // Called when the editor window is created or scripts are recompiled
    private void OnEnable()
    {
        ConstructGraphView();
        CreateToolbar();

        // Auto-load from Resources
        if (mockData == null)
        {
            mockData = Resources.Load<MockSocialData>("MockSocialData"); // Filename without extension
            if (mockData == null)
                Debug.LogWarning("MockSocialData.asset not found in Resources/");
        }

        GenerateMockGraph();
    }

    // Called when the editor window is closed or scripts are recompiled
    private void OnDisable()
    {
        rootVisualElement.Remove(graphView);
    }

    /// <summary>
    /// Sets up the main graph view and attaches it to the editor window.
    /// </summary>
    private void ConstructGraphView()
    {
        graphView = new SocialGraphView
        {
            name = "Social Graph View"
        };

        // Match graph size to window and enable interaction
        graphView.StretchToParentSize();
        rootVisualElement.Add(graphView);
    }

    /// <summary>
    /// Temporary placeholder graph using mock village nodes and dummy connections.
    /// Later this will be replaced by ECS-based data queries.
    /// </summary>
    private void GenerateMockGraph()
    {
        if (mockData == null)
        {
            Debug.LogWarning("MockSocialData not assigned.");
            return;
        }

        graphView.ClearGraph();

        var citizenLookup = new Dictionary<string, CitizenNode>();
        var citizenNodes = new List<CitizenNode>();

        // Create village and citizen nodes
        foreach (var village in mockData.villages)
        {
            var villageNode = graphView.CreateVillageNode(village.villageName, village.editorPosition);

            foreach (var citizen in village.citizens)
            {
                var citizenNode = graphView.CreateCitizenNode(citizen, citizen.editorPosition);
                graphView.ConnectNodes(villageNode, citizenNode, 1f);

                if (string.IsNullOrWhiteSpace(citizen.name))
                {
                    Debug.LogWarning($"Citizen at position {citizen.editorPosition} has no name.");
                    continue; // skip adding a nameless citizen
                }

                if (citizenLookup.ContainsKey(citizen.name))
                {
                    Debug.LogWarning($"Duplicate citizen name detected: {citizen.name}");
                }
                else
                {
                    citizenLookup[citizen.guid] = citizenNode;
                    citizenNodes.Add(citizenNode);
                }
                citizenNodes.Add(citizenNode);
            }
        }

        // Create connections between citizens
        foreach (var village in mockData.villages)
        {
            foreach (var citizen in village.citizens)
            {
                if (!citizenLookup.TryGetValue(citizen.name, out var fromNode)) continue;

                foreach (var bridge in citizen.connections)
                {
                    if (string.IsNullOrWhiteSpace(bridge.targetGuid)) continue;

                    if (citizenLookup.TryGetValue(bridge.targetGuid, out var toNode))
                    {
                        graphView.ConnectNodes(fromNode, toNode, bridge.relationshipStrength);
                    }
                }
            }
        }

        // Create guild groups
        graphView.CreateGuildGroups(citizenNodes);

        if (mockData.villages == null || mockData.villages.Count == 0)
        {
            var testVillage = new MockVillage
            {
                villageName = "Test Village",
                editorPosition = new Vector2(200, 200),
                citizens = new System.Collections.Generic.List<MockCitizen>()
            };

            var globalPool = new List<MockCitizen>();
            testVillage.Randomize(globalPool); // call the Randomize method we defined

            mockData.villages.Add(testVillage);
        }

    }

    private void CreateToolbar()
    {
        var toolbar = new Toolbar();

        var randomizeButton = new Button(() =>
        {
            RandomizeAllMockData();
            GenerateMockGraph();
        })
        {
            text = "Randomize All"
        };

        toolbar.Add(randomizeButton);
        rootVisualElement.Add(toolbar);

        var saveButton = new Button(() =>
        {
            SaveMockDataSnapshot();
        })
        { text = "Save Snapshot" };

        toolbar.Add(saveButton);
    }

    private void RandomizeAllMockData()
    {
        if (mockData == null)
        {
            mockData = ScriptableObject.CreateInstance<MockSocialData>();
        }

        mockData.villages = new List<MockVillage>();

        int villageCount = Random.Range(3, 6);
        var globalPool = new List<MockCitizen>();

        for (int i = 0; i < villageCount; i++)
        {
            var village = new MockVillage
            {
                villageName = $"Village_{i + 1}",
                editorPosition = new Vector2(300 * i, 100 * Random.Range(0, 3)),
                citizens = new List<MockCitizen>()
            };

            // Pre-fill citizen list and randomize
            int numCitizens = Random.Range(5, 12);
            for (int j = 0; j < numCitizens; j++)
            {
                var c = new MockCitizen();
                village.citizens.Add(c);
                globalPool.Add(c);
            }

            foreach (var c in village.citizens)
                c.Randomize(village.citizens, globalPool);

            mockData.villages.Add(village);
        }

        Debug.Log($"[Randomize All] Generated {villageCount} villages and {globalPool.Count} citizens.");
    }

    private void SaveMockDataSnapshot()
    {
        if (mockData == null)
        {
            Debug.LogWarning("No mock data to save.");
            return;
        }

        string path = EditorUtility.SaveFilePanelInProject(
            "Save Social Graph Snapshot",
            $"SocialGraphSnapshot_{System.DateTime.Now:yyyyMMdd_HHmmss}",
            "asset",
            "Choose where to save the mock data asset"
        );

        if (!string.IsNullOrEmpty(path))
        {
            var clone = ScriptableObject.CreateInstance<MockSocialData>();
            clone.villages = mockData.villages.ConvertAll(v => CloneVillage(v));
            AssetDatabase.CreateAsset(clone, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Snapshot saved to: {path}");
        }
    }

    private MockVillage CloneVillage(MockVillage original)
    {
        var clone = new MockVillage
        {
            villageName = original.villageName,
            editorPosition = original.editorPosition,
            citizens = new List<MockCitizen>()
        };

        foreach (var c in original.citizens)
        {
            clone.citizens.Add(CloneCitizen(c));
        }

        return clone;
    }

    private MockCitizen CloneCitizen(MockCitizen original)
    {
        return new MockCitizen
        {
            name = original.name,
            age = original.age,
            socialRank = original.socialRank,
            guild = original.guild,
            hunger = original.hunger,
            sleepiness = original.sleepiness,
            safety = original.safety,
            socialContact = original.socialContact,
            ethics = original.ethics,
            portrait = original.portrait,
            editorPosition = original.editorPosition,
            connections = new List<MockBridge>(original.connections) // shallow copy is enough
        };
    }
}
