using System.Collections.Generic;
using InfinityProject.World.Geography;
using UnityEngine;

namespace InfinityProject.World.Fauna.Presentation
{
    internal sealed class FaunaRuntimePresentationPool
    {
        private const float GroundClearanceMeters = 0.03f;

        private sealed class Instance
        {
            public GameObject GameObject;
            public DoePresentationRig Rig;
            public FaunaPresentationAgent DebugAgent;
            public readonly MaterialPropertyBlock MaterialBlock = new();

            public Vector3 TargetPosition;
            public Vector3 HeadingWorld = Vector3.forward;
            public float SpeedMetersPerSecond;
            public float GaitPhase01;
            public float EatHoldSeconds;
            public float LastConsumedKg;
            public float AnimationTime;
            public double SecondsSinceMotionSample;
            public bool HasTarget;
            public FaunaAgentState LastAgent;

            public Vector3 PelvisPosition;
            public Quaternion PelvisRotation;
            public Quaternion Spine1Rotation;
            public Quaternion Spine2Rotation;
            public Quaternion Neck1Rotation;
            public Quaternion Neck2Rotation;
            public Quaternion HeadRotation;
            public Quaternion FrontUpperLRotation;
            public Quaternion FrontLowerLRotation;
            public Quaternion FrontUpperRRotation;
            public Quaternion FrontLowerRRotation;
            public Quaternion HindUpperLRotation;
            public Quaternion HindLowerLRotation;
            public Quaternion HindUpperRRotation;
            public Quaternion HindLowerRRotation;
        }

        private readonly Dictionary<int, Instance> _active = new();
        private readonly Stack<Instance> _pool = new();
        private readonly HashSet<int> _seen = new();
        private readonly List<int> _release = new();

        private readonly Terrain _terrain;
        private readonly GameObject _prefab;
        private readonly Transform _root;

        public int ActiveCount => _active.Count;
        public int PooledCount => _pool.Count;

        public FaunaRuntimePresentationPool(Terrain terrain, GeographyData geography, GameObject prefab, Transform parent)
        {
            _terrain = terrain;
            if (geography == null) throw new System.ArgumentNullException(nameof(geography));
            _prefab = prefab;

            var rootObject = new GameObject("[Infinity] Live Fauna");
            _root = rootObject.transform;
            _root.SetParent(parent, false);
        }

        public void Sync(FaunaSimulationData fauna, double simulationDeltaSeconds = 0d)
        {
            if (fauna?.Agents == null) return;

            _seen.Clear();
            for (int i = 0; i < fauna.Agents.Length; i++)
            {
                FaunaAgentState agent = fauna.Agents[i];
                if (!agent.Alive)
                {
                    Release(agent.Id);
                    continue;
                }

                _seen.Add(agent.Id);
                Instance instance = Acquire(agent.Id);
                UpdateTarget(instance, agent, simulationDeltaSeconds);
                ApplyTint(instance, agent.Hunger01);
            }

            _release.Clear();
            foreach (KeyValuePair<int, Instance> pair in _active)
                if (!_seen.Contains(pair.Key)) _release.Add(pair.Key);

            for (int i = 0; i < _release.Count; i++) Release(_release[i]);
        }

        public void Tick(float simulationDeltaSeconds)
        {
            float dt = Mathf.Max(0f, simulationDeltaSeconds);
            foreach (Instance instance in _active.Values)
            {
                if (instance.GameObject == null || !instance.HasTarget) continue;

                if (dt > 0f)
                {
                    instance.AnimationTime += dt;
                    instance.EatHoldSeconds = Mathf.Max(0f, instance.EatHoldSeconds - dt);
                    if (instance.SpeedMetersPerSecond > 0.02f)
                    {
                        instance.GaitPhase01 = Mathf.Repeat(
                            instance.GaitPhase01 + instance.SpeedMetersPerSecond * dt / instance.Rig.StrideLengthMeters,
                            1f);
                    }

                    float distance = Vector3.Distance(instance.GameObject.transform.position, instance.TargetPosition);
                    Vector3 nextPosition = distance > 25f
                        ? instance.TargetPosition
                        : Vector3.Lerp(
                            instance.GameObject.transform.position,
                            instance.TargetPosition,
                            1f - Mathf.Exp(-10f * dt));

                    SnapToTerrain(instance, ref nextPosition, out Vector3 groundNormal);
                    Quaternion groundedRotation = BuildGroundedRotation(instance);

                    instance.GameObject.transform.position = nextPosition;
                    instance.GameObject.transform.rotation = distance > 25f
                        ? groundedRotation
                        : Quaternion.Slerp(
                            instance.GameObject.transform.rotation,
                            groundedRotation,
                            1f - Mathf.Exp(-12f * dt));
                }

                DoeAnimationState state = DetermineState(instance);
                ApplyProceduralPose(instance, state);
                LiftHoovesOutOfTerrain(instance);
                instance.DebugAgent.SetDebugState(
                    instance.LastAgent,
                    state,
                    instance.SpeedMetersPerSecond,
                    instance.GaitPhase01,
                    instance.TargetPosition,
                    instance.HeadingWorld);
            }
        }

