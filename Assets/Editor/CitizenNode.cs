using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using UnityEngine;

/// <summary>
/// Represents a citizen in the graph, with visual and data reference.
/// </summary>
public class CitizenNode : Node
{
    public Port inputPort;
    public Port outputPort;

    public string citizenName;

    public MockCitizen mockData;

    public CitizenNode(MockCitizen citizen)
    {
        mockData = citizen; // Store reference to the full data

        citizenName = citizen.name;
        title = $"{citizen.name} ({citizen.age})";

        inputPort = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(float));
        outputPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(float));

        inputPort.portName = "In";
        outputPort.portName = "Out";

        inputContainer.Add(inputPort);
        outputContainer.Add(outputPort);

        var content = new Label($"Rango: {citizen.socialRank}\nHambre: {citizen.hunger:F2}\nRespeto: {citizen.ethics.respect:F2}");
        mainContainer.Add(content);

        RefreshExpandedState();
        RefreshPorts();

        RegisterCallback<MouseDownEvent>(evt =>
        {
            if (evt.button == 0) // Left click
            {
                var graph = GetFirstAncestorOfType<SocialGraphView>();
                graph?.SetSelectedCitizen(this);
            }
        });
    }
}