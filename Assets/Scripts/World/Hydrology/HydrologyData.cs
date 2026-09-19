using System;
using UnityEngine;

namespace InfinityProject.World.Hydrology
{
    public enum HydrologyTerminalType : byte
    {
        None = 0,
        Sink = 1, // Reserved for legacy/raw diagnostics; conditioned routing uses boundary outlets.
        Outlet = 2
    }

    /// <summary>
    /// Queryable hydrological structure derived from one geography state.
    /// Routing uses a hydrologically conditioned surface while raw terrain depressions remain inspectable.
    /// </summary>
    [Serializable]
    public sealed class HydrologyData
    {
        public int Resolution { get; }
        public float WidthMeters { get; }
        public float LengthMeters { get; }
        public int SourceGeographySeed { get; }
        public long SourceHeightHash { get; }

        /// <summary>Index of the downstream D8 receiver, or -1 for a boundary outlet.</summary>
        public int[] FlowReceiver { get; }

        /// <summary>Upstream contributing area in square metres, including this sample's footprint.</summary>
        public float[] FlowAccumulationSquareMeters { get; }

        /// <summary>Drainage basin id. Every sample sharing a boundary outlet has the same id.</summary>
        public int[] BasinId { get; }

        /// <summary>Terminal classification encoded as HydrologyTerminalType.</summary>
        public byte[] TerminalType { get; }

        /// <summary>1 where the unconditioned terrain has an internal D8 local minimum, otherwise 0.</summary>
        public byte[] RawSinkMask { get; }

        /// <summary>
        /// Height in metres that water would need to rise above raw terrain before the sample can drain
        /// through the conditioned surface. Zero means no depression fill is required at this sample.
        /// </summary>
        public float[] DepressionDepthMeters { get; }

        public int BasinCount { get; }
        public int RawSinkCount { get; }
        public int OutletCount { get; }
        public int DepressionSampleCount { get; }
        public float MaxDepressionDepthMeters { get; }

        public float SampleSpacingX => WidthMeters / (Resolution - 1);
        public float SampleSpacingZ => LengthMeters / (Resolution - 1);
        public float WorldAreaSquareMeters => WidthMeters * LengthMeters;

        public HydrologyData(
            int resolution,
            float widthMeters,
            float lengthMeters,
            int sourceGeographySeed,
            long sourceHeightHash,
            int[] flowReceiver,
            float[] flowAccumulationSquareMeters,
            int[] basinId,
            byte[] terminalType,
            byte[] rawSinkMask,
            float[] depressionDepthMeters,
            int basinCount,
            int rawSinkCount,
            int outletCount,
            int depressionSampleCount,
            float maxDepressionDepthMeters)
        {
            int expected = resolution * resolution;
            if (resolution < 2)
                throw new ArgumentOutOfRangeException(nameof(resolution));
            if (widthMeters <= 0f || lengthMeters <= 0f)
                throw new ArgumentOutOfRangeException(nameof(widthMeters));
            if (flowReceiver == null || flowReceiver.Length != expected)
                throw new ArgumentException("Flow receiver array size must equal resolution².", nameof(flowReceiver));
            if (flowAccumulationSquareMeters == null || flowAccumulationSquareMeters.Length != expected)
                throw new ArgumentException("Flow accumulation array size must equal resolution².", nameof(flowAccumulationSquareMeters));
            if (basinId == null || basinId.Length != expected)
                throw new ArgumentException("Basin array size must equal resolution².", nameof(basinId));
            if (terminalType == null || terminalType.Length != expected)
                throw new ArgumentException("Terminal array size must equal resolution².", nameof(terminalType));
            if (rawSinkMask == null || rawSinkMask.Length != expected)
                throw new ArgumentException("Raw sink array size must equal resolution².", nameof(rawSinkMask));
            if (depressionDepthMeters == null || depressionDepthMeters.Length != expected)
                throw new ArgumentException("Depression depth array size must equal resolution².", nameof(depressionDepthMeters));

            Resolution = resolution;
            WidthMeters = widthMeters;
            LengthMeters = lengthMeters;
            SourceGeographySeed = sourceGeographySeed;
            SourceHeightHash = sourceHeightHash;
            FlowReceiver = flowReceiver;
            FlowAccumulationSquareMeters = flowAccumulationSquareMeters;
            BasinId = basinId;
            TerminalType = terminalType;
            RawSinkMask = rawSinkMask;
            DepressionDepthMeters = depressionDepthMeters;
            BasinCount = basinCount;
            RawSinkCount = rawSinkCount;
            OutletCount = outletCount;
            DepressionSampleCount = depressionSampleCount;
            MaxDepressionDepthMeters = maxDepressionDepthMeters;
        }