        public void Dispose()
        {
            foreach (Instance instance in _active.Values)
                if (instance.GameObject != null) Object.Destroy(instance.GameObject);
            _active.Clear();

            while (_pool.Count > 0)
            {
                Instance instance = _pool.Pop();
                if (instance.GameObject != null) Object.Destroy(instance.GameObject);
            }

            if (_root != null) Object.Destroy(_root.gameObject);
            _seen.Clear();
            _release.Clear();
        }

        private Instance Acquire(int agentId)
        {
            if (_active.TryGetValue(agentId, out Instance existing)) return existing;

            Instance instance;
            if (_pool.Count > 0)
            {
                instance = _pool.Pop();
            }
            else
            {
                GameObject go = Object.Instantiate(_prefab, _root);
                DoePresentationRig rig = go.GetComponent<DoePresentationRig>();
                if (rig == null)
                {
                    Object.Destroy(go);
                    throw new System.InvalidOperationException("Doe prefab is missing DoePresentationRig.");
                }

                FaunaPresentationAgent debugAgent = go.GetComponent<FaunaPresentationAgent>();
                if (debugAgent == null) debugAgent = go.AddComponent<FaunaPresentationAgent>();

                instance = new Instance
                {
                    GameObject = go,
                    Rig = rig,
                    DebugAgent = debugAgent
                };
                CaptureBindPose(instance);
            }

            instance.GameObject.name = $"Doe_{agentId:0000}";
            instance.GameObject.transform.SetParent(_root, false);
            instance.GameObject.SetActive(true);
            instance.HasTarget = false;
            instance.SpeedMetersPerSecond = 0f;
            instance.EatHoldSeconds = 0f;
            instance.AnimationTime = 0f;
            instance.GaitPhase01 = 0f;
            instance.SecondsSinceMotionSample = 0d;
            _active.Add(agentId, instance);
            return instance;
        }

        private void Release(int agentId)
        {
            if (!_active.TryGetValue(agentId, out Instance instance)) return;
            _active.Remove(agentId);

            if (instance.GameObject == null) return;
            ResetPose(instance);
            instance.GameObject.SetActive(false);
            _pool.Push(instance);
        }

        private void UpdateTarget(Instance instance, FaunaAgentState agent, double simulationDeltaSeconds)
        {
            Vector3 worldPosition = _terrain.transform.TransformPoint(
                new Vector3(agent.PositionLocalMeters.x, 0f, agent.PositionLocalMeters.y));
            SnapToTerrain(instance, ref worldPosition, out Vector3 terrainNormal);

            Vector3 localHeading = new(agent.Heading.x, 0f, agent.Heading.y);
            if (localHeading.sqrMagnitude <= 0.000001f) localHeading = Vector3.forward;

            Vector3 worldHeading = _terrain.transform.TransformDirection(localHeading.normalized);
            worldHeading.y = 0f;
            if (worldHeading.sqrMagnitude <= 0.000001f)
            {
                worldHeading = _terrain.transform.forward;
                worldHeading.y = 0f;
            }
            worldHeading.Normalize();

            instance.HeadingWorld = worldHeading;
            Quaternion worldRotation = BuildGroundedRotation(instance);

            instance.SecondsSinceMotionSample += System.Math.Max(0d, simulationDeltaSeconds);
            if (instance.HasTarget)
            {
                float distance = Vector3.Distance(instance.TargetPosition, worldPosition);
                if (distance > 0.0001f)
                {
                    double sampleSeconds = System.Math.Max(0.000001d, instance.SecondsSinceMotionSample);
                    instance.SpeedMetersPerSecond = distance / (float)sampleSeconds;
                    instance.SecondsSinceMotionSample = 0d;
                }
                else if (instance.SecondsSinceMotionSample >= 0.5d)
                {
                    instance.SpeedMetersPerSecond = 0f;
                }

                if (agent.CumulativeFoodConsumedKg > instance.LastConsumedKg + 0.00001f)
                    instance.EatHoldSeconds = Mathf.Max(instance.EatHoldSeconds, 0.8f);
            }
            else
            {
                instance.GameObject.transform.SetPositionAndRotation(worldPosition, worldRotation);
                instance.SpeedMetersPerSecond = 0f;
                instance.LastConsumedKg = agent.CumulativeFoodConsumedKg;
                instance.SecondsSinceMotionSample = 0d;
                instance.HasTarget = true;
            }

            instance.TargetPosition = worldPosition;
            instance.LastConsumedKg = agent.CumulativeFoodConsumedKg;
            instance.LastAgent = agent;
        }

