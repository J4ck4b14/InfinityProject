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
    private VisualElement ethicsPanelWrapper;

    public Port opinionPort; // What this citizen thinks of others (dynamic position)
    public Port targetPort;  // What others think of this citizen (dynamic position)

    private const float PortraitSize = 40f;

    public CitizenNode(MockCitizen citizen, bool isSelected = false)
    {
        mockData = citizen;
        // Match background to guild color
        if (mockData.guild == "Unaffiliated")
            style.backgroundColor = new StyleColor(new Color(0.6f, 0.6f, 0.6f, 1f)); // grey
        else
            style.backgroundColor = SocialGraphView.GuildVisuals.GetGuildColor(mockData.guild);
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

        ethicsPanelWrapper = new VisualElement(); // <— wrapper that gets added/removed
        ethicsPanelWrapper.style.marginTop = 4;
        ethicsPanelWrapper.Add(ethicsPanel);

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
            mainContainer.Add(ethicsPanelWrapper);
        }

        // Dynamic ports
        opinionPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(float));
        opinionPort.portName = "";
        opinionPort.style.position = Position.Absolute;
        opinionPort.style.left = style.width.value.value * 0.5f - 8f;
        opinionPort.style.top = -10f;
        opinionPort.portColor = Color.red;
        Add(opinionPort);
        outputContainer.Add(opinionPort);

        targetPort = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(float));
        targetPort.portName = "";
        targetPort.style.position = Position.Absolute;
        targetPort.style.left = style.width.value.value * 0.5f - 8f;
        targetPort.style.top = style.height.value.value - 10f;
        targetPort.portColor = Color.blue;
        Add(targetPort);
        inputContainer.Add(targetPort);

        RegisterCallback<MouseDownEvent>(evt =>
        {
            var graph = GetFirstAncestorOfType<SocialGraphView>();
            if (evt.button == 0) graph?.SetSelectedCitizen(this);
            else if (evt.button == 1) graph?.TryInitiateInteraction(this, evt.mousePosition);
        });

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
        label.style.color = Color.black;
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

    public void SetSelected(bool selected)
    {
        if (selected)
        {
            style.height = 300;
            ethicsPanel.Clear();

            AddEthicsSlider("Lawfulness", mockData.ethics.lawfulness);
            AddEthicsSlider("Justice", mockData.ethics.justice);
            AddEthicsSlider("Care", mockData.ethics.care);
            AddEthicsSlider("Beneficence", mockData.ethics.beneficence);
            AddEthicsSlider("Honesty", mockData.ethics.honesty);
            AddEthicsSlider("Loyalty", mockData.ethics.loyalty);
            AddEthicsSlider("Autonomy", mockData.ethics.autonomy);
            AddEthicsSlider("Respect", mockData.ethics.respect);
            AddEthicsSlider("Courage", mockData.ethics.courage);
            AddEthicsSlider("Temperance", mockData.ethics.temperance);

            if (!mainContainer.Contains(ethicsPanelWrapper))
                mainContainer.Add(ethicsPanelWrapper);
        }
        else
        {
            style.height = 180;

            if (ethicsPanelWrapper != null && ethicsPanelWrapper.parent == mainContainer)
                mainContainer.Remove(ethicsPanelWrapper);
        }
    }
}