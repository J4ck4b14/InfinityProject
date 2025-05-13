using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using UnityEngine;

/// <summary>
/// Represents a single village node in the social graph.
/// Includes input/output ports for edges.
/// </summary>
public class VillageNode : Node
{
    public Port inputPort;
    public Port outputPort;

    public VillageNode(string title)
    {
        this.title = title;

        // Add input and output ports for connection
        inputPort = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(float));
        inputPort.portName = "Entradas";
        outputPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(float));
        outputPort.portName = "Salidas";

        inputContainer.Add(inputPort);
        outputContainer.Add(outputPort);

        RefreshExpandedState();
        RefreshPorts();
    }
}
