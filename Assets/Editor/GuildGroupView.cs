using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using UnityEngine;

/// <summary>
/// Represents a semi-transparent background for a group of citizens in the same guild.
/// Automatically sizes to fit children.
/// </summary>
public class GuildGroupView : GraphElement
{
    private string guildName;

    public GuildGroupView(string guildName, Color backgroundColor)
    {
        this.guildName = guildName;
        AddToClassList("guild-area");

        // Apply guild-specific color
        style.backgroundColor = new StyleColor(backgroundColor);

        // Don't block mouse events for underlying nodes
        pickingMode = PickingMode.Ignore;

        // Optional: tooltip for name
        tooltip = $"Gremio: {guildName}";
    }

    public void SetBounds(Rect bounds)
    {
        style.left = bounds.xMin;
        style.top = bounds.yMin;
        style.width = bounds.width;
        style.height = bounds.height;
    }
}
