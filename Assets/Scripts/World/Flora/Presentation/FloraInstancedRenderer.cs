using System;
using System.Collections.Generic;
using InfinityProject.World.Geography;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityProject.World.Flora.Presentation
{
    public sealed class FloraInstancedRenderer : IDisposable
    {
        private const int MaxBatchSize = 511;
        private const string PresentationShaderName = "InfinityProject/Flora/Instanced";

        private sealed class DrawPart
        {
            public Mesh Mesh;
            public int SubmeshIndex;
            public Material Material;
            public Matrix4x4 LocalMatrix;
        }

        private sealed class SpeciesVisual
        {
            public string StableId;
            public GameObject Prefab;
            public float MaxDistance;
            public int SampleStride;
            public int MaxInstances;
            public Vector2 ScaleRange;
            public ShadowCastingMode Shadows;
            public readonly List<DrawPart> Parts = new();
            public readonly List<DrawBatch> Batches = new();
        }

        private sealed class DrawBatch
        {
            public Matrix4x4[][] PartMatrices;
            public int Count;
            public Bounds Bounds;
        }

        private readonly List<SpeciesVisual> _species = new();
        private readonly Plane[] _frustumPlanes = new Plane[6];
        private readonly Terrain _terrain;
        private readonly GeographyData _geography;
        private readonly FloraSimulationData _flora;
        private Camera _camera;
        private bool _warnedAboutInvalidMaterial;

        public int InstanceCount { get; private set; }
        public int BatchCount { get; private set; }
        public bool Enabled { get; set; } = true;

        public FloraInstancedRenderer(
            Terrain terrain,
            GeographyData geography,
            FloraSimulationData flora,
            GameObject grassPrefab,
            GameObject shrubPrefab,
            GameObject coniferPrefab)
        {
            _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            _geography = geography ?? throw new ArgumentNullException(nameof(geography));
            _flora = flora ?? throw new ArgumentNullException(nameof(flora));

            AddSpecies("temperate_grass", grassPrefab, 170f, 8, 14000, new Vector2(0.75f, 1.25f), ShadowCastingMode.Off);
            AddSpecies("riparian_shrub", shrubPrefab, 360f, 8, 9000, new Vector2(0.75f, 1.45f), ShadowCastingMode.Off);
            AddSpecies("cold_tolerant_conifer", coniferPrefab, 850f, 10, 7000, new Vector2(0.8f, 1.35f), ShadowCastingMode.Off);

            Rebuild();
        }

        public void SetCamera(Camera camera) => _camera = camera;

        public void Rebuild()
        {
            InstanceCount = 0;
            BatchCount = 0;
            for (int i = 0; i < _species.Count; i++)
            {
                SpeciesVisual visual = _species[i];
                visual.Batches.Clear();
                BuildSpeciesInstances(visual, i);
                BatchCount += visual.Batches.Count;
            }
        }

        public void Draw()
        {
            if (!Enabled) return;

            Camera camera = _camera != null ? _camera : Camera.main;
            if (camera == null) return;
            GeometryUtility.CalculateFrustumPlanes(camera, _frustumPlanes);
            Vector3 cameraPosition = camera.transform.position;

            for (int s = 0; s < _species.Count; s++)
            {
                SpeciesVisual visual = _species[s];
                float maxDistanceSq = visual.MaxDistance * visual.MaxDistance;

                for (int b = 0; b < visual.Batches.Count; b++)
                {
                    DrawBatch batch = visual.Batches[b];
                    Vector3 closest = batch.Bounds.ClosestPoint(cameraPosition);
                    if ((closest - cameraPosition).sqrMagnitude > maxDistanceSq) continue;
                    if (!GeometryUtility.TestPlanesAABB(_frustumPlanes, batch.Bounds)) continue;

                    for (int p = 0; p < visual.Parts.Count; p++)
                    {
                        DrawPart part = visual.Parts[p];
                        if (!CanRender(part.Material) || part.Mesh == null) continue;

                        var renderParams = new RenderParams(part.Material)
                        {
                            camera = camera,
                            layer = 0,
                            lightProbeUsage = LightProbeUsage.Off,
                            receiveShadows = false,
                            shadowCastingMode = visual.Shadows,
                            worldBounds = batch.Bounds
                        };

                        Graphics.RenderMeshInstanced(
                            renderParams,
                            part.Mesh,
                            part.SubmeshIndex,
                            batch.PartMatrices[p],
                            batch.Count);
                    }
                }
            }
        }

        public void Dispose()
        {
            for (int s = 0; s < _species.Count; s++)
            {
                _species[s].Parts.Clear();
                _species[s].Batches.Clear();
            }
            _species.Clear();
        }

        private bool CanRender(Material material)
        {
            bool valid = material != null && material.shader != null &&
                         material.shader.name == PresentationShaderName && material.enableInstancing;
            if (valid || _warnedAboutInvalidMaterial) return valid;

            _warnedAboutInvalidMaterial = true;
            Debug.LogWarning(
                $"[Infinity Flora] Flora presentation was skipped because a prefab does not use the dedicated '{PresentationShaderName}' instancing shader. Rebuild Flora prefabs from Infinity/World/Rebuild Flora Prefabs.");
            return false;
        }

        private void AddSpecies(
            string stableId,
            GameObject prefab,
            float maxDistance,
            int sampleStride,
            int maxInstances,
            Vector2 scaleRange,
            ShadowCastingMode shadows)
        {
            if (prefab == null) return;

            var visual = new SpeciesVisual
            {
                StableId = stableId,
                Prefab = prefab,
                MaxDistance = maxDistance,
                SampleStride = Mathf.Max(1, sampleStride),
                MaxInstances = Mathf.Max(1, maxInstances),
                ScaleRange = scaleRange,
                Shadows = shadows
            };

            ExtractDrawParts(visual);
            if (visual.Parts.Count > 0) _species.Add(visual);
        }

        private static void ExtractDrawParts(SpeciesVisual visual)
        {
            GameObject temp = UnityEngine.Object.Instantiate(visual.Prefab);
            temp.SetActive(false);
            try
            {
                Matrix4x4 rootInverse = temp.transform.worldToLocalMatrix;
                MeshFilter[] filters = temp.GetComponentsInChildren<MeshFilter>(true);
                for (int i = 0; i < filters.Length; i++)
                {
                    MeshFilter filter = filters[i];
                    MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                    if (filter.sharedMesh == null || renderer == null) continue;

                    Material[] materials = renderer.sharedMaterials;
                    int submeshCount = Mathf.Min(filter.sharedMesh.subMeshCount, materials.Length);
                    for (int submesh = 0; submesh < submeshCount; submesh++)
                    {
                        Material material = materials[submesh];
                        if (material == null) continue;
                        visual.Parts.Add(new DrawPart
                        {
                            Mesh = filter.sharedMesh,
                            SubmeshIndex = submesh,
                            Material = material,
                            LocalMatrix = rootInverse * filter.transform.localToWorldMatrix
                        });
                    }
                }
            }
            finally
            {
                UnityEngine.Object.Destroy(temp);
            }
        }

        private void BuildSpeciesInstances(SpeciesVisual visual, int visualIndex)
        {
            int floraIndex = _flora.FindSpeciesIndex(visual.StableId);
            if (floraIndex < 0 || _flora.Species == null || _flora.Species[floraIndex] == null) return;

            float[] biomass = _flora.Species[floraIndex].CurrentBiomassKgPerSquareMeter;
            if (biomass == null || biomass.Length != _flora.Resolution * _flora.Resolution) return;

            float maxDensity = 0f;
            for (int i = 0; i < biomass.Length; i++) maxDensity = Mathf.Max(maxDensity, biomass[i]);
            if (maxDensity <= 0.000001f) return;

            var matrices = new List<Matrix4x4>(Mathf.Min(visual.MaxInstances, 4096));
            int resolution = _flora.Resolution;
            int stride = visual.SampleStride;

            for (int y = 0; y < resolution && matrices.Count < visual.MaxInstances; y += stride)
            {
                for (int x = 0; x < resolution && matrices.Count < visual.MaxInstances; x += stride)
                {
                    int index = y * resolution + x;
                    float density01 = Mathf.Clamp01(biomass[index] / maxDensity);
                    if (density01 <= 0.015f) continue;

                    uint hash = Hash((uint)(index + 1), (uint)(visualIndex + 17));
                    float roll = (hash & 0x00FFFFFFu) / 16777215f;
                    float chance = Mathf.Pow(density01, 0.72f) * 0.92f;
                    if (roll > chance) continue;

                    float jitterX = Hash01(hash ^ 0x9E3779B9u) - 0.5f;
                    float jitterZ = Hash01(hash ^ 0x85EBCA6Bu) - 0.5f;
                    float sampleX = Mathf.Clamp(x + jitterX * stride, 0f, resolution - 1f);
                    float sampleY = Mathf.Clamp(y + jitterZ * stride, 0f, resolution - 1f);
                    Vector2 localXZ = new(
                        sampleX / (resolution - 1f) * _geography.WidthMeters,
                        sampleY / (resolution - 1f) * _geography.LengthMeters);
                    float elevation = _geography.SampleElevationMeters(localXZ);
                    Vector3 localPosition = new(localXZ.x, elevation, localXZ.y);
                    Vector3 worldPosition = _terrain.transform.TransformPoint(localPosition);

                    float yaw = Hash01(hash ^ 0xC2B2AE35u) * 360f;
                    float scale = Mathf.Lerp(visual.ScaleRange.x, visual.ScaleRange.y, Hash01(hash ^ 0x27D4EB2Fu));
                    Quaternion rotation = Quaternion.AngleAxis(yaw, _terrain.transform.up);
                    matrices.Add(Matrix4x4.TRS(worldPosition, rotation, Vector3.one * scale));
                }
            }

            InstanceCount += matrices.Count;
            for (int start = 0; start < matrices.Count; start += MaxBatchSize)
            {
                int count = Mathf.Min(MaxBatchSize, matrices.Count - start);
                var batchMatrices = new Matrix4x4[count];
                Vector3 min = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                Vector3 max = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

                for (int i = 0; i < count; i++)
                {
                    Matrix4x4 matrix = matrices[start + i];
                    batchMatrices[i] = matrix;
                    Vector4 column = matrix.GetColumn(3);
                    Vector3 position = new(column.x, column.y, column.z);
                    min = Vector3.Min(min, position);
                    max = Vector3.Max(max, position);
                }

                Vector3 center = (min + max) * 0.5f;
                Vector3 size = (max - min) + Vector3.one * 12f;
                var partMatrices = new Matrix4x4[visual.Parts.Count][];
                for (int p = 0; p < visual.Parts.Count; p++)
                {
                    partMatrices[p] = new Matrix4x4[count];
                    Matrix4x4 local = visual.Parts[p].LocalMatrix;
                    for (int i = 0; i < count; i++) partMatrices[p][i] = batchMatrices[i] * local;
                }

                visual.Batches.Add(new DrawBatch
                {
                    PartMatrices = partMatrices,
                    Count = count,
                    Bounds = new Bounds(center, size)
                });
            }
        }

        private static uint Hash(uint a, uint b)
        {
            uint x = a * 0x9E3779B9u + b * 0x85EBCA6Bu + 0xC2B2AE35u;
            x ^= x >> 16;
            x *= 0x7FEB352Du;
            x ^= x >> 15;
            x *= 0x846CA68Bu;
            x ^= x >> 16;
            return x;
        }

        private static float Hash01(uint value)
        {
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return (value & 0x00FFFFFFu) / 16777215f;
        }
    }
}
