using UnityEngine;

namespace InfinityProject.World.Fauna.Presentation
{
    public enum DoeAnimationState : byte
    {
        Idle = 0,
        Move = 1,
        Eat = 2,
        Sleep = 3,
        Dead = 4
    }

    [DisallowMultipleComponent]
    public sealed class FaunaPresentationAgent : MonoBehaviour
    {
        [SerializeField] private int _agentId;
        [SerializeField] private int _herdId;
        [SerializeField] private DoeAnimationState _animationState;
        [SerializeField] private float _speedMetersPerSecond;
        [SerializeField] private float _hunger01;
        [SerializeField] private float _energy01;
        [SerializeField] private float _health01;
        [SerializeField] private float _cumulativeFoodConsumedKg;
        [SerializeField] private float _gaitPhase01;
        [SerializeField] private Vector3 _targetWorldPosition;
        [SerializeField] private Vector3 _headingWorld = Vector3.forward;

        public int AgentId => _agentId;
        public int HerdId => _herdId;
        public DoeAnimationState AnimationState => _animationState;
        public float SpeedMetersPerSecond => _speedMetersPerSecond;
        public float Hunger01 => _hunger01;
        public float Energy01 => _energy01;
        public float Health01 => _health01;
        public float CumulativeFoodConsumedKg => _cumulativeFoodConsumedKg;
        public float GaitPhase01 => _gaitPhase01;
        public Vector3 TargetWorldPosition => _targetWorldPosition;
        public Vector3 HeadingWorld => _headingWorld;

        internal void SetDebugState(
            FaunaAgentState agent,
            DoeAnimationState animationState,
            float speedMetersPerSecond,
            float gaitPhase01,
            Vector3 targetWorldPosition,
            Vector3 headingWorld)
        {
            _agentId = agent.Id;
            _herdId = agent.HerdId;
            _animationState = animationState;
            _speedMetersPerSecond = Mathf.Max(0f, speedMetersPerSecond);
            _hunger01 = Mathf.Clamp01(agent.Hunger01);
            _energy01 = Mathf.Clamp01(agent.Energy01);
            _health01 = Mathf.Clamp01(agent.Health01);
            _cumulativeFoodConsumedKg = Mathf.Max(0f, agent.CumulativeFoodConsumedKg);
            _gaitPhase01 = Mathf.Repeat(gaitPhase01, 1f);
            _targetWorldPosition = targetWorldPosition;
            _headingWorld = headingWorld.sqrMagnitude > 0.000001f ? headingWorld.normalized : transform.forward;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, _targetWorldPosition);
            Gizmos.DrawSphere(_targetWorldPosition, 0.08f);

            Gizmos.color = Color.yellow;
            Vector3 heading = _headingWorld.sqrMagnitude > 0.000001f ? _headingWorld.normalized : transform.forward;
            Gizmos.DrawLine(transform.position + Vector3.up * 0.8f, transform.position + Vector3.up * 0.8f + heading * 2f);
        }
    }
}
