using System;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

public class ColoredEdge : Edge
{
    public Color edgeColor = Color.gray;
    public float edgeThickness = 2f;
    public bool dashed = false;

    private VisualElement overlay;

    public void ApplyOverlayVisual(GraphView graphView)
    {
        if (overlay != null)
        {
            overlay.RemoveFromHierarchy();
            overlay = null;
        }

        overlay = new VisualElement
        {
            pickingMode = PickingMode.Ignore,
            name = "edge-overlay"
        };

        overlay.style.position = Position.Absolute;
        overlay.style.backgroundColor = edgeColor;
        overlay.style.height = edgeThickness;
        overlay.style.transformOrigin = new StyleTransformOrigin(new TransformOrigin(0, 0));
        overlay.style.borderTopLeftRadius = 2;
        overlay.style.borderTopRightRadius = 2;
        overlay.style.overflow = Overflow.Hidden;
        overlay.style.height = edgeThickness + 1;

        graphView.contentViewContainer.Add(overlay);

        void RepositionOverlay()
        {
            if (output == null || input == null || overlay == null)
                return;

            // Get edge-aligned anchors, not center points
            var fromWorld = new Vector2(output.worldBound.xMax, output.worldBound.center.y); // right edge of output port
            var toWorld = new Vector2(input.worldBound.xMin, input.worldBound.center.y);     // left edge of input port

            var fromLocal = graphView.contentViewContainer.WorldToLocal(fromWorld);
            var toLocal = graphView.contentViewContainer.WorldToLocal(toWorld);

            var dir = toLocal - fromLocal;
            float length = dir.magnitude;

            if (dir.sqrMagnitude < 0.001f)
            {
                overlay.visible = false;
                return;
            }

            overlay.visible = true;
            overlay.style.left = fromLocal.x;
            overlay.style.top = fromLocal.y;
            overlay.style.width = length;

            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            overlay.transform.rotation = Quaternion.Euler(0, 0, angle);
        }

        graphView.schedule.Execute(RepositionOverlay).Every(16);
    }

    public void EnableInteractiveTrustChange(GraphView graphView, Action<float> onValueChanged)
    {
        RegisterCallback<MouseUpEvent>(evt =>
        {
            if (evt.button != 1) return; // Right-click only

            var menu = new GenericMenu();

            // Example: 5 trust levels
            menu.AddItem(new GUIContent("Trust: Strong Hostility"), false, () => onValueChanged?.Invoke(-1f));
            menu.AddItem(new GUIContent("Trust: Dislike"), false, () => onValueChanged?.Invoke(-0.5f));
            menu.AddItem(new GUIContent("Trust: Neutral"), false, () => onValueChanged?.Invoke(0f));
            menu.AddItem(new GUIContent("Trust: Friendly"), false, () => onValueChanged?.Invoke(0.5f));
            menu.AddItem(new GUIContent("Trust: Strong Alliance"), false, () => onValueChanged?.Invoke(1f));

            menu.ShowAsContext();
        });
    }
}
