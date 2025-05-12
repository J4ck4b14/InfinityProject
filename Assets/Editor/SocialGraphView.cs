using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements; 

/// <summary>
/// Custom GraphView responsible for rendering and managing the social node network.
/// </summary>
public class SocialGraphView : GraphView
{
    private CitizenNode selectedCitizen;
    private List<Edge> allEdges = new();

    public SocialGraphView()
    {
        // Load the USS style sheet
        var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Editor/Styles/SocialGraph.uss");
        if (styleSheet != null)
        {
            styleSheets.Add(styleSheet);
        }
        else
        {
            Debug.LogWarning("SocialGraph.uss not found in Assets/Editor/Styles");
        }

        // Enable basic interaction
        SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
        this.AddManipulator(new ContentDragger());
        this.AddManipulator(new SelectionDragger());
        this.AddManipulator(new RectangleSelector());

        // Background grid
        Insert(0, new GridBackground());

        // Visual styling
        style.flexGrow = 1.0f;
    }

    /// <summary>
    /// Clears the current graph view, including all nodes and edges.
    /// </summary>
    public void ClearGraph()
    {
        DeleteElements(graphElements);
    }

    /// <summary>
    /// Instantiates a basic village node with a given label and position.
    /// </summary>
    public VillageNode CreateVillageNode(string villageName, Vector2 position)
    {
        var node = new VillageNode(villageName);
        node.SetPosition(new Rect(position, new Vector2(200, 100)));
        node.AddToClassList("village-node");
        AddElement(node);
        return node;
    }

    /// <summary>
    /// Creates an edge between two nodes, visually representing a relationship.
    /// </summary>
    public void ConnectNodes(Node from, Node to, float strength)
    {
        // Buscar automáticamente el primer puerto de salida y entrada
        var outPort = from.outputContainer.Q<Port>();
        var inPort = to.inputContainer.Q<Port>();

        if (outPort == null || inPort == null)
        {
            Debug.LogWarning($"No se encontraron puertos válidos en nodos '{from.title}' o '{to.title}'.");
            return;
        }

        var edge = outPort.ConnectTo(inPort);
        AddElement(edge);

        // Assume citizen nodes store the reputation
        if (from is CitizenNode fromC && to is CitizenNode toC)
        {
            var bridge = fromC.mockData.connections.Find(b => b.targetGuid == toC.mockData.name);
            if (bridge != null)
            {
                StyleEdgeByReputation(edge, bridge.relationshipStrength, bridge.reputation);
            }
        }

        // Estilizado visual
        edge.edgeControl.edgeWidth = (int)Mathf.Lerp(1f, 5f, strength);
        edge.style.borderTopColor = Color.Lerp(Color.red, Color.green, strength);
        edge.style.borderBottomColor = Color.Lerp(Color.red, Color.green, strength);
        edge.style.borderLeftColor = Color.Lerp(Color.red, Color.green, strength);
        edge.style.borderRightColor = Color.Lerp(Color.red, Color.green, strength);

        allEdges.Add(edge);
    }

    public CitizenNode CreateCitizenNode(MockCitizen citizen, Vector2 position)
    {
        var node = new CitizenNode(citizen);
        node.SetPosition(new Rect(position, new Vector2(220, 130)));
        node.AddToClassList("citizen-node");
        AddElement(node);
        return node;
    }

    public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
    {
        base.BuildContextualMenu(evt);
        Vector2 mousePosition = evt.mousePosition;

        evt.menu.AppendAction("Add Citizen", _ =>
        {
            var newCitizen = new MockCitizen
            {
                name = $"Newbie_{Random.Range(1000, 9999)}",
                age = Random.Range(18, 70),
                guild = "Verdant Bloom", // For example
                ethics = MockEthicalProfile.Neutral,
                hunger = 0.5f,
                sleepiness = 0.4f,
                safety = 0.6f,
                socialContact = 0.5f,
                connections = new(),
                portrait = null,
                editorPosition = mousePosition
            };

            var node = CreateCitizenNode(newCitizen, mousePosition);
        });

        evt.menu.AppendAction("Clear Selection", _ =>
        {
            SetSelectedCitizen(null);
        });
    }

    public void CreateGuildGroups(List<CitizenNode> citizens)
    {
        var groups = new Dictionary<string, List<CitizenNode>>();

        // Group by guild
        foreach (var citizen in citizens)
        {
            var guild = citizen.mockData.guild;
            if (string.IsNullOrWhiteSpace(guild)) continue;

            if (!groups.ContainsKey(guild))
                groups[guild] = new List<CitizenNode>();

            groups[guild].Add(citizen);
        }

        // Create group views
        foreach (var kv in groups)
        {
            if (kv.Value.Count < 3) continue; // Skip undersized guilds

            Rect bounds = CalculateBoundingBox(kv.Value);
            var group = new GuildGroupView(kv.Key, GuildVisuals.GetGuildColor(kv.Key));
            group.SetBounds(bounds);
            Insert(0, group); // Draw behind everything else
        }
    }

