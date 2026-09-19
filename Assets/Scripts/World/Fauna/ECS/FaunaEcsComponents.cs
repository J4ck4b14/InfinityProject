using Unity.Entities;
using Unity.Mathematics;

namespace InfinityProject.World.Fauna.ECS
{
    public struct FaunaEcsIdentity : IComponentData
    {
        public int AgentId;
        public int HerdId;
        public int StateIndex;
    }

    public struct FaunaEcsLocalPosition : IComponentData
    {
        public float2 Meters;
    }

    public struct FaunaEcsHeading : IComponentData
    {
        public float2 Value;
    }

    public struct FaunaEcsMotion : IComponentData
    {
        public float LastStepDistanceMeters;
        public float CumulativeDistanceMeters;
    }

    public struct FaunaEcsPhysiology : IComponentData
    {
        public float Hunger01;
        public float Energy01;
        public float Health01;
        public float CumulativeFoodConsumedKg;
        public byte Alive;
    }

    public struct FaunaEcsMovementResult
    {
        public float2 PositionMeters;
        public float2 Heading;
    }

    public struct FaunaEcsV1Tag : IComponentData { }
}
