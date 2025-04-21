using JetBrains.Annotations;
using System;
using Unity.Entities;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public partial class AnimalSpawnSystem : MonoBehaviour
{
    // Runtime
    [InternalBufferCapacity(64)]
    struct AnimalPrefabInfo : IBufferElementData
    {
        public Entity EntityPrefab;
        [Tooltip("How many do you want?")]
        public float ammount;
    }

    [Serializable]
    public struct AnimalSpawn
    {
        
    }
}
