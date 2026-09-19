using InfinityProject.World.Fauna;
using InfinityProject.World.Flora;
using UnityEditor;
using UnityEngine;

public sealed class FaunaDebugWindow : EditorWindow
{
    private FloraData _floraPotential;
    private FloraSimulationData _floraState;
    private FaunaSimulationData _fauna;
    private FaunaGenerationSettings _settings;
    private Texture2D _texture;
    private int _selectedAgent;
    private Vector2 _scroll;

    public static FaunaDebugWindow Open(
        FloraData floraPotential,
        FloraSimulationData floraState,
        FaunaSimulationData fauna,
        FaunaGenerationSettings settings)
    {
        var window = GetWindow<FaunaDebugWindow>("Fauna Inspector");
        window.minSize = new Vector2(430f, 500f);
        window.SetData(floraPotential, floraState, fauna, settings);
        window.Show();
        return window;
    }

    public void SetData(
        FloraData floraPotential,
        FloraSimulationData floraState,
        FaunaSimulationData fauna,
        FaunaGenerationSettings settings)
    {
        _floraPotential = floraPotential;
        _floraState = floraState;
        _fauna = fauna;
        _settings = settings?.Clone() ?? new FaunaGenerationSettings();
        _selectedAgent = fauna != null && fauna.AgentCount > 0 ? Mathf.Clamp(_selectedAgent, 0, fauna.AgentCount - 1) : 0;
        RebuildTexture();
        Repaint();
    }

    private void OnDisable() => DestroyTexture();

    private void OnGUI()
    {
        if (_floraPotential == null || _floraState == null || _fauna == null)
        {
            EditorGUILayout.HelpBox("Initialize living Flora and Fauna first.", MessageType.Info);
            return;
        }

        if (_fauna.Agents == null)
        {
            EditorGUILayout.HelpBox("Fauna inspector data expired after a script/play-mode reload. Reopen the inspector from Ecology Lab.", MessageType.Info);
            return;
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Infinity Fauna — deer consumer", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"{_fauna.SpeciesDisplayName} | day {_fauna.SimulationTimeDays:0.00} | alive {_fauna.AliveCount}/{_fauna.AgentCount}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"Mean hunger {_fauna.MeanHunger01:0.00} | mean health {_fauna.MeanHealth01:0.00} | consumed {_fauna.CumulativeFoodConsumedKg:N1} kg", EditorStyles.miniLabel);

        if (_fauna.AgentCount > 0)
        {
            _selectedAgent = EditorGUILayout.IntSlider("Inspect agent", _selectedAgent, 0, _fauna.AgentCount - 1);
            FaunaAgentState a = _fauna.Agents[_selectedAgent];
            EditorGUILayout.LabelField($"#{a.Id} herd {a.HerdId} | {(a.Alive ? "alive" : "dead")} | XZ {a.PositionLocalMeters.x:0.0}, {a.PositionLocalMeters.y:0.0} m", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Hunger {a.Hunger01:0.00} | energy {a.Energy01:0.00} | health {a.Health01:0.00} | eaten {a.CumulativeFoodConsumedKg:0.0} kg", EditorStyles.miniLabel);
            float localPreferredFoodKg = FaunaSimulator.EstimatePreferredFoodAvailableKg(_floraState, _settings.Species, a.PositionLocalMeters);
            EditorGUILayout.LabelField(
                $"Browse footprint preferred food ~{localPreferredFoodKg:0.0} kg | daily need {_settings.Species.DailyFoodRequirementKg:0.0} kg",
                EditorStyles.miniLabel);
        }

        if (GUILayout.Button("Refresh Map")) RebuildTexture();
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        DrawMap();
        EditorGUILayout.EndScrollView();
        EditorGUILayout.HelpBox("Background = live biomass of the first edible Flora species. Deer are yellow when well-fed, orange/red as hunger rises; grey points are dead agents. GameObjects are not simulation truth.", MessageType.None);
    }

    private void DrawMap()
    {
        if (_texture == null) return;
        float width = Mathf.Max(position.width - 24f, 64f);
        float height = width * (_floraState.LengthMeters / _floraState.WidthMeters);
        Rect rect = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(true));
        EditorGUI.DrawPreviewTexture(rect, _texture, null, ScaleMode.StretchToFill);
    }

    private void RebuildTexture()
    {
        DestroyTexture();
        if (_floraState == null || _fauna == null || _fauna.Agents == null) return;
        int res = _floraState.Resolution;
        _texture = new Texture2D(res, res, TextureFormat.RGB24, false, true)
        {
            name = "Infinity Fauna Debug",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        var pixels = new Color32[res * res];

        int backgroundSpecies = -1;
        if (_settings?.Species?.Diet != null)
        {
            for (int i = 0; i < _settings.Species.Diet.Count; i++)
            {
                backgroundSpecies = _floraState.FindSpeciesIndex(_settings.Species.Diet[i].FloraSpeciesStableId);
                if (backgroundSpecies >= 0) break;
            }
        }
        int potentialSpeciesIndex = backgroundSpecies >= 0
            ? _floraPotential.FindSpeciesIndex(_floraState.Species[backgroundSpecies].StableId)
            : -1;
        FloraSpeciesData potentialSpecies = potentialSpeciesIndex >= 0
            ? _floraPotential.Species[potentialSpeciesIndex]
            : null;
        for (int i = 0; i < pixels.Length; i++)
        {
            float ratio = 0f;
            if (backgroundSpecies >= 0 && potentialSpecies != null)
            {
                float k = potentialSpecies.CarryingCapacityKgPerSquareMeter[i];
                ratio = k > 0.000001f ? Mathf.Clamp01(_floraState.Species[backgroundSpecies].CurrentBiomassKgPerSquareMeter[i] / k) : 0f;
            }
            Color baseColor = Color.Lerp(new Color(0.025f, 0.025f, 0.025f, 1f), new Color(0.05f, 0.28f, 0.07f, 1f), ratio);
            pixels[i] = (Color32)baseColor;
        }

        for (int i = 0; i < _fauna.Agents.Length; i++)
        {
            FaunaAgentState agent = _fauna.Agents[i];
            int x = Mathf.Clamp(Mathf.RoundToInt(agent.PositionLocalMeters.x / _floraState.WidthMeters * (res - 1)), 0, res - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(agent.PositionLocalMeters.y / _floraState.LengthMeters * (res - 1)), 0, res - 1);
            Color32 color = agent.Alive
                ? (Color32)Color.Lerp(new Color(1f, 0.92f, 0.1f, 1f), new Color(1f, 0.08f, 0.03f, 1f), agent.Hunger01)
                : new Color32(140, 140, 140, 255);
            int radius = res >= 512 ? 3 : 2;
            for (int oy = -radius; oy <= radius; oy++)
            for (int ox = -radius; ox <= radius; ox++)
            {
                if (ox * ox + oy * oy > radius * radius) continue;
                int px = x + ox;
                int py = y + oy;
                if (px < 0 || py < 0 || px >= res || py >= res) continue;
                pixels[py * res + px] = color;
            }
        }

        _texture.SetPixels32(pixels);
        _texture.Apply(false, true);
    }

    private void DestroyTexture()
    {
        if (_texture != null) DestroyImmediate(_texture);
        _texture = null;
    }
}
