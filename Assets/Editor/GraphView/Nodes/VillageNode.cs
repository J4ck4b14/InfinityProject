using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

public class VillageNode : Node
{
    public Port relationshipInput;
    public Port relationshipOutput;

    public Port commerceInput;
    public Port commerceOutput;

    public VillageNode(string villageName)
    {
        title = villageName;

        style.width = 180;
        style.height = 100;
        style.backgroundColor = new StyleColor(new Color(0.12f, 0.12f, 0.12f));

        // Style title
        titleContainer.style.justifyContent = Justify.Center;
        titleContainer.style.alignItems = Align.Center;
        foreach (var label in titleContainer.Children())
        {
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.fontSize = 13;
            label.style.color = Color.white;
        }

        // Trust (float) ports
        relationshipInput = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(float));
        relationshipInput.portName = "Rel. In";
        inputContainer.Add(relationshipInput);

        relationshipOutput = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(float));
        relationshipOutput.portName = "Rel. Out";
        outputContainer.Add(relationshipOutput);

        // Commerce (string) ports
        commerceInput = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(string));
        commerceInput.portName = "Trade In";
        inputContainer.Add(commerceInput);

        commerceOutput = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(string));
        commerceOutput.portName = "Trade Out";
        outputContainer.Add(commerceOutput);

        relationshipInput.portColor = Color.cyan;
        relationshipOutput.portColor = Color.cyan;
        commerceInput.portColor = new Color(1f, 0.8f, 0.1f);   // warm yellow
        commerceOutput.portColor = new Color(1f, 0.8f, 0.1f);

        RefreshExpandedState();
        RefreshPorts();
    }

    public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
    {
        base.BuildContextualMenu(evt);

        if (parent is not GraphView graphView || graphView is not SocialGraphView socialGraph)
            return;

        evt.menu.AppendSeparator();

        foreach (var other in socialGraph.GetAllVillageNodes())
        {
            if (other == this) continue;

            string otherName = other.title;
            for (float t = -0.5f; t <= 0.5f; t += 0.25f)
            {
                float trust = Mathf.Round(t * 100f) / 100f;
                string label = $"Set relationship with {otherName} to {trust:+0.00;-0.00}";
                evt.menu.AppendAction(label, _ =>
                {
                    socialGraph.UpdateVillageRelationship(this, other, trust);
                });
            }
        }
    }

    private void InjectVillageTrustMenu(DropdownMenu menu)
    {
        if (parent is not GraphView graphView || graphView is not SocialGraphView socialGraph)
            return;

        menu.AppendSeparator();

        foreach (var other in socialGraph.GetAllVillageNodes())
        {
            if (other == this) continue;

            string targetName = other.title;
            for (float t = -0.5f; t <= 0.5f; t += 0.25f)
            {
                float trust = (float)Mathf.Round(t * 100f) / 100f;
                string label = $"Set relationship with {targetName} to {trust:+0.00;-0.00}";
                menu.AppendAction(label, _ =>
                {
                    socialGraph.UpdateVillageRelationship(this, other, trust);
                });
            }
        }
    }

}
