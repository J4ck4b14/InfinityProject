using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using UnityEngine;

/// <summary>
/// Represents a citizen in the graph, with visual and data reference.
/// </summary>
public class CitizenNode : Node
{
    public MockCitizen mockData;

    private VisualElement portrait;
    private VisualElement infoContainer;
    private VisualElement moneyContainer;
    private VisualElement ethicsPanel;

    private Port opinionPort; // What this citizen thinks of others (dynamic position)
    private Port targetPort;  // What others think of this citizen (dynamic position)

    private const float PortraitSize = 40f;

    public CitizenNode(MockCitizen citizen, bool isSelected = false)
    {
        mockData = citizen;
        title = string.Empty;
        name = $"Citizen_{citizen.guid}";

        // Base styling
        AddToClassList("citizen-node");
        style.width = 250;
        style.height = 180;

        var main = new VisualElement();
        main.style.flexDirection = FlexDirection.Row;
        main.style.marginBottom = 6;
        main.style.marginTop = 6;
        main.style.marginLeft = 8;
        main.style.marginRight = 8;

        // Portrait
        portrait = new VisualElement();
        portrait.style.width = PortraitSize;
        portrait.style.height = PortraitSize;
        portrait.style.backgroundColor = GetPortraitColor();
        portrait.style.marginRight = 10;
        portrait.style.borderTopLeftRadius = 4;
        portrait.style.borderBottomLeftRadius = 4;
        main.Add(portrait);

        // Info
        infoContainer = new VisualElement();
        infoContainer.style.flexGrow = 1;

        AddTextLine($"<b>Name:</b> {citizen.name}");
        AddTextLine($"<b>Age:</b> {citizen.age}  <b>Gender:</b> {citizen.gender}");
        AddTextLine($"<b>ID:</b> {citizen.guid.Substring(0, 5)}");
        AddTextLine($"<b>Profession:</b> {citizen.profession}");
        AddTextLine($"<b>Status:</b> {citizen.status}");
        AddTextLine($"<b>Guild:</b> {citizen.guild}");

        main.Add(infoContainer);
        mainContainer.Add(main);

        // Money bar
        moneyContainer = new VisualElement();
        moneyContainer.style.flexDirection = FlexDirection.Row;
        moneyContainer.style.justifyContent = Justify.FlexEnd;
        moneyContainer.style.alignItems = Align.Center;
        moneyContainer.style.marginRight = 8;

        var coin = new Label("\uD83D\uDCB0");
        coin.style.unityFontStyleAndWeight = FontStyle.Bold;
        coin.style.marginRight = 4;
        var amount = new Label($"{citizen.money}");
        moneyContainer.Add(coin);
        moneyContainer.Add(amount);
        mainContainer.Add(moneyContainer);

        // Ethics panel (only visible if selected)
        ethicsPanel = new VisualElement();
        ethicsPanel.style.marginTop = 4;
        ethicsPanel.style.paddingBottom = 4;
        ethicsPanel.style.flexDirection = FlexDirection.Column;

        if (isSelected)
        {
            AddEthicsSlider("Lawfulness", citizen.ethics.lawfulness);
            AddEthicsSlider("Justice", citizen.ethics.justice);
            AddEthicsSlider("Care", citizen.ethics.care);
            AddEthicsSlider("Beneficence", citizen.ethics.beneficence);
            AddEthicsSlider("Honesty", citizen.ethics.honesty);
            AddEthicsSlider("Loyalty", citizen.ethics.loyalty);
            AddEthicsSlider("Autonomy", citizen.ethics.autonomy);
            AddEthicsSlider("Respect", citizen.ethics.respect);
            AddEthicsSlider("Courage", citizen.ethics.courage);
            AddEthicsSlider("Temperance", citizen.ethics.temperance);
            mainContainer.Add(ethicsPanel);
        }

        // Dynamic ports
        opinionPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(float));
        opinionPort.portName = string.Empty;
        targetPort = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(float));
        targetPort.portName = string.Empty;

        AddDynamicPort(opinionPort, new Vector2(-5, 20));
        AddDynamicPort(targetPort, new Vector2(style.width.value.value - 5, style.height.value.value - 30));

        RefreshExpandedState();
        RefreshPorts();
    }

    private void AddDynamicPort(Port port, Vector2 localOffset)
    {
        port.style.position = Position.Absolute;
        port.style.left = localOffset.x;
        port.style.top = localOffset.y;
        Add(port);
        RegisterCallback<MouseDownEvent>(evt =>
        {
            var graph = GetFirstAncestorOfType<SocialGraphView>();

            if (evt.button == 0)
            {
                graph?.SetSelectedCitizen(this);
            }
            else if (evt.button == 1)
            {
                graph?.TryInitiateInteraction(this, evt.mousePosition);
            }
        });
    }

    private void AddTextLine(string content)
    {
        var label = new Label(content);
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        label.style.fontSize = 11;
        infoContainer.Add(label);
    }

    private void AddEthicsSlider(string label, float value)
    {
        var container = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 2 } };
        container.Add(new Label(label) { style = { width = 80, fontSize = 10 } });

        var slider = new Slider(-1f, 1f) { value = value };
        slider.style.flexGrow = 1;
        slider.SetEnabled(false);
        container.Add(slider);

        ethicsPanel.Add(container);
    }

    private Color GetPortraitColor()
    {
        // Generate deterministic color based on GUID hash
        int hash = mockData.guid.GetHashCode();
        Random.InitState(hash);
        return new Color(Random.value, Random.value, Random.value);
    }
}