        private void SnapToTerrain(Instance instance, ref Vector3 worldPosition, out Vector3 terrainNormal)
        {
            TerrainData data = _terrain.terrainData;
            if (data == null)
            {
                terrainNormal = _terrain.transform.up;
                return;
            }

            Vector3 local = _terrain.transform.InverseTransformPoint(worldPosition);
            float u = Mathf.Clamp01(local.x / Mathf.Max(0.001f, data.size.x));
            float v = Mathf.Clamp01(local.z / Mathf.Max(0.001f, data.size.z));
            float height = data.GetInterpolatedHeight(u, v);

            Vector3 surfaceLocal = new(local.x, height, local.z);
            Vector3 surfaceWorld = _terrain.transform.TransformPoint(surfaceLocal);
            terrainNormal = _terrain.transform.TransformDirection(data.GetInterpolatedNormal(u, v)).normalized;

            worldPosition = surfaceWorld + Vector3.up * (instance.Rig.GroundOffset + GroundClearanceMeters);
        }

        private static Quaternion BuildGroundedRotation(Instance instance)
        {
            // Until the legs have terrain IK, keep the body upright. Rotating the whole
            // animal to the terrain normal drives downhill legs and the belly into slopes.
            Vector3 heading = instance.HeadingWorld;
            heading.y = 0f;
            if (heading.sqrMagnitude <= 0.000001f)
            {
                heading = instance.GameObject.transform.forward;
                heading.y = 0f;
            }
            if (heading.sqrMagnitude <= 0.000001f) heading = Vector3.forward;
            heading.Normalize();

            Quaternion desiredForward = Quaternion.LookRotation(heading, Vector3.up);
            Quaternion modelForward = Quaternion.LookRotation(instance.Rig.ModelForwardLocal, Vector3.up);
            return desiredForward * Quaternion.Inverse(modelForward);
        }

        private static DoeAnimationState DetermineState(Instance instance)
        {
            if (!instance.LastAgent.Alive) return DoeAnimationState.Dead;
            if (instance.SpeedMetersPerSecond > 0.08f) return DoeAnimationState.Move;
            if (instance.EatHoldSeconds > 0f) return DoeAnimationState.Eat;
            return DoeAnimationState.Idle;
        }

        private static void CaptureBindPose(Instance instance)
        {
            DoePresentationRig rig = instance.Rig;
            instance.PelvisPosition = rig.Pelvis.localPosition;
            instance.PelvisRotation = rig.Pelvis.localRotation;
            instance.Spine1Rotation = rig.Spine1.localRotation;
            instance.Spine2Rotation = rig.Spine2.localRotation;
            instance.Neck1Rotation = rig.Neck1.localRotation;
            instance.Neck2Rotation = rig.Neck2.localRotation;
            instance.HeadRotation = rig.Head.localRotation;
            instance.FrontUpperLRotation = rig.FrontUpperL.localRotation;
            instance.FrontLowerLRotation = rig.FrontLowerL.localRotation;
            instance.FrontUpperRRotation = rig.FrontUpperR.localRotation;
            instance.FrontLowerRRotation = rig.FrontLowerR.localRotation;
            instance.HindUpperLRotation = rig.HindUpperL.localRotation;
            instance.HindLowerLRotation = rig.HindLowerL.localRotation;
            instance.HindUpperRRotation = rig.HindUpperR.localRotation;
            instance.HindLowerRRotation = rig.HindLowerR.localRotation;
        }

