using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using System;
using Random = UnityEngine.Random;
using System.Linq;

/// <summary>
/// Custom GraphView responsible for rendering and managing the social node network.
/// </summary>
public class SocialGraphView : GraphView
{
    private CitizenNode selectedCitizen;
    private List<Edge> allEdges = new();
    private CitizenInteractionController interactionController = new();

    // Small metadata holder for edges so we can read typed data later
    private class EdgeMeta
    {
        public float Strength;
        public MockEthicalProfile Profile;
    }

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

        // Periodically refresh edge styles so UI reflects changes to reputation data
        schedule.Execute(() => UpdateEdgeStyles()).Every(250);
    }

    /// <summary>
    /// Clears the current graph view, including all nodes and edges.
    /// </summary>
    public void ClearGraph()
    {
        DeleteElements(graphElements);
        allEdges.Clear();
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

    public void ConnectCitizens(CitizenNode source, CitizenNode target, float strength, MockEthicalProfile profile)
    {
        if (source == null || target == null || source == target)
            return;

        var edge = source.opinionPort.ConnectTo(target.targetPort);
        if (edge == null) return;

        // Assign typed metadata so we can read it later without reflection
        edge.userData = new EdgeMeta { Strength = strength, Profile = profile };
        AddElement(edge);
        allEdges.Add(edge);

        // Compute friendliness from profile
        float friendliness = (profile.care + profile.honesty + profile.loyalty + profile.respect + profile.temperance) / 5f;
        Color color = friendliness > 0
            ? Color.Lerp(Color.gray, new Color(0f, 0.6f, 0f), friendliness) // greenish
            : Color.Lerp(Color.gray, new Color(0.6f, 0f, 0f), -friendliness); // reddish

        if (edge.edgeControl != null)
        {
            edge.edgeControl.edgeWidth = (int)Mathf.Lerp(1f, 4f, Mathf.Abs(strength));
            edge.edgeControl.inputColor = color;
            edge.edgeControl.outputColor = color;
        }

        // Style immediately
        if (edge.userData is EdgeMeta meta)
            StyleEdgeByReputation(edge, meta.Strength, meta.Profile);
    }

    public enum VillageConnectionType
    {
        Relationship,
        Commerce
    }

    /// <summary>
    /// Connects two village nodes using either trust (float) or trade (string) edges.
    /// </summary>
    public void ConnectVillages(VillageNode from, VillageNode to, VillageConnectionType type, float weight = 1f, Color? colorOverride = null)
    {
        if (from == null || to == null || from == to)
        {
            Debug.LogWarning("ConnectVillages: invalid source/target.");
            return;
        }

        Port fromPort = null;
        Port toPort = null;

        switch (type)
        {
            case VillageConnectionType.Relationship:
                fromPort = from.relationshipOutput;
                toPort = to.relationshipInput;
                break;

            case VillageConnectionType.Commerce:
                fromPort = from.commerceOutput;
                toPort = to.commerceInput;
                break;

            default:
                Debug.LogWarning("Unknown edge type.");
                return;
        }

        if (fromPort == null || toPort == null)
        {
            Debug.LogWarning($"Missing port(s) for {type} edge.");
            return;
        }

        // Create custom edge with color and thickness
        var edge = new ColoredEdge
        {
            output = fromPort,
            input = toPort,
            edgeColor = GetEdgeColor(type, weight),
            edgeThickness = type == VillageConnectionType.Commerce
        ? 2f
        : Mathf.Lerp(2f, 8f, Mathf.Clamp01(Mathf.Abs(weight))),
            dashed = type == VillageConnectionType.Commerce
        };

        edge.input.Connect(edge);
        edge.output.Connect(edge);
        AddElement(edge);
        edge.edgeControl.visible = false;

        // Defer styling until edgeControl is ready
        edge.ApplyOverlayVisual(this);

        if (type == VillageConnectionType.Relationship)
        {
            edge.EnableInteractiveTrustChange(this, newTrust =>
            {
                edge.edgeColor = GetEdgeColor(type, newTrust);
                edge.edgeThickness = Mathf.Lerp(2f, 8f, Mathf.Abs(newTrust));
                edge.ApplyOverlayVisual(this); // redraw overlay
                Debug.Log($"📝 Updated trust: {from.title} → {to.title} = {newTrust:F2}");
            });
        }

        edge.tooltip = type == VillageConnectionType.Commerce
            ? $"Trade route: {from.title} → {to.title}"
            : $"Trust: {weight:F2} ({from.title} → {to.title})";

        Debug.Log($"✔️ {type} edge created: {from.title} → {to.title} (w={weight:F2})");
    }

    public CitizenNode CreateCitizenNode(MockCitizen citizen, Vector2 position)
    {
        if (citizen == null)
        {
            Debug.LogWarning("X CreateCitizenNode: Citizen is null.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(citizen.guid))
        {
            Debug.LogWarning($"X CreateCitizenNode: Citizen '{citizen.name}' has no valid GUID.");
            return null;
        }

        var node = new CitizenNode(citizen);
        if (node == null)
        {
            Debug.LogWarning($"X CreateCitizenNode: Failed to instantiate CitizenNode for {citizen.name}.");
            return null;
        }

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
        if (edge.edgeControl != null)
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

            // If edge carries metadata, apply reputation-based styling
            if (edge.userData is EdgeMeta meta)
            {
                StyleEdgeByReputation(edge, meta.Strength, meta.Profile);
            }
        }
    }

    public void SetSelectedCitizen(CitizenNode node)
    {
        // Deselect previous
        selectedCitizen?.SetSelected(false);

        selectedCitizen = node;
        interactionController.SetInitiator(node);

        // Select new
        selectedCitizen?.SetSelected(true);

        UpdateEdgeStyles();
    }

    public void SetOnActionChosen(Action<MockCitizen, MockCitizen, string> callback)
    {
        interactionController.SetOnActionChosen(callback);
    }


    public void TryInitiateInteraction(CitizenNode target, Vector2 screenPos)
    {
        interactionController.TryShowContextMenu(target, screenPos);
    }

    private Color GetEdgeColor(VillageConnectionType type, float weight)
    {
        if (type == VillageConnectionType.Commerce)
            return new Color(1f, 0.85f, 0.1f); // Yellow

        if (Mathf.Abs(weight) > 0.9f)
            return new Color(0.0f, 1.0f, 0.55f); // Bright green for strong affinity
        else if (weight < -0.3f)
            return new Color(0.85f, 0.2f, 0.2f); // Red
        else if (weight > 0.3f)
            return new Color(0.2f, 0.85f, 0.2f); // Green
        else
            return new Color(0.6f, 0.6f, 0.6f); // Gray for neutral
    }

    public List<VillageNode> GetAllVillageNodes()
    {
        return graphElements
            .OfType<VillageNode>()
            .ToList();
    }
    public List<CitizenNode> GetAllCitizenNodes()
    {
        return graphElements
            .OfType<CitizenNode>()
            .ToList();
    }

    public void UpdateVillageRelationship(VillageNode from, VillageNode to, float trust)
    {
        // Remove any existing edge between these two
        var existing = graphElements
            .OfType<ColoredEdge>()
            .FirstOrDefault(e => e.output.node == from && e.input.node == to);

        if (existing != null)
        {
            RemoveElement(existing);
        }

        // Reconnect with new value
        ConnectVillages(from, to, VillageConnectionType.Relationship, trust);
    }

}