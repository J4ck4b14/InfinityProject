using UnityEngine;

namespace InfinityProject.World.Fauna.Presentation
{
    public sealed class DoePresentationRig : MonoBehaviour
    {
        [SerializeField] private Transform _root;
        [SerializeField] private Transform _pelvis;
        [SerializeField] private Transform _spine1;
        [SerializeField] private Transform _spine2;
        [SerializeField] private Transform _neck1;
        [SerializeField] private Transform _neck2;
        [SerializeField] private Transform _head;

        [SerializeField] private Transform _frontUpperL;
        [SerializeField] private Transform _frontLowerL;
        [SerializeField] private Transform _frontHoofL;
        [SerializeField] private Transform _frontUpperR;
        [SerializeField] private Transform _frontLowerR;
        [SerializeField] private Transform _frontHoofR;
        [SerializeField] private Transform _hindUpperL;
        [SerializeField] private Transform _hindLowerL;
        [SerializeField] private Transform _hindHoofL;
        [SerializeField] private Transform _hindUpperR;
        [SerializeField] private Transform _hindLowerR;
        [SerializeField] private Transform _hindHoofR;

        [SerializeField] private Renderer[] _renderers;
        [SerializeField] private Vector3 _modelForwardLocal = Vector3.forward;
        [SerializeField] private float _groundOffset;

        [Header("Procedural pose")]
        [SerializeField, Min(0.2f)] private float _strideLengthMeters = 1.25f;
        [SerializeField, Range(1f, 45f)] private float _walkUpperLegDegrees = 22f;
        [SerializeField, Range(0f, 45f)] private float _walkLowerLegDegrees = 18f;
        [SerializeField, Range(0f, 60f)] private float _eatNeck1Degrees = 22f;
        [SerializeField, Range(0f, 60f)] private float _eatNeck2Degrees = 34f;

        public Transform Root => _root;
        public Transform Pelvis => _pelvis;
        public Transform Spine1 => _spine1;
        public Transform Spine2 => _spine2;
        public Transform Neck1 => _neck1;
        public Transform Neck2 => _neck2;
        public Transform Head => _head;

        public Transform FrontUpperL => _frontUpperL;
        public Transform FrontLowerL => _frontLowerL;
        public Transform FrontHoofL => _frontHoofL;
        public Transform FrontUpperR => _frontUpperR;
        public Transform FrontLowerR => _frontLowerR;
        public Transform FrontHoofR => _frontHoofR;
        public Transform HindUpperL => _hindUpperL;
        public Transform HindLowerL => _hindLowerL;
        public Transform HindHoofL => _hindHoofL;
        public Transform HindUpperR => _hindUpperR;
        public Transform HindLowerR => _hindLowerR;
        public Transform HindHoofR => _hindHoofR;

        public Renderer[] Renderers => _renderers;
        public Vector3 ModelForwardLocal => _modelForwardLocal;
        public float GroundOffset => _groundOffset;
        public float StrideLengthMeters => Mathf.Max(0.2f, _strideLengthMeters);
        public float WalkUpperLegDegrees => _walkUpperLegDegrees;
        public float WalkLowerLegDegrees => _walkLowerLegDegrees;
        public float EatNeck1Degrees => _eatNeck1Degrees;
        public float EatNeck2Degrees => _eatNeck2Degrees;

        public void Configure(
            Transform root,
            Transform pelvis,
            Transform spine1,
            Transform spine2,
            Transform neck1,
            Transform neck2,
            Transform head,
            Transform frontUpperL,
            Transform frontLowerL,
            Transform frontHoofL,
            Transform frontUpperR,
            Transform frontLowerR,
            Transform frontHoofR,
            Transform hindUpperL,
            Transform hindLowerL,
            Transform hindHoofL,
            Transform hindUpperR,
            Transform hindLowerR,
            Transform hindHoofR,
            Renderer[] renderers,
            Vector3 modelForwardLocal,
            float groundOffset)
        {
            _root = root;
            _pelvis = pelvis;
            _spine1 = spine1;
            _spine2 = spine2;
            _neck1 = neck1;
            _neck2 = neck2;
            _head = head;
            _frontUpperL = frontUpperL;
            _frontLowerL = frontLowerL;
            _frontHoofL = frontHoofL;
            _frontUpperR = frontUpperR;
            _frontLowerR = frontLowerR;
            _frontHoofR = frontHoofR;
            _hindUpperL = hindUpperL;
            _hindLowerL = hindLowerL;
            _hindHoofL = hindHoofL;
            _hindUpperR = hindUpperR;
            _hindLowerR = hindLowerR;
            _hindHoofR = hindHoofR;
            _renderers = renderers;
            _modelForwardLocal = modelForwardLocal;
            _groundOffset = groundOffset;
        }
    }
}
