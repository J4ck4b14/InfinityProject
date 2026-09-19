using System;
using InfinityProject.World.Ecology;
using InfinityProject.World.Flora;
using InfinityProject.World.Flora.Presentation;
using InfinityProject.World.Geography;
using InfinityProject.World.Ground;
using InfinityProject.World.Hydrology;
using InfinityProject.World.Hydrology.Presentation;
using UnityEngine;

namespace InfinityProject.World.Presentation
{
    [DisallowMultipleComponent]
    public sealed class InfinityWorldPresentationController : MonoBehaviour
    {
        private static readonly int TerrainBaseYId = Shader.PropertyToID("_TerrainBaseY");
        private static readonly int TerrainHeightId = Shader.PropertyToID("_TerrainHeight");
        private static readonly int WorldDataTexId = Shader.PropertyToID("_WorldDataTex");
        private static readonly int WorldDataEnabledId = Shader.PropertyToID("_WorldDataEnabled");
        private static readonly int SandTexId = Shader.PropertyToID("_SandTex");
        private static readonly int GrassTexId = Shader.PropertyToID("_GrassTex");
        private static readonly int RockTexId = Shader.PropertyToID("_RockTex");
        private static readonly int SandTexEnabledId = Shader.PropertyToID("_SandTexEnabled");
        private static readonly int GrassTexEnabledId = Shader.PropertyToID("_GrassTexEnabled");
        private static readonly int RockTexEnabledId = Shader.PropertyToID("_RockTexEnabled");

        private Terrain _terrain;
        private GeographyData _geography;
        private GroundConditionData _ground;
        private HydrologyData _hydrology;
        private FloraSimulationData _flora;
        private InfinityLiveEcologyController _ecology;
        private Material _terrainMaterial;
        private Material _originalTerrainMaterial;
        private Texture2D _worldDataTexture;
        private HydrologyRiverPresenter _rivers;
        private FloraInstancedRenderer _floraRenderer;
        private bool _initialized;
        private bool _floraVisualDirty;
        private float _nextFloraVisualRefreshTime;
        private Color32[] _worldDataPixels;
        private bool _terrainPresentationEnabled = true;
        private bool _originalTerrainDrawInstanced;
        private bool _riverPresentationEnabled = true;
        private bool _floraPresentationEnabled = true;

        public int RiverSegments => _rivers?.SegmentCount ?? 0;
        public int RiverChunks => _rivers?.ChunkCount ?? 0;
        public int FloraInstances => _floraRenderer?.InstanceCount ?? 0;
        public int FloraBatches => _floraRenderer?.BatchCount ?? 0;
        public bool TerrainPresentationEnabled => _terrainPresentationEnabled;
        public bool RiverPresentationEnabled => _riverPresentationEnabled;
        public bool FloraPresentationEnabled => _floraPresentationEnabled;

        public void Initialize(
            Terrain terrain,
            GeographyData geography,
            GroundConditionData ground,
            HydrologyData hydrology,
            FloraSimulationData flora,
            GameObject grassPrefab,
            GameObject shrubPrefab,
            GameObject coniferPrefab,
            Texture2D soilTexture,
            Texture2D grassTexture,
            Texture2D rockTexture,
            Material terrainSourceMaterial,
            Material riverSourceMaterial,
            InfinityLiveEcologyController ecology)
        {
            Shutdown();
            _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            _geography = geography ?? throw new ArgumentNullException(nameof(geography));
            _ground = ground;
            _hydrology = hydrology;
            _flora = flora ?? throw new ArgumentNullException(nameof(flora));
            _ecology = ecology;
            _originalTerrainDrawInstanced = _terrain.drawInstanced;

            // The current custom Terrain material is intentionally rendered through the
            // non-instanced Terrain path. Unity's Terrain instancing expects terrain-specific
            // shader support; forcing it on with a generic custom pass can produce undefined
            // object matrices and full-screen geometry corruption. Flora has its own GPU
            // instancing path and is unaffected by this setting.
            _terrain.drawInstanced = false;

            BuildTerrainMaterial(soilTexture, grassTexture, rockTexture, terrainSourceMaterial);
            RebuildWorldDataTexture();

            if (_hydrology != null)
            {
                _rivers = new HydrologyRiverPresenter(_terrain.transform, _geography, _hydrology, riverSourceMaterial);
                _rivers.SetVisible(_riverPresentationEnabled);
            }

            _floraRenderer = new FloraInstancedRenderer(
                _terrain,
                _geography,
                _flora,
                grassPrefab,
                shrubPrefab,
                coniferPrefab)
            {
                Enabled = _floraPresentationEnabled
            };
            _floraRenderer.SetCamera(Camera.main);

            if (_ecology != null) _ecology.FloraStateChanged += OnFloraStateChanged;
            ApplyTerrainVisibility();
            _initialized = true;
        }