        private static void ResetPose(Instance instance)
        {
            DoePresentationRig rig = instance.Rig;
            if (rig == null) return;

            rig.Pelvis.localPosition = instance.PelvisPosition;
            rig.Pelvis.localRotation = instance.PelvisRotation;
            rig.Spine1.localRotation = instance.Spine1Rotation;
            rig.Spine2.localRotation = instance.Spine2Rotation;
            rig.Neck1.localRotation = instance.Neck1Rotation;
            rig.Neck2.localRotation = instance.Neck2Rotation;
            rig.Head.localRotation = instance.HeadRotation;
            rig.FrontUpperL.localRotation = instance.FrontUpperLRotation;
            rig.FrontLowerL.localRotation = instance.FrontLowerLRotation;
            rig.FrontUpperR.localRotation = instance.FrontUpperRRotation;
            rig.FrontLowerR.localRotation = instance.FrontLowerRRotation;
            rig.HindUpperL.localRotation = instance.HindUpperLRotation;
            rig.HindLowerL.localRotation = instance.HindLowerLRotation;
            rig.HindUpperR.localRotation = instance.HindUpperRRotation;
            rig.HindLowerR.localRotation = instance.HindLowerRRotation;
        }

        private void ApplyProceduralPose(Instance instance, DoeAnimationState state)
        {
            ResetPose(instance);
            DoePresentationRig rig = instance.Rig;

            if (state == DoeAnimationState.Move)
            {
                float phase = instance.GaitPhase01 * Mathf.PI * 2f;
                float a = Mathf.Sin(phase);
                float b = -a;
                float upper = rig.WalkUpperLegDegrees;
                float lower = rig.WalkLowerLegDegrees;
                Vector3 lateralWorld = GetModelLateralWorld(instance);

                SetPitch(rig.FrontUpperL, instance.FrontUpperLRotation, a * upper, lateralWorld);
                SetPitch(rig.FrontUpperR, instance.FrontUpperRRotation, b * upper, lateralWorld);
                SetPitch(rig.HindUpperL, instance.HindUpperLRotation, b * upper, lateralWorld);
                SetPitch(rig.HindUpperR, instance.HindUpperRRotation, a * upper, lateralWorld);

                SetPitch(rig.FrontLowerL, instance.FrontLowerLRotation, Mathf.Max(0f, -a) * lower, lateralWorld);
                SetPitch(rig.FrontLowerR, instance.FrontLowerRRotation, Mathf.Max(0f, -b) * lower, lateralWorld);
                SetPitch(rig.HindLowerL, instance.HindLowerLRotation, Mathf.Max(0f, -b) * lower, lateralWorld);
                SetPitch(rig.HindLowerR, instance.HindLowerRRotation, Mathf.Max(0f, -a) * lower, lateralWorld);

                rig.Pelvis.localPosition = instance.PelvisPosition + Vector3.up * (Mathf.Sin(phase * 2f) * 0.018f);
                SetPitch(rig.Spine2, instance.Spine2Rotation, Mathf.Sin(phase * 2f) * 1.5f, lateralWorld);
            }
            else if (state == DoeAnimationState.Eat)
            {
                Vector3 forward = instance.GameObject.transform.TransformDirection(rig.ModelForwardLocal);
                forward.y = 0f;
                if (forward.sqrMagnitude <= 0.000001f) forward = instance.GameObject.transform.forward;
                forward.Normalize();

                Vector3 target = instance.GameObject.transform.position + forward * 0.58f;
                target.y = SampleTerrainWorldHeight(target) + 0.07f;

                // Aim the actual neck chain at forage instead of assuming a particular
                // imported bone axis. This keeps the graze in the sagittal plane.
                AimBoneAtTarget(rig.Neck1, rig.Neck2, target, rig.EatNeck1Degrees);
                AimBoneAtTarget(rig.Neck2, rig.Head, target, rig.EatNeck2Degrees);

                float nibble = Mathf.Sin(instance.AnimationTime * 3.1f) * 1.5f;
                SetPitch(rig.Head, rig.Head.localRotation, nibble, GetModelLateralWorld(instance));
            }
            else if (state == DoeAnimationState.Idle)
            {
                float breath = Mathf.Sin(instance.AnimationTime * 1.35f);
                rig.Pelvis.localPosition = instance.PelvisPosition + Vector3.up * (breath * 0.004f);
                SetPitch(rig.Spine2, instance.Spine2Rotation, breath * 0.8f, GetModelLateralWorld(instance));
                rig.Head.localRotation = instance.HeadRotation * Quaternion.AngleAxis(
                    Mathf.Sin(instance.AnimationTime * 0.55f) * 1.5f,
                    Vector3.up);
            }
        }

