using System;
using System.Collections.Generic;
using InfinityProject.World.Geography;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityProject.World.Hydrology.Presentation
{
    public sealed class HydrologyRiverPresenter : IDisposable
    {
        private const float SurfaceOffsetMeters = 0.10f;
        private const float MaxRenderableConditionedDepthMeters = 0.35f;
        private const float UvMetersPerRepeat = 18f;

        private static readonly int WaterTimeId = Shader.PropertyToID("_WaterTime");
        private static readonly int FlowSpeedId = Shader.PropertyToID("_FlowSpeed");
        private static readonly int FlowTurbulenceId = Shader.PropertyToID("_FlowTurbulence");
        private static readonly int ShallowColorId = Shader.PropertyToID("_ShallowColor");
        private static readonly int DeepColorId = Shader.PropertyToID("_DeepColor");
        private static readonly int FoamDistanceId = Shader.PropertyToID("_FoamDistance");

        private readonly Transform _root;
        private readonly Material _material;
        private GameObject _riverObject;
        private Mesh _mesh;

        public int SegmentCount { get; private set; }
        public int ChunkCount => _riverObject != null ? 1 : 0;
        public float ChannelThresholdSquareMeters { get; }

        public HydrologyRiverPresenter(
            Transform parent,
            GeographyData geography,
            HydrologyData hydrology,
            Material sourceMaterial = null,
            float channelThresholdSquareMeters = 50000f)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (geography == null) throw new ArgumentNullException(nameof(geography));
            if (hydrology == null) throw new ArgumentNullException(nameof(hydrology));

            ChannelThresholdSquareMeters = Mathf.Max(1f, channelThresholdSquareMeters);

            GameObject rootObject = new("[Infinity] Rivers");
            _root = rootObject.transform;
            _root.SetParent(parent, false);

            Shader shader = Shader.Find("InfinityProject/Water/River")
                ?? Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard");

            _material = sourceMaterial != null
                ? new Material(sourceMaterial) { name = "Infinity_River_Runtime" }
                : new Material(shader) { name = "Infinity_River_Runtime" };

            _material.SetFloat(FlowSpeedId, 0.65f);
            _material.SetFloat(FlowTurbulenceId, 0.25f);
            _material.SetColor(ShallowColorId, new Color(0.20f, 0.55f, 0.58f, 0.82f));
            _material.SetColor(DeepColorId, new Color(0.035f, 0.18f, 0.30f, 0.94f));
            _material.SetFloat(FoamDistanceId, 0.45f);

            Build(geography, hydrology);
        }

        public void Tick(float unscaledTime)
        {
            if (_material != null) _material.SetFloat(WaterTimeId, unscaledTime);
        }

        public void SetVisible(bool visible)
        {
            if (_root != null) _root.gameObject.SetActive(visible);
        }

        public void Dispose()
        {
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            if (_riverObject != null) UnityEngine.Object.Destroy(_riverObject);
            if (_material != null) UnityEngine.Object.Destroy(_material);
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);

            _mesh = null;
            _riverObject = null;
        }

        private void Build(GeographyData geography, HydrologyData hydrology)
        {
            if (geography.Resolution != hydrology.Resolution)
                throw new ArgumentException("Geography and Hydrology resolutions must match.");

            int sampleCount = hydrology.FlowReceiver.Length;
            bool[] nodeMask = new bool[sampleCount];
            List<SampleEdge> sampleEdges = new();
            float maxAccumulation = ChannelThresholdSquareMeters;

            float maxNeighborDistance = Mathf.Sqrt(
                geography.SampleSpacingX * geography.SampleSpacingX +
                geography.SampleSpacingZ * geography.SampleSpacingZ) * 1.1f;

            for (int index = 0; index < sampleCount; index++)
            {
                if (!IsRenderableChannelSample(hydrology, index, ChannelThresholdSquareMeters)) continue;

                int receiver = hydrology.FlowReceiver[index];
                if (receiver < 0 || !IsRenderableChannelSample(hydrology, receiver, ChannelThresholdSquareMeters))
                    continue;

                hydrology.Coordinates(index, out int fromX, out int fromY);
                hydrology.Coordinates(receiver, out int toX, out int toY);
                float edgeDistance = new Vector2(
                    (toX - fromX) * geography.SampleSpacingX,
                    (toY - fromY) * geography.SampleSpacingZ).magnitude;
                if (edgeDistance > maxNeighborDistance)
                    continue;

                sampleEdges.Add(new SampleEdge(index, receiver));
                nodeMask[index] = true;
                nodeMask[receiver] = true;
                maxAccumulation = Mathf.Max(maxAccumulation, hydrology.FlowAccumulationSquareMeters[index]);
                maxAccumulation = Mathf.Max(maxAccumulation, hydrology.FlowAccumulationSquareMeters[receiver]);
            }

            if (sampleEdges.Count == 0) return;

            List<int> samples = new(sampleEdges.Count + 1);
            int[] nodeLookup = new int[sampleCount];
            Array.Fill(nodeLookup, -1);

            for (int sample = 0; sample < sampleCount; sample++)
            {
                if (!nodeMask[sample]) continue;
                nodeLookup[sample] = samples.Count;
                samples.Add(sample);
            }

            int nodeCount = samples.Count;
            Vector3[] centers = new Vector3[nodeCount];
            float[] widths = new float[nodeCount];
            int[] outgoing = new int[nodeCount];
            int[] strongestIncoming = new int[nodeCount];
            Array.Fill(outgoing, -1);
            Array.Fill(strongestIncoming, -1);

            for (int node = 0; node < nodeCount; node++)
            {
                int sample = samples[node];
                hydrology.Coordinates(sample, out int x, out int y);
                // Priority-Flood depression depth is routing metadata, not a visible river
                // elevation. Rendering at the filled spill surface creates airborne bridges.
                float waterSurface = geography.GetElevationMeters(x, y) + SurfaceOffsetMeters;

                centers[node] = new Vector3(
                    x * geography.SampleSpacingX,
                    waterSurface,
                    y * geography.SampleSpacingZ);

                widths[node] = CalculateWidth(
                    hydrology.FlowAccumulationSquareMeters[sample],
                    maxAccumulation);
            }

            List<NodeEdge> edges = new(sampleEdges.Count);
            for (int i = 0; i < sampleEdges.Count; i++)
            {
                SampleEdge edge = sampleEdges[i];
                int from = nodeLookup[edge.From];
                int to = nodeLookup[edge.To];
                if (from < 0 || to < 0 || from == to) continue;

                outgoing[from] = to;
                edges.Add(new NodeEdge(from, to));

                int currentIncoming = strongestIncoming[to];
                if (currentIncoming < 0 ||
                    hydrology.FlowAccumulationSquareMeters[samples[from]] >
                    hydrology.FlowAccumulationSquareMeters[samples[currentIncoming]])
                {
                    strongestIncoming[to] = from;
                }
            }

            SegmentCount = edges.Count;
            if (SegmentCount == 0) return;

            Vector3[] rights = new Vector3[nodeCount];
            for (int node = 0; node < nodeCount; node++)
            {
                Vector3 tangent = CalculateTangent(node, centers, outgoing, strongestIncoming);
                Vector3 right = Vector3.Cross(Vector3.up, tangent);
                if (right.sqrMagnitude <= 0.000001f) right = Vector3.right;
                rights[node] = right.normalized;
            }

            float[] distanceToOutlet = CalculateDistanceToOutlet(samples, centers, outgoing, hydrology);

            Vector3[] vertices = new Vector3[nodeCount * 2];
            Vector3[] normals = new Vector3[nodeCount * 2];
            Vector2[] uvs = new Vector2[nodeCount * 2];

            for (int node = 0; node < nodeCount; node++)
            {
                Vector3 halfWidth = rights[node] * (widths[node] * 0.5f);
                int vertex = node * 2;
                Vector3 left = centers[node] - halfWidth;
                Vector3 right = centers[node] + halfWidth;

                // There is no carved riverbed yet. Keep each ribbon edge visibly above
                // the raw terrain so a wide channel cannot disappear into a cross-slope.
                left.y = Mathf.Max(left.y, SampleGeographyHeight(geography, left.x, left.z) + SurfaceOffsetMeters);
                right.y = Mathf.Max(right.y, SampleGeographyHeight(geography, right.x, right.z) + SurfaceOffsetMeters);

                vertices[vertex] = left;
                vertices[vertex + 1] = right;
                normals[vertex] = Vector3.up;
                normals[vertex + 1] = Vector3.up;

                float v = distanceToOutlet[node] / UvMetersPerRepeat;
                uvs[vertex] = new Vector2(0f, v);
                uvs[vertex + 1] = new Vector2(1f, v);
            }

            int[] triangles = new int[edges.Count * 6];
            for (int i = 0; i < edges.Count; i++)
            {
                NodeEdge edge = edges[i];
                int a = edge.From * 2;
                int b = edge.To * 2;
                int triangle = i * 6;

                triangles[triangle] = a;
                triangles[triangle + 1] = b;
                triangles[triangle + 2] = a + 1;
                triangles[triangle + 3] = a + 1;
                triangles[triangle + 4] = b;
                triangles[triangle + 5] = b + 1;
            }

            _mesh = new Mesh { name = "InfinityRiverNetwork" };
            if (vertices.Length > 65535) _mesh.indexFormat = IndexFormat.UInt32;
            _mesh.vertices = vertices;
            _mesh.normals = normals;
            _mesh.uv = uvs;
            _mesh.triangles = triangles;
            _mesh.RecalculateBounds();

            _riverObject = new GameObject("RiverNetwork");
            _riverObject.transform.SetParent(_root, false);
            MeshFilter filter = _riverObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = _riverObject.AddComponent<MeshRenderer>();
            filter.sharedMesh = _mesh;
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }


        private static bool IsRenderableChannelSample(
            HydrologyData hydrology,
            int index,
            float thresholdSquareMeters)
        {
            if (!hydrology.IsCandidateChannel(index, thresholdSquareMeters))
                return false;

            if (!hydrology.IsDepressionSample(index))
                return true;

            // Very shallow conditioning is effectively a surface correction. Deeper
            // depressions should eventually be represented as standing water/lakes,
            // not as a river ribbon suspended at the Priority-Flood spill elevation.
            return hydrology.DepressionDepthMeters[index] <= MaxRenderableConditionedDepthMeters;
        }

        private static float SampleGeographyHeight(GeographyData geography, float xMeters, float zMeters)
        {
            float gx = Mathf.Clamp(xMeters / Mathf.Max(0.0001f, geography.SampleSpacingX), 0f, geography.Resolution - 1f);
            float gy = Mathf.Clamp(zMeters / Mathf.Max(0.0001f, geography.SampleSpacingZ), 0f, geography.Resolution - 1f);

            int x0 = Mathf.FloorToInt(gx);
            int y0 = Mathf.FloorToInt(gy);
            int x1 = Mathf.Min(x0 + 1, geography.Resolution - 1);
            int y1 = Mathf.Min(y0 + 1, geography.Resolution - 1);
            float tx = gx - x0;
            float ty = gy - y0;

            float h00 = geography.GetElevationMeters(x0, y0);
            float h10 = geography.GetElevationMeters(x1, y0);
            float h01 = geography.GetElevationMeters(x0, y1);
            float h11 = geography.GetElevationMeters(x1, y1);
            float h0 = Mathf.Lerp(h00, h10, tx);
            float h1 = Mathf.Lerp(h01, h11, tx);
            return Mathf.Lerp(h0, h1, ty);
        }

        private float CalculateWidth(float accumulation, float maxAccumulation)
        {
            float normalized = maxAccumulation <= ChannelThresholdSquareMeters
                ? 0f
                : Mathf.Clamp01(
                    Mathf.Log10(Mathf.Max(accumulation, ChannelThresholdSquareMeters) / ChannelThresholdSquareMeters + 1f) /
                    Mathf.Log10(maxAccumulation / ChannelThresholdSquareMeters + 1f));

            return Mathf.Lerp(0.7f, 8f, Mathf.Pow(normalized, 0.65f));
        }

        private static Vector3 CalculateTangent(
            int node,
            Vector3[] centers,
            int[] outgoing,
            int[] strongestIncoming)
        {
            Vector3 incoming = Vector3.zero;
            Vector3 outgoingDirection = Vector3.zero;

            int upstream = strongestIncoming[node];
            if (upstream >= 0)
            {
                incoming = centers[node] - centers[upstream];
                incoming.y = 0f;
                if (incoming.sqrMagnitude > 0.000001f) incoming.Normalize();
            }

            int downstream = outgoing[node];
            if (downstream >= 0)
            {
                outgoingDirection = centers[downstream] - centers[node];
                outgoingDirection.y = 0f;
                if (outgoingDirection.sqrMagnitude > 0.000001f) outgoingDirection.Normalize();
            }

            Vector3 tangent = incoming + outgoingDirection;
            if (tangent.sqrMagnitude <= 0.000001f)
                tangent = outgoingDirection.sqrMagnitude > 0.000001f ? outgoingDirection : incoming;
            if (tangent.sqrMagnitude <= 0.000001f) tangent = Vector3.forward;
            return tangent.normalized;
        }

        private static float[] CalculateDistanceToOutlet(
            List<int> samples,
            Vector3[] centers,
            int[] outgoing,
            HydrologyData hydrology)
        {
            int nodeCount = samples.Count;
            float[] distances = new float[nodeCount];
            List<int> order = new(nodeCount);
            for (int i = 0; i < nodeCount; i++) order.Add(i);

            order.Sort((a, b) =>
                hydrology.FlowAccumulationSquareMeters[samples[b]].CompareTo(
                    hydrology.FlowAccumulationSquareMeters[samples[a]]));

            for (int i = 0; i < order.Count; i++)
            {
                int node = order[i];
                int receiver = outgoing[node];
                if (receiver < 0) continue;
                distances[node] = distances[receiver] + Vector3.Distance(centers[node], centers[receiver]);
            }

            return distances;
        }

        private readonly struct SampleEdge
        {
            public readonly int From;
            public readonly int To;

            public SampleEdge(int from, int to)
            {
                From = from;
                To = to;
            }
        }

        private readonly struct NodeEdge
        {
            public readonly int From;
            public readonly int To;

            public NodeEdge(int from, int to)
            {
                From = from;
                To = to;
            }
        }
    }
}
