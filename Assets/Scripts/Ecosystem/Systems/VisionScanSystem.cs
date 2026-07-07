using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// PERCEPTION PASS – runs first in the simulation loop.
///
/// Builds a flat snapshot of every animal's data, then uses an IJobEntity
/// to fill each animal's NearbyTarget buffer with everything visible inside
/// its VisionCone.  Each entry is tagged IsThreat / IsFood / IsMate so
/// downstream steering systems never need to do their own spatial queries.
///
/// O(N²) brute-force — fine for ≤ ~500 animals.
/// The job runs single-threaded (.Run / .Schedule, NOT .ScheduleParallel)
/// because writing to a DynamicBuffer while other jobs might read the same
/// entity's data is not safe in parallel without extra synchronisation.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(HungerSystem))]
public partial class VisionScanSystem : SystemBase
{
    private EntityQuery _allAnimalsQuery;

    protected override void OnCreate()
    {
        base.OnCreate();
        _allAnimalsQuery = GetEntityQuery(
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FeedingBehavior>(),
            ComponentType.ReadOnly<SpeciesTag>(),
            ComponentType.ReadOnly<Hunger>(),
            ComponentType.ReadOnly<ReproductionData>(),
            ComponentType.ReadOnly<BiologicalGender>(),
            ComponentType.ReadOnly<Lifespan>()
        );
    }

    // ── Snapshot struct passed into the job ──────────────────────────────────

    /// <summary>All data the inner loop needs about one candidate animal.</summary>
    public struct AnimalSnapshot
    {
        public Entity          Entity;
        public float3          Position;
        public FeedingBehavior Diet;
        public SpeciesTag      Species;
        public BiologicalGender Gender;
        public ReproductionData Repro;
        public Lifespan        Life;
    }

    // ── IJobEntity ───────────────────────────────────────────────────────────

    [BurstCompile]
    public partial struct VisionScanJob : IJobEntity
    {
        [ReadOnly] public NativeArray<AnimalSnapshot> Snapshot;

        public void Execute(
            Entity self,
            ref DynamicBuffer<NearbyTarget> targets,
            in LocalTransform xf,
            in VisionCone cone,
            in FeedingBehavior myDiet,
            in SpeciesTag mySpecies,
            in BiologicalGender myGender,
            in Lifespan myLife,
            in Hunger myHunger,
            in ReproductionData myRepro)
        {
            targets.Clear();

            float3 myPos    = xf.Position;
            float3 myFwd    = xf.Forward();
            float  rangeSq  = cone.Range * cone.Range;
            float  cosHalf  = math.cos(cone.HalfAngle);

            for (int i = 0; i < Snapshot.Length; i++)
            {
                var s = Snapshot[i];
                if (s.Entity == self) continue;

                float3 toTarget = s.Position - myPos;
                float  sqDist   = math.lengthsq(toTarget);
                if (sqDist > rangeSq) continue;

                // Vision cone angle check
                if (sqDist > 0.001f)
                {
                    float dot = math.dot(math.normalize(toTarget), myFwd);
                    if (dot < cosHalf) continue;
                }

                // ── Classification ────────────────────────────────────────
                // IsThreat: target is a carnivore/omnivore of a different species
                //           and we are not a carnivore ourselves (pure prey logic)
                bool isThreat = s.Diet.CanEatMeat
                             && !myDiet.CanEatMeat
                             && s.Species.SpeciesId != mySpecies.SpeciesId;

                // IsFood: a carnivore/omnivore can eat herbivores;
                //         herbivores see other animals as non-food (plant food
                //         is handled separately by FeedSystem / plant entities)
                bool isFood = myDiet.CanEatMeat && s.Diet.IsHerbivore;

                // IsMate: same species, opposite sex, target in fertility window,
                //         scanner not too hungry to care
                bool inWindow = s.Life.Age >= s.Repro.FertilityWindow.x
                             && s.Life.Age <= s.Repro.FertilityWindow.y;
                bool isMate   = s.Species.SpeciesId == mySpecies.SpeciesId
                             && s.Gender.IsMale != myGender.IsMale
                             && inWindow
                             && myHunger.Level < 0.6f;

                targets.Add(new NearbyTarget
                {
                    Entity   = s.Entity,
                    SqDist   = sqDist,
                    IsThreat = isThreat,
                    IsFood   = isFood && !isThreat,
                    IsMate   = isMate,
                });
            }
        }
    }

    // ── OnUpdate ─────────────────────────────────────────────────────────────

    protected override void OnUpdate()
    {
        int count = _allAnimalsQuery.CalculateEntityCount();
        if (count == 0) return;

        // Build snapshot arrays from the query
        var entities   = _allAnimalsQuery.ToEntityArray           (Allocator.TempJob);
        var transforms = _allAnimalsQuery.ToComponentDataArray<LocalTransform>   (Allocator.TempJob);
        var diets      = _allAnimalsQuery.ToComponentDataArray<FeedingBehavior>  (Allocator.TempJob);
        var species    = _allAnimalsQuery.ToComponentDataArray<SpeciesTag>       (Allocator.TempJob);
        var genders    = _allAnimalsQuery.ToComponentDataArray<BiologicalGender> (Allocator.TempJob);
        var repros     = _allAnimalsQuery.ToComponentDataArray<ReproductionData> (Allocator.TempJob);
        var lifespans  = _allAnimalsQuery.ToComponentDataArray<Lifespan>         (Allocator.TempJob);

        // Pack into a single struct array so the job only needs one [ReadOnly] field
        var snapshot = new NativeArray<AnimalSnapshot>(count, Allocator.TempJob);
        for (int i = 0; i < count; i++)
        {
            snapshot[i] = new AnimalSnapshot
            {
                Entity   = entities[i],
                Position = transforms[i].Position,
                Diet     = diets[i],
                Species  = species[i],
                Gender   = genders[i],
                Repro    = repros[i],
                Life     = lifespans[i],
            };
        }

        // Dispose individual arrays — snapshot now owns the data
        entities.Dispose();
        transforms.Dispose();
        diets.Dispose();
        species.Dispose();
        genders.Dispose();
        repros.Dispose();
        lifespans.Dispose();

        // Schedule the job (single-threaded: buffer writes are not parallel-safe)
        var job = new VisionScanJob { Snapshot = snapshot };
        Dependency = job.Schedule(Dependency);

        // Dispose snapshot after the job completes
        snapshot.Dispose(Dependency);
    }
}