        public int Index(int x, int y)
        {
            x = Mathf.Clamp(x, 0, Resolution - 1);
            y = Mathf.Clamp(y, 0, Resolution - 1);
            return y * Resolution + x;
        }

        public void Coordinates(int index, out int x, out int y)
        {
            index = Mathf.Clamp(index, 0, FlowReceiver.Length - 1);
            x = index % Resolution;
            y = index / Resolution;
        }

        public HydrologyTerminalType GetTerminalType(int index)
            => (HydrologyTerminalType)TerminalType[Mathf.Clamp(index, 0, TerminalType.Length - 1)];

        public bool IsOutlet(int index) => GetTerminalType(index) == HydrologyTerminalType.Outlet;
        public bool IsRawSink(int index) => RawSinkMask[Mathf.Clamp(index, 0, RawSinkMask.Length - 1)] != 0;
        public bool IsDepressionSample(int index) => DepressionDepthMeters[Mathf.Clamp(index, 0, DepressionDepthMeters.Length - 1)] > 0f;

        /// <summary>
        /// Thresholded drainage routing. This may pass through hydrologically conditioned depressions and
        /// therefore is not automatically an exposed surface stream.
        /// </summary>
        public bool IsCandidateChannel(int index, float thresholdSquareMeters)
            => FlowReceiver[index] >= 0 && FlowAccumulationSquareMeters[index] >= thresholdSquareMeters;

        /// <summary>
        /// True when thresholded drainage is crossing terrain that had to be raised to the spill surface.
        /// This represents conditioned standing-water transit, not an exposed stream climbing raw terrain.
        /// </summary>
        public bool IsCandidateConditionedTransit(int index, float thresholdSquareMeters)
            => IsCandidateChannel(index, thresholdSquareMeters) && IsDepressionSample(index);

        /// <summary>
        /// Candidate channel that can be interpreted as an exposed surface channel at this sample.
        /// Thresholded routing inside a filled depression is classified separately as conditioned transit.
        /// </summary>
        public bool IsCandidateOpenChannel(int index, float thresholdSquareMeters)
            => IsCandidateChannel(index, thresholdSquareMeters) && !IsDepressionSample(index);

        /// <summary>
        /// A diagnostic channel head is the first threshold-crossing sample in an exposed branch. It must
        /// not lie inside a conditioned depression, and it must have no immediate upstream thresholded
        /// routing sample. The latter prevents a lake/outflow transition from being mislabeled as a source.
        /// </summary>
        public bool IsCandidateChannelSource(int index, float thresholdSquareMeters)
        {
            if (!IsCandidateOpenChannel(index, thresholdSquareMeters))
                return false;

            Coordinates(index, out int x, out int y);
            int minY = Mathf.Max(0, y - 1);
            int maxY = Mathf.Min(Resolution - 1, y + 1);
            int minX = Mathf.Max(0, x - 1);
            int maxX = Mathf.Min(Resolution - 1, x + 1);

            for (int ny = minY; ny <= maxY; ny++)
            {
                int row = ny * Resolution;
                for (int nx = minX; nx <= maxX; nx++)
                {
                    if (nx == x && ny == y)
                        continue;

                    int upstream = row + nx;
                    if (FlowReceiver[upstream] == index && IsCandidateChannel(upstream, thresholdSquareMeters))
                        return false;
                }
            }

            return true;
        }

        public int CountCandidateChannelSources(float thresholdSquareMeters)
        {
            int count = 0;
            for (int i = 0; i < FlowReceiver.Length; i++)
            {
                if (IsCandidateChannelSource(i, thresholdSquareMeters))
                    count++;
            }
            return count;
        }

        public int CountCandidateConditionedTransitSamples(float thresholdSquareMeters)
        {
            int count = 0;
            for (int i = 0; i < FlowReceiver.Length; i++)
            {
                if (IsCandidateConditionedTransit(i, thresholdSquareMeters))
                    count++;
            }
            return count;
        }

        /// <summary>Returns receiver bearing clockwise from +Z/north. Outlets return 0 by convention.</summary>
        public float GetFlowBearingDegrees(int index)
        {
            int receiver = FlowReceiver[index];
            if (receiver < 0)
                return 0f;

            Coordinates(index, out int x0, out int y0);
            Coordinates(receiver, out int x1, out int y1);
            float dx = (x1 - x0) * SampleSpacingX;
            float dz = (y1 - y0) * SampleSpacingZ;
            float bearing = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
            return Mathf.Repeat(bearing + 360f, 360f);
        }
    }
}
