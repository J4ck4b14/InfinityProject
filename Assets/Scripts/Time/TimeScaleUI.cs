using InfinityProject.Time;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Attach to any persistent GameObject.
/// Keyboard shortcuts 1–5. Draws a toolbar in the top-right corner.
/// </summary>
public class TimeScaleUI : MonoBehaviour
{
    private static readonly string[] ModeNames =
    {
        "Slow (0.5×)",
        "1:1 real time",
        "5 min / year",
        "5 min / decade",
        "1 min / year",
    };

    private int   _modeIndex = 1;
    private World _world;

    private const float ButtonW = 130f;
    private const float ButtonH = 30f;
    private const float PadX    = 10f;
    private const float PadY    = 10f;

    private GUIStyle _activeStyle;
    private GUIStyle _normalStyle;
    private bool     _stylesReady;

    private void Start() => ApplyScale(_modeIndex);

    private void Update()
    {
        for (int i = 0; i < ModeNames.Length; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            { SetMode(i); break; }
        }
    }

    private void OnGUI()
    {
        EnsureStyles();

        float totalW = ModeNames.Length * ButtonW + (ModeNames.Length - 1) * 4f;
        float startX = Screen.width - totalW - PadX;
        float startY = PadY;

        GUI.Box(new Rect(startX - 6, startY - 4, totalW + 12, ButtonH + 8), GUIContent.none);

        for (int i = 0; i < ModeNames.Length; i++)
        {
            float x = startX + i * (ButtonW + 4f);
            if (GUI.Button(new Rect(x, startY, ButtonW, ButtonH),
                $"{i + 1}. {ModeNames[i]}",
                _modeIndex == i ? _activeStyle : _normalStyle))
                SetMode(i);
        }

        double scale = TimeConfig.ScaleValues[_modeIndex];
        GUI.Label(new Rect(startX, startY + ButtonH + 6, totalW, 20),
            $"Scale: {scale:N1} game-s / real-s  —  {ModeNames[_modeIndex]}");
    }

    private void SetMode(int index)
    {
        _modeIndex = Mathf.Clamp(index, 0, ModeNames.Length - 1);
        ApplyScale(_modeIndex);
    }

    private void ApplyScale(int index)
    {
        if (!TryGetWorld()) return;

        var em    = _world.EntityManager;
        var query = em.CreateEntityQuery(ComponentType.ReadWrite<GameTime>());
        if (query.CalculateEntityCount() == 0) { query.Dispose(); return; }

        using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
        var gt = em.GetComponentData<GameTime>(entities[0]);
        gt.ScaleIndex = (byte)index;
        em.SetComponentData(entities[0], gt);

        query.Dispose();
    }

    private bool TryGetWorld()
    {
        if (_world != null && _world.IsCreated) return true;
        foreach (var w in World.All)
            if (w.IsCreated && w.Name == "Default World")
            { _world = w; return true; }
        return false;
    }

    private void EnsureStyles()
    {
        if (_stylesReady) return;
        _stylesReady = true;

        _normalStyle = new GUIStyle(GUI.skin.button) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
        _activeStyle = new GUIStyle(_normalStyle);
        _activeStyle.normal.background = MakeTex(2, 2, new Color(0.2f, 0.6f, 1f, 0.9f));
        _activeStyle.hover.background  = _activeStyle.normal.background;
        _activeStyle.normal.textColor  = Color.white;
        _activeStyle.fontStyle         = FontStyle.Bold;
    }

    private static Texture2D MakeTex(int w, int h, Color col)
    {
        var t = new Texture2D(w, h);
        var p = new Color[w * h];
        for (int i = 0; i < p.Length; i++) p[i] = col;
        t.SetPixels(p); t.Apply();
        return t;
    }
}
