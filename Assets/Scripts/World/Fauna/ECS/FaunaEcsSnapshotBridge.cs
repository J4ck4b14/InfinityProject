using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using ECSWorld = Unity.Entities.World;

namespace InfinityProject.World.Fauna.ECS
{
    public sealed class FaunaEcsSnapshotBridge : IDisposable
    {
        private readonly Dictionary<int, Entity> _entityByAgentId = new();
        private readonly HashSet<int> _seenAgentIds = new();
        private readonly List<int> _removedAgentIds = new();

        private ECSWorld _world;
        private EntityManager _entityManager;
        private EntityArchetype _archetype;

        public bool IsCreated => _world != null && _world.IsCreated;
        public int EntityCount => _entityByAgentId.Count;
        public ECSWorld World => _world;
        public EntityManager EntityManager => _entityManager;

        public FaunaEcsSnapshotBridge(string worldName = "Infinity Fauna ECS V1 Bridge")
        {
            _world = new ECSWorld(worldName);
            _entityManager = _world.EntityManager;
            _archetype = _entityManager.CreateArchetype(
                typeof(FaunaEcsV1Tag),
                typeof(FaunaEcsIdentity),
                typeof(FaunaEcsLocalPosition),
                typeof(FaunaEcsHeading),
                typeof(FaunaEcsMotion),
                typeof(FaunaEcsPhysiology));
        }

        public void Sync(FaunaSimulationData fauna)
        {
            if (fauna == null) throw new ArgumentNullException(nameof(fauna));
            ThrowIfDisposed();

            _seenAgentIds.Clear();
            for (int i = 0; i < fauna.Agents.Length; i++)
            {
                FaunaAgentState source = fauna.Agents[i];
                if (!_seenAgentIds.Add(source.Id))
                    throw new InvalidOperationException($"Fauna contains duplicate stable agent id {source.Id}.");

                if (!_entityByAgentId.TryGetValue(source.Id, out Entity entity) || !_entityManager.Exists(entity))
                {
                    entity = _entityManager.CreateEntity(_archetype);
                    _entityByAgentId[source.Id] = entity;
                }

                float2 heading = new(source.Heading.x, source.Heading.y);
                float headingLengthSq = math.lengthsq(heading);
                heading = headingLengthSq > 0.000001f
                    ? heading * math.rsqrt(headingLengthSq)
                    : new float2(0f, 1f);

                _entityManager.SetComponentData(entity, new FaunaEcsIdentity
                {
                    AgentId = source.Id,
                    HerdId = source.HerdId,
                    StateIndex = i
                });
                _entityManager.SetComponentData(entity, new FaunaEcsLocalPosition
                {
                    Meters = new float2(source.PositionLocalMeters.x, source.PositionLocalMeters.y)
                });
                _entityManager.SetComponentData(entity, new FaunaEcsHeading { Value = heading });
                _entityManager.SetComponentData(entity, new FaunaEcsPhysiology
                {
                    Hunger01 = source.Hunger01,
                    Energy01 = source.Energy01,
                    Health01 = source.Health01,
                    CumulativeFoodConsumedKg = source.CumulativeFoodConsumedKg,
                    Alive = source.Alive ? (byte)1 : (byte)0
                });
            }

            if (_entityByAgentId.Count == _seenAgentIds.Count) return;

            _removedAgentIds.Clear();
            foreach (KeyValuePair<int, Entity> pair in _entityByAgentId)
            {
                if (_seenAgentIds.Contains(pair.Key)) continue;
                if (_entityManager.Exists(pair.Value)) _entityManager.DestroyEntity(pair.Value);
                _removedAgentIds.Add(pair.Key);
            }

            for (int i = 0; i < _removedAgentIds.Count; i++)
                _entityByAgentId.Remove(_removedAgentIds[i]);
        }

        public void SyncPhysiology(FaunaSimulationData fauna)
        {
            if (fauna == null) throw new ArgumentNullException(nameof(fauna));
            ThrowIfDisposed();

            for (int i = 0; i < fauna.Agents.Length; i++)
            {
                FaunaAgentState source = fauna.Agents[i];
                if (!_entityByAgentId.TryGetValue(source.Id, out Entity entity) || !_entityManager.Exists(entity)) continue;

                _entityManager.SetComponentData(entity, new FaunaEcsPhysiology
                {
                    Hunger01 = source.Hunger01,
                    Energy01 = source.Energy01,
                    Health01 = source.Health01,
                    CumulativeFoodConsumedKg = source.CumulativeFoodConsumedKg,
                    Alive = source.Alive ? (byte)1 : (byte)0
                });
            }
        }

        public bool TryGetEntity(int agentId, out Entity entity)
        {
            entity = Entity.Null;
            if (!IsCreated || !_entityByAgentId.TryGetValue(agentId, out Entity found) || !_entityManager.Exists(found))
                return false;

            entity = found;
            return true;
        }

        public bool TryReadAgent(int agentId, out FaunaEcsAgentSnapshot snapshot)
        {
            snapshot = default;
            if (!TryGetEntity(agentId, out Entity entity)) return false;

            snapshot = new FaunaEcsAgentSnapshot(
                _entityManager.GetComponentData<FaunaEcsIdentity>(entity),
                _entityManager.GetComponentData<FaunaEcsLocalPosition>(entity),
                _entityManager.GetComponentData<FaunaEcsHeading>(entity),
                _entityManager.GetComponentData<FaunaEcsMotion>(entity),
                _entityManager.GetComponentData<FaunaEcsPhysiology>(entity));
            return true;
        }

        public void Dispose()
        {
            _entityByAgentId.Clear();
            _seenAgentIds.Clear();
            _removedAgentIds.Clear();
            if (_world != null && _world.IsCreated) _world.Dispose();
            _world = null;
            _entityManager = default;
            _archetype = default;
        }

        private void ThrowIfDisposed()
        {
            if (!IsCreated) throw new ObjectDisposedException(nameof(FaunaEcsSnapshotBridge));
        }
    }

    public readonly struct FaunaEcsAgentSnapshot
    {
        public readonly FaunaEcsIdentity Identity;
        public readonly FaunaEcsLocalPosition Position;
        public readonly FaunaEcsHeading Heading;
        public readonly FaunaEcsMotion Motion;
        public readonly FaunaEcsPhysiology Physiology;

        public FaunaEcsAgentSnapshot(
            FaunaEcsIdentity identity,
            FaunaEcsLocalPosition position,
            FaunaEcsHeading heading,
            FaunaEcsMotion motion,
            FaunaEcsPhysiology physiology)
        {
            Identity = identity;
            Position = position;
            Heading = heading;
            Motion = motion;
            Physiology = physiology;
        }
    }
}
