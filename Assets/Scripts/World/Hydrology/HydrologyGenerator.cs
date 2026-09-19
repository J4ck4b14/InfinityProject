using System;
using InfinityProject.World.Geography;
using UnityEngine;

namespace InfinityProject.World.Hydrology
{
    /// <summary>
    /// Hydrology V0.1. Raw depressions remain diagnostics, but routing uses a Priority-Flood
    /// conditioned surface so water can rise to a spill point instead of terminating in every micro-pit.
    /// Geography itself is never modified.
    /// </summary>
    public static class HydrologyGenerator
    {
        private const float FillEpsilonMeters = 0.0001f;

        public static HydrologyData Generate(GeographyData geography, HydrologyGenerationSettings sourceSettings = null)
        {
            if (geography == null)
                throw new ArgumentNullException(nameof(geography));
            return Generate(new HydrologyElevationInput(geography), sourceSettings);
        }

        public static HydrologyData Generate(HydrologyElevationInput geography, HydrologyGenerationSettings sourceSettings = null)
        {
            if (geography == null)
                throw new ArgumentNullException(nameof(geography));

            HydrologyGenerationSettings settings = sourceSettings?.Clone() ?? new HydrologyGenerationSettings();
            settings.Validate();

            int resolution = geography.Resolution;
            int count = resolution * resolution;

            var rawSinkMask = new byte[count];
            int rawSinkCount = BuildRawSinkMask(geography, settings.MinimumDropMeters, rawSinkMask);

            var conditionedElevation = new float[count];
            var depressionDepth = new float[count];

            PriorityFlood(geography, conditionedElevation);

            int depressionSampleCount = 0;
            float maxDepressionDepth = 0f;
            for (int i = 0; i < count; i++)
            {
                float raw = geography.NormalizedHeight[i] * geography.MaxElevationMeters;
                float depth = Mathf.Max(0f, conditionedElevation[i] - raw);
                if (depth <= FillEpsilonMeters)
                    depth = 0f;
                else
                    depressionSampleCount++;

                depressionDepth[i] = depth;
                if (depth > maxDepressionDepth)
                    maxDepressionDepth = depth;
            }

            var receiver = new int[count];
            var terminalType = new byte[count];
            Array.Fill(receiver, -1);
            BuildConditionedReceivers(
                geography,
                conditionedElevation,
                settings.MinimumDropMeters,
                receiver,
                terminalType);

            var accumulation = new float[count];
            InitialiseContributingArea(geography, accumulation);

            var indegree = new int[count];
            for (int i = 0; i < count; i++)
            {
                int next = receiver[i];
                if (next >= 0)
                    indegree[next]++;
            }

            AccumulateDownstream(receiver, indegree, accumulation);

            var basinId = new int[count];
            Array.Fill(basinId, -1);
            int basinCount = AssignBasins(receiver, basinId);

            int outletCount = 0;
            for (int i = 0; i < terminalType.Length; i++)
            {
                if ((HydrologyTerminalType)terminalType[i] == HydrologyTerminalType.Outlet)
                    outletCount++;
            }

            return new HydrologyData(
                resolution,
                geography.WidthMeters,
                geography.LengthMeters,
                geography.Seed,
                ComputeHeightHash(geography.NormalizedHeight),
                receiver,
                accumulation,
                basinId,
                terminalType,
                rawSinkMask,
                depressionDepth,
                basinCount,
                rawSinkCount,
                outletCount,
                depressionSampleCount,
                maxDepressionDepth);
        }

        public static long ComputeHeightHash(float[] normalizedHeight)
        {
            if (normalizedHeight == null)
                return 0L;

            unchecked
            {
                ulong hash = 14695981039346656037UL;
                const ulong prime = 1099511628211UL;
                for (int i = 0; i < normalizedHeight.Length; i++)
                {
                    uint bits = (uint)BitConverter.SingleToInt32Bits(normalizedHeight[i]);
                    hash ^= bits;
                    hash *= prime;
                }
                return (long)hash;
            }
        }