        public void SetTerrainPresentationEnabled(bool enabled)
        {
            _terrainPresentationEnabled = enabled;
            ApplyTerrainVisibility();
        }

        public void SetRiverPresentationEnabled(bool enabled)
        {
            _riverPresentationEnabled = enabled;
            _rivers?.SetVisible(enabled);
        }

        public void SetFloraPresentationEnabled(bool enabled)
        {
            _floraPresentationEnabled = enabled;
            if (_floraRenderer != null) _floraRenderer.Enabled = enabled;
        }

        private void LateUpdate()
        {
            if (!_initialized) return;
            if (_floraVisualDirty && Time.unscaledTime >= _nextFloraVisualRefreshTime)
            {
                _floraVisualDirty = false;
                _nextFloraVisualRefreshTime = Time.unscaledTime + 10f;
                RebuildWorldDataTexture();
                _floraRenderer?.Rebuild();
            }

            if (_riverPresentationEnabled) _rivers?.Tick(Time.unscaledTime);
            _floraRenderer?.Draw();
        }

        private void OnDisable() => Shutdown();
        private void OnDestroy() => Shutdown();

        private void OnFloraStateChanged()
        {
            _floraVisualDirty = true;
        }

        private void BuildTerrainMaterial(Texture2D soilTexture, Texture2D grassTexture, Texture2D rockTexture, Material sourceMaterial)
        {
            Shader shader = Shader.Find("InfinityProject/Terrain/Surface");
            if (shader == null)
            {
                Debug.LogWarning("[Infinity Presentation] Terrain surface shader was not found.");
                return;
            }

            _originalTerrainMaterial = _terrain.materialTemplate;
            _terrainMaterial = sourceMaterial != null
                ? new Material(sourceMaterial) { name = "Infinity_Terrain_Runtime" }
                : new Material(shader) { name = "Infinity_Terrain_Runtime" };

            if (_terrainMaterial.shader != shader) _terrainMaterial.shader = shader;
            _terrainMaterial.SetFloat(TerrainBaseYId, _terrain.transform.position.y);
            _terrainMaterial.SetFloat(TerrainHeightId, _terrain.terrainData != null ? _terrain.terrainData.size.y : _geography.MaxElevationMeters);
            _terrainMaterial.SetFloat("_TexScale", 7f);
            _terrainMaterial.SetFloat("_DetailStrength", 0.045f);
            _terrainMaterial.SetFloat("_DetailScale", 28f);
            _terrainMaterial.SetFloat(WorldDataEnabledId, 1f);

            if (soilTexture != null)
            {
                _terrainMaterial.SetTexture(SandTexId, soilTexture);
                _terrainMaterial.SetFloat(SandTexEnabledId, 1f);
            }
            if (grassTexture != null)
            {
                _terrainMaterial.SetTexture(GrassTexId, grassTexture);
                _terrainMaterial.SetFloat(GrassTexEnabledId, 1f);
            }
            if (rockTexture != null)
            {
                _terrainMaterial.SetTexture(RockTexId, rockTexture);
                _terrainMaterial.SetFloat(RockTexEnabledId, 1f);
            }

            ApplyTerrainVisibility();
        }

        private void ApplyTerrainVisibility()
        {
            if (_terrain == null) return;

            bool useInfinityMaterial = _terrainPresentationEnabled && _terrainMaterial != null;
            _terrain.materialTemplate = useInfinityMaterial ? _terrainMaterial : _originalTerrainMaterial;

            // Unity Terrain's instanced draw path requires a terrain-instancing-aware shader.
            // Until the Infinity terrain presentation moves to a native Terrain/Lit splatmap
            // pipeline, keep the custom material on Terrain's stable non-instanced path.
            _terrain.drawInstanced = useInfinityMaterial ? false : _originalTerrainDrawInstanced;
        }

