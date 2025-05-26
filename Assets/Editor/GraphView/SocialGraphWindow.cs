using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Custom Unity Editor tool for visualizing the social structure of villages and citizens.
/// Built using GraphView to support zooming, panning, and edge-based navigation.
/// </summary>
public class SocialGraphWindow : EditorWindow
{
    public enum GraphMode
    {
        Global,
        SingleVillage,
        Settlements
    }

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
    private void GenerateMockGraph(GraphMode mode = GraphMode.Global, string villageFilter = null)
    {
        if (mockData == null) return;

        graphView.ClearGraph();

        var citizenLookup = new Dictionary<string, CitizenNode>();
        var citizenNodes = new List<CitizenNode>();

        var villagesToLoad = mode switch
        {
            GraphMode.Global => mockData.villages,
            GraphMode.SingleVillage => mockData.villages.Where(v => v.villageName == villageFilter).ToList(),
            _ => new List<MockVillage>()
        };

        foreach (var village in villagesToLoad)
        {
            // Only create VillageNode in Global view (optional)
            if (mode == GraphMode.Global)
            {
                var vNode = graphView.CreateVillageNode(village.villageName, village.editorPosition);
                vNode.title = village.villageName;
            }

            float radius = 300f;
            Vector2 center = village.editorPosition + new Vector2(0f, 200f);

            int count = village.citizens.Count;
            float angleStep = 360f / Mathf.Max(count, 1);

            for (int i = 0; i < count; i++)
            {
                var citizen = village.citizens[i];
                if (citizen == null || string.IsNullOrWhiteSpace(citizen.guid)) continue;

                float angle = angleStep * i * Mathf.Deg2Rad;
                Vector2 pos = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

                citizen.editorPosition = pos;

                var citizenNode = graphView.CreateCitizenNode(citizen, pos);
                if (citizenNode == null) continue;

                if (!citizenLookup.ContainsKey(citizen.guid))
                {
                    citizenLookup[citizen.guid] = citizenNode;
                    citizenNodes.Add(citizenNode);
                }
            }
        }

        // Citizen-to-citizen edges
        foreach (var village in villagesToLoad)
        {
            foreach (var citizen in village.citizens)
            {
                if (!citizenLookup.TryGetValue(citizen.guid, out var fromNode)) continue;

                foreach (var bridge in citizen.connections)
                {
                    if (citizenLookup.TryGetValue(bridge.targetGuid, out var toNode))
                    {
                        graphView.ConnectCitizens(fromNode, toNode, bridge.relationshipStrength, bridge.reputation);
                    }
                }
            }
        }

        if (mode == GraphMode.SingleVillage)
        {
            int count = citizenNodes.Count;
            float radius = 300f;
            Vector2 center = new Vector2(600f, 400f); // You can tweak this for visual balance

            for (int i = 0; i < count; i++)
            {
                float angle = 2 * Mathf.PI * i / count;
                Vector2 pos = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                citizenNodes[i].SetPosition(new Rect(pos, citizenNodes[i].GetPosition().size));
            }
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

        // Add dropdown to filter by village
        var villageDropdown = new ToolbarMenu
        {
            text = "Select Village"
        };

        // Settlements = only village-to-village
        villageDropdown.menu.AppendAction("Settlements", _ => GenerateVillageRelationGraph());

        // Global = all villages, all citizens
        villageDropdown.menu.AppendAction("Global", _ => GenerateMockGraph(GraphMode.Global));

        foreach (var village in mockData.villages)
        {
            string name = village.villageName;
            villageDropdown.menu.AppendAction(name, _ => GenerateMockGraph(GraphMode.SingleVillage, name));
        }

        // Add each village from the loaded mock data
        if (mockData != null)
        {
            foreach (var village in mockData.villages)
            {
                string name = village.villageName;
                villageDropdown.menu.AppendAction(name, _ => GenerateMockGraph(name));
            }
        }

        toolbar.Add(villageDropdown);

        // Add a button to clear the graph
        var clearButton = new Button(() =>
        {
            graphView.ClearGraph();
            Debug.Log("Graph cleared.");
        })
        { text = "Clear Graph" };

        toolbar.Add(clearButton);

        var legend = new Label("🟢 Trust  ⚪ Neutral  🔴 Hostility   🟡 Trade");
        legend.style.unityFontStyleAndWeight = FontStyle.Bold;
        legend.style.fontSize = 12;
        legend.style.marginLeft = 20;
        legend.style.color = Color.white;

        toolbar.Add(legend);
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

        // Prepare a pool of unique names
        var possibleNames = new List<string>
        {
            "Ashenreach", "Frostmere", "Duskwatch", "Briarhollow",
            "Sunhelm", "Mournstead", "Dawnrise", "Stonebrook",
            "Hollowshade", "Emberhold"
        };

        for (int i = 0; i < villageCount; i++)
        {
            if (possibleNames.Count == 0)
            {
                Debug.LogWarning("Ran out of unique village names!");
                break;
            }

            // Pick a name randomly from remaining options and remove it
            int index = Random.Range(0, possibleNames.Count);
            string villageName = possibleNames[index];
            possibleNames.RemoveAt(index);

            var village = new MockVillage
            {
                villageName = villageName,
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

        Debug.Log($"[Randomize All] Generated {mockData.villages.Count} villages and {globalPool.Count} citizens.");

        foreach (var v in mockData.villages)
        {
            foreach (var c in v.citizens)
            {
                if (string.IsNullOrWhiteSpace(c.guid))
                    Debug.LogWarning($"! Citizen with missing GUID in {v.villageName}");
                if (string.IsNullOrWhiteSpace(c.name))
                    Debug.LogWarning($"! Citizen with missing name in {v.villageName}");
            }
        }
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

    private void GenerateMockGraph(string villageFilter)
    {
        if (mockData == null) return;

        graphView.ClearGraph();

        var citizenLookup = new Dictionary<string, CitizenNode>();
        var citizenNodes = new List<CitizenNode>();

        var filteredVillages = string.IsNullOrEmpty(villageFilter)
            ? mockData.villages
            : mockData.villages.FindAll(v => v.villageName == villageFilter);

        foreach (var village in filteredVillages)
        {
            VillageNode villageNode = null;

            if (string.IsNullOrEmpty(villageFilter)) // global view
                villageNode = graphView.CreateVillageNode(village.villageName, village.editorPosition);

            foreach (var citizen in village.citizens)
            {
                var citizenNode = graphView.CreateCitizenNode(citizen, citizen.editorPosition);

                if (!citizenLookup.ContainsKey(citizen.guid))
                {
                    citizenLookup[citizen.guid] = citizenNode;
                    citizenNodes.Add(citizenNode);
                }
            }
        }

        foreach (var village in filteredVillages)
        {
            foreach (var citizen in village.citizens)
            {
                if (!citizenLookup.TryGetValue(citizen.guid, out var fromNode)) continue;

                foreach (var bridge in citizen.connections)
                {
                    if (citizenLookup.TryGetValue(bridge.targetGuid, out var toNode))
                    {
                        graphView.ConnectCitizens(fromNode, toNode, bridge.relationshipStrength, bridge.reputation);
                    }
                }
            }
        }

        graphView.CreateGuildGroups(citizenNodes);
    }

    private void GenerateVillageRelationGraph()
    {
        if (mockData == null || mockData.villages == null) return;

        graphView.ClearGraph();

        // Create all village nodes and store them
        Dictionary<string, VillageNode> villageNodes = new();
        foreach (var village in mockData.villages)
        {
            var node = graphView.CreateVillageNode(village.villageName, village.editorPosition);
            villageNodes[village.villageName] = node;

        }

        // Analyze inter-village links via citizen connections
        Dictionary<(string from, string to), (float trust, int commerce)> links = new();

        foreach (var fromVillage in mockData.villages)
        {
            foreach (var citizen in fromVillage.citizens)
            {
                foreach (var bridge in citizen.connections)
                {
                    var targetCitizen = mockData.villages
                        .SelectMany(v => v.citizens)
                        .FirstOrDefault(c => c.guid == bridge.targetGuid);

                    if (targetCitizen == null) continue;

                    var toVillage = mockData.villages.FirstOrDefault(v => v.citizens.Contains(targetCitizen));
                    if (toVillage == null || toVillage == fromVillage) continue;

                    if (string.IsNullOrWhiteSpace(fromVillage.villageName) ||
                        toVillage == null ||
                        string.IsNullOrWhiteSpace(toVillage.villageName) ||
                        toVillage == fromVillage)
                        continue;

                    var key = (from: fromVillage.villageName, to: toVillage.villageName);
                    if (!links.ContainsKey(key))
                        links[key] = (0f, 0);

                    links[key] = (
                        links[key].trust + bridge.reputation.care + bridge.reputation.honesty + bridge.reputation.loyalty,
                        links[key].commerce + 1
                    );
                }
            }
        }

        // Create edges based on inter-village data
        foreach (var kvp in links)
        {
            if (!villageNodes.ContainsKey(kvp.Key.from) || !villageNodes.ContainsKey(kvp.Key.to)) continue;

            float avgTrust = kvp.Value.trust / Mathf.Max(1f, kvp.Value.commerce);
            float weight = Mathf.Clamp01(avgTrust * 0.2f + 0.5f); // Normalize trust to [0,1]

            var from = villageNodes[kvp.Key.from];
            var to = villageNodes[kvp.Key.to];

            // Determine color
            Color edgeColor;
            bool hasTrade = kvp.Value.commerce > 0;

            if (avgTrust >= 0.5f)
                edgeColor = Color.Lerp(Color.green, new Color(0f, 0.3f, 0f), 1 - avgTrust);
            else
                edgeColor = Color.Lerp(Color.red, new Color(0.3f, 0f, 0f), 1 - avgTrust);

            if (hasTrade)
            {
                graphView.ConnectVillages(from, to, SocialGraphView.VillageConnectionType.Commerce);
            }

            // DEBUG TEST: force diverse relationships
            if (villageNodes.Count >= 4)
            {
                var nodes = villageNodes.Values.ToList();

                graphView.ConnectVillages(nodes[0], nodes[1], SocialGraphView.VillageConnectionType.Relationship, -0.4f); // hostile
                graphView.ConnectVillages(nodes[2], nodes[3], SocialGraphView.VillageConnectionType.Relationship, 0f);    // neutral
            }

            // Then connect with color
            graphView.ConnectVillages(from, to, SocialGraphView.VillageConnectionType.Relationship, weight);
        }

        Vector2 center = new Vector2(800, 400);
        float radius = 350f;
        int count = villageNodes.Count;
        float angleStep = 2 * Mathf.PI / count;

        int i = 0;
        foreach (var node in villageNodes.Values)
        {
            float angle = i * angleStep;
            Vector2 pos = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            node.SetPosition(new Rect(pos, node.GetPosition().size));
            i++;
        }

    }

}