        private static Vector3 GetModelLateralWorld(Instance instance)
        {
            Vector3 forward = instance.Rig.ModelForwardLocal;
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.000001f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 lateralLocal = Vector3.Cross(Vector3.up, forward);
            if (lateralLocal.sqrMagnitude <= 0.000001f) lateralLocal = Vector3.right;
            return instance.GameObject.transform.TransformDirection(lateralLocal.normalized);
        }

        private static void SetPitch(Transform bone, Quaternion bindRotation, float degrees, Vector3 lateralWorld)
        {
            if (bone == null) return;
            Vector3 axisLocal = bone.InverseTransformDirection(lateralWorld);
            if (axisLocal.sqrMagnitude <= 0.000001f) axisLocal = Vector3.right;
            bone.localRotation = bindRotation * Quaternion.AngleAxis(degrees, axisLocal.normalized);
        }

        private static void AimBoneAtTarget(Transform bone, Transform child, Vector3 targetWorld, float maxDegrees)
        {
            if (bone == null || child == null) return;

            Vector3 current = child.position - bone.position;
            Vector3 desired = targetWorld - bone.position;
            if (current.sqrMagnitude <= 0.000001f || desired.sqrMagnitude <= 0.000001f) return;

            Quaternion delta = Quaternion.FromToRotation(current.normalized, desired.normalized);
            Quaternion desiredRotation = delta * bone.rotation;
            bone.rotation = Quaternion.RotateTowards(bone.rotation, desiredRotation, Mathf.Max(0f, maxDegrees));
        }

        private void LiftHoovesOutOfTerrain(Instance instance)
        {
            DoePresentationRig rig = instance.Rig;
            float requiredLift = 0f;

            AccumulateHoofLift(rig.FrontHoofL, ref requiredLift);
            AccumulateHoofLift(rig.FrontHoofR, ref requiredLift);
            AccumulateHoofLift(rig.HindHoofL, ref requiredLift);
            AccumulateHoofLift(rig.HindHoofR, ref requiredLift);

            if (requiredLift > 0f)
                instance.GameObject.transform.position += Vector3.up * requiredLift;
        }

        private void AccumulateHoofLift(Transform hoof, ref float requiredLift)
        {
            if (hoof == null) return;
            float ground = SampleTerrainWorldHeight(hoof.position);
            float penetration = ground + GroundClearanceMeters - hoof.position.y;
            if (penetration > requiredLift) requiredLift = penetration;
        }

        private float SampleTerrainWorldHeight(Vector3 worldPosition)
        {
            TerrainData data = _terrain.terrainData;
            if (data == null) return worldPosition.y;

            Vector3 local = _terrain.transform.InverseTransformPoint(worldPosition);
            float u = Mathf.Clamp01(local.x / Mathf.Max(0.001f, data.size.x));
            float v = Mathf.Clamp01(local.z / Mathf.Max(0.001f, data.size.z));
            float localHeight = data.GetInterpolatedHeight(u, v);
            return _terrain.transform.TransformPoint(new Vector3(local.x, localHeight, local.z)).y;
        }

        private static void ApplyTint(Instance instance, float hunger01)
        {
            Renderer[] renderers = instance.Rig.Renderers;
            if (renderers == null) return;

            Color tint = Color.Lerp(Color.white, new Color(1f, 0.45f, 0.25f, 1f), Mathf.Clamp01(hunger01) * 0.45f);
            instance.MaterialBlock.SetColor("_BaseColor", tint);
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].SetPropertyBlock(instance.MaterialBlock);
        }
    }
}
