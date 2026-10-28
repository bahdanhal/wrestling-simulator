using UnityEngine;

namespace Wrestling.Presentation
{
    /// <summary>
    /// Procedural figure sized to one wrestler's body profile: torso, head, limbs and shoes, placed
    /// from a <see cref="Pose"/> every frame. Display only.
    /// </summary>
    public sealed class WrestlerFigure : MonoBehaviour
    {
        private Transform _torso;
        private Transform _neck;
        private Transform _head;
        private Transform _upperArmL;
        private Transform _upperArmR;
        private Transform _forearmL;
        private Transform _forearmR;
        private Transform _thighL;
        private Transform _thighR;
        private Transform _shinL;
        private Transform _shinR;
        private Transform _footL;
        private Transform _footR;
        private Transform _handL;
        private Transform _handR;

        public BodyProportions Body { get; private set; }

        public void Build(BodyProportions body, Material singlet, Material skin, Material shoe)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
                if (Application.isPlaying)
                {
                    Destroy(child);
                }
                else
                {
                    DestroyImmediate(child);
                }
            }

            Body = body;
            float limb = body.LimbRadius;
            _torso = Segment("Torso", ProceduralMesh.Capsule(body.Torso, body.TorsoHalfWidth, body.TorsoHalfDepth), singlet);
            _neck = Segment("Neck", ProceduralMesh.Capsule(body.Neck + body.HeadRadius * 0.4f, limb * 1.25f, limb * 1.25f), skin);
            _head = Segment("Head", ProceduralMesh.Capsule(body.HeadRadius * 0.35f, body.HeadRadius * 0.85f, body.HeadRadius * 0.95f), skin);
            _upperArmL = Segment("UpperArmL", ProceduralMesh.Capsule(body.UpperArm, limb * 1.05f, limb * 1.05f), skin);
            _upperArmR = Segment("UpperArmR", ProceduralMesh.Capsule(body.UpperArm, limb * 1.05f, limb * 1.05f), skin);
            _forearmL = Segment("ForearmL", ProceduralMesh.Capsule(body.Forearm, limb * 0.85f, limb * 0.85f), skin);
            _forearmR = Segment("ForearmR", ProceduralMesh.Capsule(body.Forearm, limb * 0.85f, limb * 0.85f), skin);
            _handL = Segment("HandL", ProceduralMesh.Capsule(limb * 0.6f, limb * 1.0f, limb * 0.6f), skin);
            _handR = Segment("HandR", ProceduralMesh.Capsule(limb * 0.6f, limb * 1.0f, limb * 0.6f), skin);
            _thighL = Segment("ThighL", ProceduralMesh.Capsule(body.Thigh, limb * 1.55f, limb * 1.55f), singlet);
            _thighR = Segment("ThighR", ProceduralMesh.Capsule(body.Thigh, limb * 1.55f, limb * 1.55f), singlet);
            _shinL = Segment("ShinL", ProceduralMesh.Capsule(body.Shin, limb * 1.1f, limb * 1.1f), skin);
            _shinR = Segment("ShinR", ProceduralMesh.Capsule(body.Shin, limb * 1.1f, limb * 1.1f), skin);
            _footL = Segment("FootL", ProceduralMesh.Capsule(body.Foot * 0.75f, limb * 1.0f, limb * 1.3f), shoe);
            _footR = Segment("FootR", ProceduralMesh.Capsule(body.Foot * 0.75f, limb * 1.0f, limb * 1.3f), shoe);
        }

        public void Apply(in Pose pose)
        {
            Place(_torso, pose.Pelvis, pose.Neck, pose.ChestNormal);
            Place(_neck, pose.Neck - (pose.Spine * 0.02f), pose.Head, pose.ChestNormal);
            Vector3 headAxis = (pose.Head - pose.Neck).normalized;
            Place(_head, pose.Head - (headAxis * (Body.HeadRadius * 0.15f)), pose.Head + (headAxis * (Body.HeadRadius * 0.2f)), pose.ChestNormal);
            Place(_upperArmL, pose.ShoulderL, pose.ElbowL, pose.ChestNormal);
            Place(_upperArmR, pose.ShoulderR, pose.ElbowR, pose.ChestNormal);
            Place(_forearmL, pose.ElbowL, pose.HandL, pose.ChestNormal);
            Place(_forearmR, pose.ElbowR, pose.HandR, pose.ChestNormal);
            Vector3 handDirL = (pose.HandL - pose.ElbowL).normalized;
            Vector3 handDirR = (pose.HandR - pose.ElbowR).normalized;
            Place(_handL, pose.HandL, pose.HandL + (handDirL * Body.LimbRadius), pose.ChestNormal);
            Place(_handR, pose.HandR, pose.HandR + (handDirR * Body.LimbRadius), pose.ChestNormal);
            Place(_thighL, pose.HipL, pose.KneeL, pose.ChestNormal);
            Place(_thighR, pose.HipR, pose.KneeR, pose.ChestNormal);
            Place(_shinL, pose.KneeL, pose.AnkleL, pose.ChestNormal);
            Place(_shinR, pose.KneeR, pose.AnkleR, pose.ChestNormal);
            Place(_footL, pose.AnkleL, pose.ToeL, Vector3.up);
            Place(_footR, pose.AnkleR, pose.ToeR, Vector3.up);
        }

        private Transform Segment(string segmentName, Mesh mesh, Material material)
        {
            var segment = new GameObject(segmentName);
            segment.transform.SetParent(transform, false);
            segment.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = segment.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return segment.transform;
        }

        /// <summary>Points a segment's +Y from <paramref name="from"/> to <paramref name="to"/>, its +Z towards <paramref name="facing"/>.</summary>
        private static void Place(Transform segment, Vector3 from, Vector3 to, Vector3 facing)
        {
            Vector3 axis = to - from;
            if (axis.sqrMagnitude < 1e-8f)
            {
                axis = Vector3.up;
            }

            Vector3 depth = Vector3.ProjectOnPlane(facing, axis);
            if (depth.sqrMagnitude < 1e-6f)
            {
                depth = Vector3.ProjectOnPlane(Vector3.forward, axis);
            }

            segment.SetPositionAndRotation(from, Quaternion.LookRotation(depth.normalized, axis.normalized));
        }
    }
}