    private Rect CalculateBoundingBox(List<CitizenNode> nodes)
    {
        float xMin = float.MaxValue, xMax = float.MinValue;
        float yMin = float.MaxValue, yMax = float.MinValue;

        foreach (var node in nodes)
        {
            Rect r = node.GetPosition();
            xMin = Mathf.Min(xMin, r.xMin);
            xMax = Mathf.Max(xMax, r.xMax);
            yMin = Mathf.Min(yMin, r.yMin);
            yMax = Mathf.Max(yMax, r.yMax);
        }

        return Rect.MinMaxRect(xMin - 20, yMin - 20, xMax + 20, yMax + 20); // Padding
    }

    public static class GuildVisuals
    {
        private static readonly Dictionary<string, Color> GuildColors = new()
    {
        { "The Iron Veil",     new Color(1.0f, 0.95f, 0.75f, 0.25f) }, // light yellow
        { "Circle of Embers",  new Color(1.0f, 0.6f, 0.2f, 0.25f) },   // orange-red-yellow
        { "The Hollow Mark",   new Color(0.4f, 0.3f, 0.2f, 0.25f) },   // brown
        { "Pale Fang",         new Color(1.0f, 1.0f, 1.0f, 0.25f) },   // snow white
        { "Anvil Union",       new Color(0.5f, 0.5f, 0.5f, 0.25f) },   // grey
        { "The Oathbound",     new Color(0.5f, 0.2f, 0.7f, 0.25f) },   // purple
        { "The Rooted Maw",    new Color(0.4f, 0.05f, 0.1f, 0.25f) },  // burgundy
        { "Crimson Ledger",    new Color(0.7f, 0.6f, 0.1f, 0.25f) },   // dark yellow
        { "Verdant Bloom",     new Color(0.2f, 0.6f, 0.2f, 0.25f) },   // green
    };

        public static Color GetGuildColor(string guildName)
        {
            return GuildColors.TryGetValue(guildName, out var color) ? color : new Color(1f, 1f, 1f, 0.15f);
        }
    }

    public void StyleEdgeByReputation(Edge edge, float strength, MockEthicalProfile profile)
    {
        // 1. Color from reputation
        Color col = GetReputationColor(profile);
        edge.style.borderTopColor = col;
        edge.style.borderBottomColor = col;
        edge.style.borderLeftColor = col;
        edge.style.borderRightColor = col;

        // 2. Width from strength
        float baseWidth = Mathf.Lerp(1f, 5f, strength);
        edge.edgeControl.edgeWidth = (int)baseWidth;

        // 3. Glow for very high stability
        if (strength > 0.95f && Magnitude(profile) > 6f)
        {
            edge.AddToClassList("edge-glow");
        }
        else if (strength < 0.25f)
        {
            edge.AddToClassList("edge-weak");
        }
        else
        {
            edge.AddToClassList("edge-solid");
        }
    }

    private Color GetReputationColor(MockEthicalProfile rep)
    {
        float favor = rep.care + rep.respect + rep.beneficence + rep.honesty;
        float hostility = -rep.lawfulness + -rep.autonomy + -rep.temperance;

        float score = Mathf.Clamp01((favor - hostility + 5f) / 10f); // Normalized to [0,1]

        return Color.Lerp(Color.red, Color.green, score);
    }

    private float Magnitude(MockEthicalProfile profile)
    {
        return Mathf.Abs(profile.care) + Mathf.Abs(profile.respect) + Mathf.Abs(profile.beneficence) +
               Mathf.Abs(profile.honesty) + Mathf.Abs(profile.lawfulness) + Mathf.Abs(profile.autonomy) +
               Mathf.Abs(profile.temperance) + Mathf.Abs(profile.courage) + Mathf.Abs(profile.loyalty) +
               Mathf.Abs(profile.justice);
    }

    public void SetSelectedCitizen(CitizenNode citizen)
    {
        selectedCitizen = citizen;
        UpdateEdgeStyles();
    }

    private void UpdateEdgeStyles()
    {
        foreach (var edge in allEdges)
        {
            edge.RemoveFromClassList("edge-solid");
            edge.RemoveFromClassList("edge-dotted");
            edge.RemoveFromClassList("edge-dashed");

            if (edge.output == null || edge.input == null || edge.output.node == null || edge.input.node == null)
                continue;

            if (selectedCitizen == null)
            {
                edge.AddToClassList("edge-dashed");
                continue;
            }

            var from = edge.output.node as CitizenNode;
            var to = edge.input.node as CitizenNode;

            if (from == selectedCitizen || to == selectedCitizen)
            {
                edge.AddToClassList("edge-solid");
            }
            else
            {
                edge.AddToClassList("edge-dotted");
            }
        }
    }
}