        private static int BuildRawSinkMask(HydrologyElevationInput geography, float minimumDropMeters, byte[] rawSinkMask)
        {
            int resolution = geography.Resolution;
            int sinkCount = 0;

            for (int y = 1; y < resolution - 1; y++)
            {
                for (int x = 1; x < resolution - 1; x++)
                {
                    int index = y * resolution + x;
                    float center = geography.NormalizedHeight[index] * geography.MaxElevationMeters;
                    bool hasLower = false;

                    for (int oy = -1; oy <= 1 && !hasLower; oy++)
                    {
                        int row = (y + oy) * resolution;
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            if (ox == 0 && oy == 0)
                                continue;

                            float neighbour = geography.NormalizedHeight[row + x + ox] * geography.MaxElevationMeters;
                            if (center - neighbour > minimumDropMeters)
                            {
                                hasLower = true;
                                break;
                            }
                        }
                    }

                    if (!hasLower)
                    {
                        rawSinkMask[index] = 1;
                        sinkCount++;
                    }
                }
            }

            return sinkCount;
        }

        /// <summary>
        /// Computes the minimum spill elevation needed for every sample to connect to the open world boundary.
        /// The original elevation field is not changed.
        /// </summary>
        private static void PriorityFlood(
            HydrologyElevationInput geography,
            float[] conditionedElevation)
        {
            int resolution = geography.Resolution;
            int count = resolution * resolution;
            var visited = new byte[count];
            var heap = new MinHeap(count);

            void Seed(int x, int y)
            {
                int index = y * resolution + x;
                if (visited[index] != 0)
                    return;

                visited[index] = 1;
                float elevation = geography.NormalizedHeight[index] * geography.MaxElevationMeters;
                conditionedElevation[index] = elevation;
                heap.Push(index, elevation);
            }

            for (int x = 0; x < resolution; x++)
            {
                Seed(x, 0);
                Seed(x, resolution - 1);
            }
            for (int y = 1; y < resolution - 1; y++)
            {
                Seed(0, y);
                Seed(resolution - 1, y);
            }

            while (heap.Count > 0)
            {
                heap.Pop(out int current, out float currentFill);
                int x = current % resolution;
                int y = current / resolution;

                int minY = Mathf.Max(0, y - 1);
                int maxY = Mathf.Min(resolution - 1, y + 1);
                int minX = Mathf.Max(0, x - 1);
                int maxX = Mathf.Min(resolution - 1, x + 1);

                for (int ny = minY; ny <= maxY; ny++)
                {
                    int row = ny * resolution;
                    for (int nx = minX; nx <= maxX; nx++)
                    {
                        if (nx == x && ny == y)
                            continue;

                        int next = row + nx;
                        if (visited[next] != 0)
                            continue;

                        visited[next] = 1;
                        float raw = geography.NormalizedHeight[next] * geography.MaxElevationMeters;
                        float fill = Mathf.Max(raw, currentFill);
                        conditionedElevation[next] = fill;
                        heap.Push(next, fill);
                    }
                }
            }
        }

        /// <summary>
        /// Routes on real downhill gradients first, then resolves every equal-height conditioned flat as a
        /// connected region. Flat cells move monotonically toward a real spill/outlet while preferring the
        /// interior of the flat over its higher walls and, secondarily, lower raw terrain. This removes the
        /// arbitrary Priority-Flood visitation path from flow direction without changing Geography.
        /// </summary>
        private static void BuildConditionedReceivers(
            HydrologyElevationInput geography,
            float[] conditionedElevation,
            float minimumDropMeters,
            int[] receiver,
            byte[] terminalType)
        {
            int resolution = geography.Resolution;
            int count = resolution * resolution;
            float spacingX = geography.SampleSpacingX;
            float spacingZ = geography.SampleSpacingZ;
            const float tieEpsilon = 0.0000001f;

            Array.Fill(receiver, -1);

            // Pass 1: preserve every unambiguous physical downhill route.
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int index = y * resolution + x;
                    bool border = x == 0 || y == 0 || x == resolution - 1 || y == resolution - 1;

                    if (border)
                    {
                        int edgeReceiver = FindLowerBoundaryNeighbour(
                            geography, x, y, minimumDropMeters, spacingX, spacingZ);
                        receiver[index] = edgeReceiver;
                        if (edgeReceiver < 0)
                            terminalType[index] = (byte)HydrologyTerminalType.Outlet;
                        continue;
                    }

                    float center = conditionedElevation[index];
                    float bestGradient = 0f;
                    int bestReceiver = -1;

                    for (int oy = -1; oy <= 1; oy++)
                    {
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            if (ox == 0 && oy == 0)
                                continue;

                            int neighbourIndex = (y + oy) * resolution + x + ox;
                            float drop = center - conditionedElevation[neighbourIndex];
                            if (drop <= minimumDropMeters)
                                continue;

                            float horizontalDistance = Mathf.Sqrt(
                                ox * ox * spacingX * spacingX +
                                oy * oy * spacingZ * spacingZ);
                            float gradient = drop / Mathf.Max(horizontalDistance, 0.000001f);

                            if (gradient > bestGradient + tieEpsilon ||
                                (Mathf.Abs(gradient - bestGradient) <= tieEpsilon &&
                                 (bestReceiver < 0 || neighbourIndex < bestReceiver)))
                            {
                                bestGradient = gradient;
                                bestReceiver = neighbourIndex;
                            }
                        }
                    }