        private void RebuildWorldDataTexture()
        {
            if (_terrainMaterial == null || _flora == null || _flora.SpeciesCount <= 0 || _flora.Species == null) return;

            int size = Mathf.Min(512, _flora.Resolution);
            if (_worldDataTexture == null || _worldDataTexture.width != size || _worldDataTexture.height != size)
            {
                if (_worldDataTexture != null) Destroy(_worldDataTexture);
                _worldDataTexture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
                {
                    name = "Infinity_WorldData_Runtime",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            int sourceResolution = _flora.Resolution;
            float maxBiomass = 0f;
            for (int y = 0; y < size; y++)
            {
                int sy = Mathf.RoundToInt((float)y / Mathf.Max(1, size - 1) * (sourceResolution - 1));
                for (int x = 0; x < size; x++)
                {
                    int sx = Mathf.RoundToInt((float)x / Mathf.Max(1, size - 1) * (sourceResolution - 1));
                    int sourceIndex = sy * sourceResolution + sx;
                    for (int species = 0; species < _flora.SpeciesCount; species++)
                    {
                        FloraSimulationSpeciesState state = _flora.Species[species];
                        if (state?.CurrentBiomassKgPerSquareMeter == null || sourceIndex >= state.CurrentBiomassKgPerSquareMeter.Length) continue;
                        maxBiomass = Mathf.Max(maxBiomass, state.CurrentBiomassKgPerSquareMeter[sourceIndex]);
                    }
                }
            }
            maxBiomass = Mathf.Max(maxBiomass, 0.000001f);

            float maxFlow = 1f;
            if (_hydrology?.FlowAccumulationSquareMeters != null)
            {
                for (int i = 0; i < _hydrology.FlowAccumulationSquareMeters.Length; i++)
                    maxFlow = Mathf.Max(maxFlow, _hydrology.FlowAccumulationSquareMeters[i]);
            }

            if (_worldDataPixels == null || _worldDataPixels.Length != size * size)
                _worldDataPixels = new Color32[size * size];
            Color32[] colors = _worldDataPixels;
            for (int y = 0; y < size; y++)
            {
                int sy = Mathf.RoundToInt((float)y / Mathf.Max(1, size - 1) * (sourceResolution - 1));
                for (int x = 0; x < size; x++)
                {
                    int sx = Mathf.RoundToInt((float)x / Mathf.Max(1, size - 1) * (sourceResolution - 1));
                    int sourceIndex = sy * sourceResolution + sx;

                    float wetness = _ground?.WaterAvailabilityPotential != null && sourceIndex < _ground.WaterAvailabilityPotential.Length
                        ? Mathf.Clamp01(_ground.WaterAvailabilityPotential[sourceIndex])
                        : 0.5f;

                    float biomass = 0f;
                    for (int s = 0; s < _flora.SpeciesCount; s++)
                    {
                        FloraSimulationSpeciesState state = _flora.Species[s];
                        if (state?.CurrentBiomassKgPerSquareMeter == null || sourceIndex >= state.CurrentBiomassKgPerSquareMeter.Length) continue;
                        biomass = Mathf.Max(biomass, state.CurrentBiomassKgPerSquareMeter[sourceIndex]);
                    }
                    biomass = Mathf.Clamp01(biomass / maxBiomass);

                    float flow = 0f;
                    if (_hydrology?.FlowAccumulationSquareMeters != null && sourceIndex < _hydrology.FlowAccumulationSquareMeters.Length)
                    {
                        float accumulation = Mathf.Max(0f, _hydrology.FlowAccumulationSquareMeters[sourceIndex]);
                        flow = Mathf.Clamp01(Mathf.Log10(accumulation + 1f) / Mathf.Log10(maxFlow + 1f));
                    }

                    float retention = _ground?.SoilRetentionPotential != null && sourceIndex < _ground.SoilRetentionPotential.Length
                        ? Mathf.Clamp01(_ground.SoilRetentionPotential[sourceIndex])
                        : 0.5f;

                    colors[y * size + x] = new Color(wetness, biomass, flow, retention);
                }
            }

            _worldDataTexture.SetPixels32(colors);
            _worldDataTexture.Apply(false, false);
            _terrainMaterial.SetTexture(WorldDataTexId, _worldDataTexture);
            _terrainMaterial.SetFloat(WorldDataEnabledId, 1f);
        }

        private void Shutdown()
        {
            if (_ecology != null) _ecology.FloraStateChanged -= OnFloraStateChanged;
            _rivers?.Dispose();
            _rivers = null;
            _floraRenderer?.Dispose();
            _floraRenderer = null;

            if (_terrain != null)
            {
                if (_terrainMaterial != null && _terrain.materialTemplate == _terrainMaterial)
                    _terrain.materialTemplate = _originalTerrainMaterial;
                _terrain.drawInstanced = _originalTerrainDrawInstanced;
            }
            if (_terrainMaterial != null) Destroy(_terrainMaterial);
            if (_worldDataTexture != null) Destroy(_worldDataTexture);

            _terrainMaterial = null;
            _worldDataTexture = null;
            _ecology = null;
            _worldDataPixels = null;
            _floraVisualDirty = false;
            _initialized = false;
        }
    }
}
