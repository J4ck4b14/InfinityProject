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

    private VisualElement _overlay;

    public void ApplyOverlayVisual(GraphView graphView)
    {
        if (_overlay != null)
        {
            _overlay.RemoveFromHierarchy();
            _overlay = null;
        }

        _overlay = new VisualElement
        {
            pickingMode = PickingMode.Ignore,
            name = "edge-overlay"
        };

        _overlay.style.position = Position.Absolute;
        _overlay.style.backgroundColor = edgeColor;
        _overlay.style.height = edgeThickness + 1;
        _overlay.style.borderTopLeftRadius = 2;
        _overlay.style.borderTopRightRadius = 2;
        _overlay.style.overflow = Overflow.Hidden;

        graphView.contentViewContainer.Add(_overlay);

        void RepositionOverlay()
        {
            if (output == null || input == null || _overlay == null)
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
                _overlay.visible = false;
                return;
            }

            _overlay.visible = true;
            _overlay.style.left = fromLocal.x;
            _overlay.style.top = fromLocal.y;
            _overlay.style.width = length;

            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            _overlay.transform.rotation = Quaternion.Euler(0, 0, angle);
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
