using System;
using UnityEngine;

namespace InfinityProject.World.Fauna
{
    [Serializable]
    public struct FaunaAgentState
    {
        public int Id;
        public int HerdId;
        public Vector2 PositionLocalMeters;
        public Vector2 Heading;
        public float Hunger01;
        public float Energy01;
        public float Health01;
        public float CumulativeFoodConsumedKg;
        public bool Alive;
    }

    [Serializable]
    public sealed class FaunaSimulationData
    {
        public string SpeciesStableId { get; }
        public string SpeciesDisplayName { get; }
        public int SourceGeographySeed { get; }
        public float WidthMeters { get; }
        public float LengthMeters { get; }
        public double SimulationTimeDays { get; private set; }
        public FaunaAgentState[] Agents { get; }

        public int AgentCount => Agents?.Length ?? 0;

        public FaunaSimulationData(
            string speciesStableId,
            string speciesDisplayName,
            int sourceGeographySeed,
            float widthMeters,
            float lengthMeters,
            double simulationTimeDays,
            FaunaAgentState[] agents)
        {
            if (widthMeters <= 0f || lengthMeters <= 0f) throw new ArgumentOutOfRangeException(nameof(widthMeters));
            SpeciesStableId = speciesStableId ?? string.Empty;
            SpeciesDisplayName = string.IsNullOrWhiteSpace(speciesDisplayName) ? SpeciesStableId : speciesDisplayName;
            SourceGeographySeed = sourceGeographySeed;
            WidthMeters = widthMeters;
            LengthMeters = lengthMeters;
            SimulationTimeDays = Math.Max(0d, simulationTimeDays);
            Agents = agents ?? throw new ArgumentNullException(nameof(agents));
        }

        public int AliveCount
        {
            get
            {
                if (Agents == null) return 0;
                int count = 0;
                for (int i = 0; i < Agents.Length; i++) if (Agents[i].Alive) count++;
                return count;
            }
        }

        public float MeanHunger01
        {
            get
            {
                if (Agents == null) return 1f;
                float sum = 0f;
                int count = 0;
                for (int i = 0; i < Agents.Length; i++)
                {
                    if (!Agents[i].Alive) continue;
                    sum += Agents[i].Hunger01;
                    count++;
                }
                return count > 0 ? sum / count : 1f;
            }
        }

        public float MeanHealth01
        {
            get
            {
                if (Agents == null) return 0f;
                float sum = 0f;
                int count = 0;
                for (int i = 0; i < Agents.Length; i++)
                {
                    if (!Agents[i].Alive) continue;
                    sum += Agents[i].Health01;
                    count++;
                }
                return count > 0 ? sum / count : 0f;
            }
        }

        public double CumulativeFoodConsumedKg
        {
            get
            {
                if (Agents == null) return 0d;
                double sum = 0d;
                for (int i = 0; i < Agents.Length; i++) sum += Agents[i].CumulativeFoodConsumedKg;
                return sum;
            }
        }

        internal void SetSimulationTime(double absoluteDays)
            => SimulationTimeDays = Math.Max(0d, absoluteDays);
    }
}