                    receiver[index] = bestReceiver;
                }
            }

            ResolveConditionedFlats(geography, conditionedElevation, minimumDropMeters, receiver, terminalType);

            for (int y = 1; y < resolution - 1; y++)
            {
                for (int x = 1; x < resolution - 1; x++)
                {
                    int index = y * resolution + x;
                    if (receiver[index] < 0)
                        throw new InvalidOperationException($"Conditioned flat routing could not find a spill path for interior sample ({x},{y}).");
                }
            }
        }

        private static void ResolveConditionedFlats(
            HydrologyElevationInput geography,
            float[] conditionedElevation,
            float minimumDropMeters,
            int[] receiver,
            byte[] terminalType)
        {
            int resolution = geography.Resolution;
            int count = resolution * resolution;
            float flatTolerance = Mathf.Max(minimumDropMeters, 0.000001f);
            const float tieEpsilon = 0.0000001f;

            var componentMark = new int[count];
            var component = new int[count];
            var queue = new int[count];
            var outletDistance = new int[count];
            var highDistance = new int[count];
            int token = 0;

            for (int start = 0; start < count; start++)
            {
                int sx = start % resolution;
                int sy = start / resolution;
                bool startBorder = sx == 0 || sy == 0 || sx == resolution - 1 || sy == resolution - 1;

                // Strict downhill cells are already solved. Border outlets can seed a flat but need no receiver.
                if (receiver[start] >= 0 || startBorder)
                    continue;
                if (componentMark[start] != 0)
                    continue;

                token++;
                float level = conditionedElevation[start];
                int componentCount = 0;
                int head = 0;
                int tail = 0;
                queue[tail++] = start;
                componentMark[start] = token;

                while (head < tail)
                {
                    int current = queue[head++];
                    component[componentCount++] = current;
                    int x = current % resolution;
                    int y = current / resolution;

                    int minY = Mathf.Max(0, y - 1);
                    int maxY = Mathf.Min(resolution - 1, y + 1);
                    int minX = Mathf.Max(0, x - 1);
                    int maxX = Mathf.Min(resolution - 1, x + 1);

                    for (int ny = minY; ny <= maxY; ny++)
                    {
                        int row = ny * resolution;
                        for (int nx = minX; nx <= maxX; nx++)
                        {
                            if (nx == x && ny == y)
                                continue;

                            int next = row + nx;
                            if (componentMark[next] != 0)
                                continue;
                            if (Mathf.Abs(conditionedElevation[next] - level) > flatTolerance)
                                continue;

                            componentMark[next] = token;
                            queue[tail++] = next;
                        }
                    }
                }

                int outletSeedCount = 0;
                int highSeedCount = 0;
                for (int i = 0; i < componentCount; i++)
                {
                    int current = component[i];
                    outletDistance[current] = -1;
                    highDistance[current] = -1;
                }

                // Drainage seeds are flat cells that already descend to a lower conditioned neighbour,
                // plus boundary outlets. High-edge seeds identify where the flat touches higher terrain.
                for (int i = 0; i < componentCount; i++)
                {
                    int current = component[i];
                    int x = current % resolution;
                    int y = current / resolution;
                    bool border = x == 0 || y == 0 || x == resolution - 1 || y == resolution - 1;

                    if (receiver[current] >= 0 || (border && (HydrologyTerminalType)terminalType[current] == HydrologyTerminalType.Outlet))
                    {
                        outletDistance[current] = 0;
                        queue[outletSeedCount++] = current;
                    }

                    bool touchesHigher = false;
                    int minY = Mathf.Max(0, y - 1);
                    int maxY = Mathf.Min(resolution - 1, y + 1);
                    int minX = Mathf.Max(0, x - 1);
                    int maxX = Mathf.Min(resolution - 1, x + 1);
                    for (int ny = minY; ny <= maxY && !touchesHigher; ny++)
                    {
                        int row = ny * resolution;
                        for (int nx = minX; nx <= maxX; nx++)
                        {
                            if (nx == x && ny == y)
                                continue;
                            int next = row + nx;
                            if (conditionedElevation[next] > level + flatTolerance)
                            {
                                touchesHigher = true;
                                break;
                            }
                        }
                    }

                    if (touchesHigher)
                    {
                        highDistance[current] = 0;
                        highSeedCount++;
                    }
                }

                if (outletSeedCount == 0)
                    throw new InvalidOperationException("Conditioned flat has no lower spill cell or boundary outlet.");

                // Multi-source BFS toward a spill/outlet. Every unresolved cell will later point to a neighbour
                // with a strictly smaller outlet distance, which guarantees acyclic flat routing.
                head = 0;
                tail = outletSeedCount;
                while (head < tail)
                {
                    int current = queue[head++];
                    int distance = outletDistance[current];
                    int x = current % resolution;
                    int y = current / resolution;
                    int minY = Mathf.Max(0, y - 1);
                    int maxY = Mathf.Min(resolution - 1, y + 1);
                    int minX = Mathf.Max(0, x - 1);
                    int maxX = Mathf.Min(resolution - 1, x + 1);

                    for (int ny = minY; ny <= maxY; ny++)
                    {
                        int row = ny * resolution;
                        for (int nx = minX; nx <= maxX; nx++)
                        {
                            if (nx == x && ny == y)
                                continue;
                            int next = row + nx;
                            if (componentMark[next] != token || outletDistance[next] >= 0)
                                continue;
                            outletDistance[next] = distance + 1;
                            queue[tail++] = next;
                        }
                    }
                }

                // Distance from higher terrain provides a second, subordinate gradient. It encourages routes
                // through the interior of broad flats instead of following an arbitrary edge/heap visitation path.
                if (highSeedCount > 0)
                {
                    head = 0;
                    tail = 0;
                    for (int i = 0; i < componentCount; i++)
                    {
                        int current = component[i];
                        if (highDistance[current] == 0)
                            queue[tail++] = current;
                    }

                    while (head < tail)
                    {
                        int current = queue[head++];
                        int distance = highDistance[current];
                        int x = current % resolution;
                        int y = current / resolution;
                        int minY = Mathf.Max(0, y - 1);
                        int maxY = Mathf.Min(resolution - 1, y + 1);
                        int minX = Mathf.Max(0, x - 1);
                        int maxX = Mathf.Min(resolution - 1, x + 1);

                        for (int ny = minY; ny <= maxY; ny++)
                        {
                            int row = ny * resolution;
                            for (int nx = minX; nx <= maxX; nx++)
                            {
                                if (nx == x && ny == y)
                                    continue;
                                int next = row + nx;
                                if (componentMark[next] != token || highDistance[next] >= 0)
                                    continue;
                                highDistance[next] = distance + 1;
                                queue[tail++] = next;
                            }
                        }
                    }
                }

                for (int i = 0; i < componentCount; i++)
                {
                    int current = component[i];
                    int x = current % resolution;
                    int y = current / resolution;
                    bool border = x == 0 || y == 0 || x == resolution - 1 || y == resolution - 1;
                    if (receiver[current] >= 0 || border)
                        continue;

                    int currentOutletDistance = outletDistance[current];
                    int best = -1;
                    int bestHighDistance = int.MinValue;
                    float bestRawElevation = float.PositiveInfinity;
                    float bestStepDistance = float.PositiveInfinity;

                    for (int oy = -1; oy <= 1; oy++)
                    {
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            if (ox == 0 && oy == 0)
                                continue;

                            int next = (y + oy) * resolution + x + ox;
                            if (componentMark[next] != token)
                                continue;
                            if (outletDistance[next] >= currentOutletDistance)
                                continue;

                            int nextHighDistance = highDistance[next];
                            float nextRaw = geography.NormalizedHeight[next] * geography.MaxElevationMeters;
                            float stepDistance = Mathf.Sqrt(
                                ox * ox * geography.SampleSpacingX * geography.SampleSpacingX +
                                oy * oy * geography.SampleSpacingZ * geography.SampleSpacingZ);

                            bool better = false;
                            if (nextHighDistance > bestHighDistance)
                                better = true;
                            else if (nextHighDistance == bestHighDistance && nextRaw < bestRawElevation - tieEpsilon)
                                better = true;
                            else if (nextHighDistance == bestHighDistance && Mathf.Abs(nextRaw - bestRawElevation) <= tieEpsilon && stepDistance < bestStepDistance - tieEpsilon)
                                better = true;
                            else if (nextHighDistance == bestHighDistance && Mathf.Abs(nextRaw - bestRawElevation) <= tieEpsilon && Mathf.Abs(stepDistance - bestStepDistance) <= tieEpsilon && (best < 0 || next < best))
                                better = true;

                            if (better)
                            {
                                best = next;
                                bestHighDistance = nextHighDistance;
                                bestRawElevation = nextRaw;
                                bestStepDistance = stepDistance;
                            }
                        }
                    }

                    if (best < 0)
                        throw new InvalidOperationException("Flat routing failed to find a neighbour closer to the spill/outlet.");

                    receiver[current] = best;
                }
            }
        }

        private static int FindLowerBoundaryNeighbour(
            HydrologyElevationInput geography,
            int x,
            int y,
            float minimumDropMeters,
            float spacingX,
            float spacingZ)
        {
            int resolution = geography.Resolution;
            int index = y * resolution + x;
            float center = geography.NormalizedHeight[index] * geography.MaxElevationMeters;
            float bestGradient = 0f;
            int bestReceiver = -1;
            const float tieEpsilon = 0.0000001f;

            for (int oy = -1; oy <= 1; oy++)
            {
                for (int ox = -1; ox <= 1; ox++)
                {
                    if (ox == 0 && oy == 0)
                        continue;

                    int nx = x + ox;
                    int ny = y + oy;
                    if (nx < 0 || nx >= resolution || ny < 0 || ny >= resolution)
                        continue;

                    bool neighbourIsBorder = nx == 0 || ny == 0 || nx == resolution - 1 || ny == resolution - 1;
                    if (!neighbourIsBorder)
                        continue;

                    int neighbourIndex = ny * resolution + nx;
                    float neighbour = geography.NormalizedHeight[neighbourIndex] * geography.MaxElevationMeters;
                    float drop = center - neighbour;
                    if (drop <= minimumDropMeters)
                        continue;

                    float horizontalDistance = Mathf.Sqrt(
                        ox * ox * spacingX * spacingX +
                        oy * oy * spacingZ * spacingZ);
                    float gradient = drop / Mathf.Max(horizontalDistance, 0.000001f);

                    if (gradient > bestGradient + tieEpsilon ||
                        (Mathf.Abs(gradient - bestGradient) <= tieEpsilon &&
                         (bestReceiver < 0 || neighbourIndex < bestReceiver)))
                    {
                        bestGradient = gradient;
                        bestReceiver = neighbourIndex;
                    }
                }
            }

            return bestReceiver;
        }

        private static void InitialiseContributingArea(HydrologyElevationInput geography, float[] accumulation)
        {
            int resolution = geography.Resolution;
            float baseArea = geography.SampleSpacingX * geography.SampleSpacingZ;

            for (int y = 0; y < resolution; y++)
            {
                float zWeight = (y == 0 || y == resolution - 1) ? 0.5f : 1f;
                for (int x = 0; x < resolution; x++)
                {
                    float xWeight = (x == 0 || x == resolution - 1) ? 0.5f : 1f;
                    accumulation[y * resolution + x] = baseArea * xWeight * zWeight;
                }
            }
        }

        private static void AccumulateDownstream(int[] receiver, int[] indegree, float[] accumulation)
        {
            int count = receiver.Length;
            var queue = new int[count];
            int head = 0;
            int tail = 0;

            for (int i = 0; i < count; i++)
            {
                if (indegree[i] == 0)
                    queue[tail++] = i;
            }

            int processed = 0;
            while (head < tail)
            {
                int current = queue[head++];
                processed++;
                int next = receiver[current];
                if (next < 0)
                    continue;

                accumulation[next] += accumulation[current];
                indegree[next]--;
                if (indegree[next] == 0)
                    queue[tail++] = next;
            }

            if (processed != count)
                throw new InvalidOperationException("Hydrology routing contained a cycle.");
        }

        private static int AssignBasins(int[] receiver, int[] basinId)
        {
            int count = receiver.Length;
            var terminalToBasin = new int[count];
            var path = new int[count];
            Array.Fill(terminalToBasin, -1);

            int basinCount = 0;
            for (int start = 0; start < count; start++)
            {
                if (basinId[start] >= 0)
                    continue;

                int current = start;
                int pathLength = 0;
                while (current >= 0 && basinId[current] < 0)
                {
                    path[pathLength++] = current;
                    current = receiver[current];
                }

                int basin;
                if (current >= 0)
                {
                    basin = basinId[current];
                }
                else
                {
                    int terminal = path[pathLength - 1];
                    basin = terminalToBasin[terminal];
                    if (basin < 0)
                    {
                        basin = basinCount++;
                        terminalToBasin[terminal] = basin;
                    }
                }

                for (int i = 0; i < pathLength; i++)
                    basinId[path[i]] = basin;
            }

            return basinCount;
        }

        /// <summary>Allocation-free deterministic binary min-heap for Priority-Flood.</summary>
        private sealed class MinHeap
        {
            private readonly int[] _indices;
            private readonly float[] _priorities;
            private int _count;

            public int Count => _count;

            public MinHeap(int capacity)
            {
                _indices = new int[capacity];
                _priorities = new float[capacity];
            }

            public void Push(int index, float priority)
            {
                int child = _count++;
                while (child > 0)
                {
                    int parent = (child - 1) >> 1;
                    if (!ComesBefore(priority, index, _priorities[parent], _indices[parent]))
                        break;

                    _indices[child] = _indices[parent];
                    _priorities[child] = _priorities[parent];
                    child = parent;
                }

                _indices[child] = index;
                _priorities[child] = priority;
            }

            public void Pop(out int index, out float priority)
            {
                if (_count <= 0)
                    throw new InvalidOperationException("Cannot pop an empty heap.");

                index = _indices[0];
                priority = _priorities[0];
                _count--;
                if (_count == 0)
                    return;

                int lastIndex = _indices[_count];
                float lastPriority = _priorities[_count];
                int parent = 0;

                while (true)
                {
                    int left = parent * 2 + 1;
                    if (left >= _count)
                        break;

                    int right = left + 1;
                    int best = left;
                    if (right < _count && ComesBefore(
                            _priorities[right], _indices[right],
                            _priorities[left], _indices[left]))
                    {
                        best = right;
                    }

                    if (!ComesBefore(
                            _priorities[best], _indices[best],
                            lastPriority, lastIndex))
                    {
                        break;
                    }

                    _indices[parent] = _indices[best];
                    _priorities[parent] = _priorities[best];
                    parent = best;
                }

                _indices[parent] = lastIndex;
                _priorities[parent] = lastPriority;
            }

            private static bool ComesBefore(float priorityA, int indexA, float priorityB, int indexB)
            {
                if (priorityA < priorityB) return true;
                if (priorityA > priorityB) return false;
                return indexA < indexB;
            }
        }
    }
}
