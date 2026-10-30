using System;
using UnityEngine;

namespace Wrestling.Presentation
{
    /// <summary>
    /// Pose-space corrective blend shapes for the shoulders and hips. The character importer bakes
    /// each shape at a key pose (arm raised, arm across the chest, hip flexed) as the difference
    /// between volume-preserving and linear skinning there; at runtime this class measures the
    /// same angles from the final bones and fades the shapes in. Shape names, key poses and the
    /// angle measures are defined once here for both. Display only.
    /// </summary>
    public sealed class CorrectiveShapes
    {
        public const string ShoulderRaiseLeft = "PSD_ShoulderRaise_L";
        public const string ShoulderRaiseRight = "PSD_ShoulderRaise_R";
        public const string ShoulderAcrossLeft = "PSD_ShoulderAcross_L";
        public const string ShoulderAcrossRight = "PSD_ShoulderAcross_R";
        public const string HipFlexLeft = "PSD_HipFlex_L";
        public const string HipFlexRight = "PSD_HipFlex_R";

        /// <summary>Arm elevation from hanging (degrees) at which the raise shape is baked.</summary>
        public const float RaiseKeyElevation = 125f;

        /// <summary>Arm elevation and horizontal angle from straight out sideways (degrees, 90 = forward) of the across key pose.</summary>
        public const float AcrossKeyElevation = 90f;

        public const float AcrossKeyAzimuth = 115f;

        /// <summary>Thigh flexion from hanging (degrees) at which the hip shape is baked.</summary>
        public const float HipFlexKey = 95f;

        private const float Epsilon = 0.25f;

        private readonly SkinnedMeshRenderer _renderer;
        private readonly Transform _chest;
        private readonly Transform _hips;
        private readonly Side _left;
        private readonly Side _right;
        private readonly Vector3 _chestUp;
        private readonly Vector3 _chestLeft;
        private readonly Vector3 _chestForward;
        private readonly Vector3 _hipsUp;
        private readonly Vector3 _hipsForward;

        private sealed class Side
        {
            public Transform Arm;
            public Transform ForeArm;
            public Transform Thigh;
            public Transform Knee;
            public float Sign;
            public int Raise;
            public int Across;
            public int Hip;
            public float RaiseWeight = -1f;
            public float AcrossWeight = -1f;
            public float HipWeight = -1f;
        }

        /// <param name="find">Finds a rig bone by its name without the Mixamo prefix, or returns null.</param>
        /// <remarks>Build it while the rig is still in its bind pose.</remarks>
        public CorrectiveShapes(Func<string, Transform> find, SkinnedMeshRenderer renderer)
        {
            Transform Required(string name) => find(name) ?? throw new InvalidOperationException($"Character rig has no bone '{name}'.");

            _renderer = renderer;
            _chest = Required("Spine2");
            _hips = Required("Hips");
            Mesh mesh = renderer.sharedMesh;
            int Shape(string name)
            {
                int index = mesh.GetBlendShapeIndex(name);
                return index >= 0 ? index : throw new InvalidOperationException($"Body mesh has no corrective shape '{name}'; re-import the character models.");
            }

            Side Make(string side, float sign, string raise, string across, string hip) => new Side
            {
                Arm = Required(side + "Arm"),
                ForeArm = Required(side + "ForeArm"),
                Thigh = Required(side + "UpLeg"),
                Knee = Required(side + "Leg"),
                Sign = sign,
                Raise = Shape(raise),
                Across = Shape(across),
                Hip = Shape(hip),
            };

            _left = Make("Left", 1f, ShoulderRaiseLeft, ShoulderAcrossLeft, HipFlexLeft);
            _right = Make("Right", -1f, ShoulderRaiseRight, ShoulderAcrossRight, HipFlexRight);
            Transform toe = Required("LeftToeBase");
            BodyFrame(_hips.position, Required("Neck").position, _left.Arm.position, _right.Arm.position, toe.position - toe.parent.position, out Vector3 up, out Vector3 left, out Vector3 forward);
            Quaternion toChest = Quaternion.Inverse(_chest.rotation);
            _chestUp = toChest * up;
            _chestLeft = toChest * left;
            _chestForward = toChest * forward;
            Quaternion toHips = Quaternion.Inverse(_hips.rotation);
            _hipsUp = toHips * up;
            _hipsForward = toHips * forward;
        }

        /// <summary>How strongly each side's hip and shoulder are bent: (hip left, hip right, shoulder left, shoulder right), 0–1.</summary>
        public Vector4 FoldDrive { get; private set; }

        /// <summary>
        /// The body's up, left and forward directions from the hips, neck, shoulders and the toes'
        /// direction (the toes point forward).
        /// </summary>
        public static void BodyFrame(Vector3 hips, Vector3 neck, Vector3 leftArm, Vector3 rightArm, Vector3 toeDirection, out Vector3 up, out Vector3 left, out Vector3 forward)
        {
            up = (neck - hips).normalized;
            left = Vector3.ProjectOnPlane(leftArm - rightArm, up).normalized;
            forward = Vector3.Cross(up, left);
            if (Vector3.Dot(forward, toeDirection) < 0f)
            {
                forward = -forward;
            }
        }

        /// <summary>Arm direction for an elevation from hanging and a horizontal angle from straight out sideways (90 = forward).</summary>
        public static Vector3 ArmDirection(float elevation, float azimuth, Vector3 up, Vector3 outward, Vector3 forward)
        {
            float e = elevation * Mathf.Deg2Rad;
            float a = azimuth * Mathf.Deg2Rad;
            Vector3 horizontal = (outward * Mathf.Cos(a)) + (forward * Mathf.Sin(a));
            return ((-up * Mathf.Cos(e)) + (horizontal * Mathf.Sin(e))).normalized;
        }

        /// <summary>Thigh direction for a flexion from hanging (degrees, forward positive).</summary>
        public static Vector3 ThighDirection(float flexion, Vector3 up, Vector3 forward)
        {
            float f = flexion * Mathf.Deg2Rad;
            return ((-up * Mathf.Cos(f)) + (forward * Mathf.Sin(f))).normalized;
        }

        /// <summary>Weights of the raise and across shapes for an arm direction in the body frame.</summary>
        public static void ShoulderWeights(Vector3 direction, Vector3 up, Vector3 outward, Vector3 forward, out float raise, out float across)
        {
            float elevation = Mathf.Acos(Mathf.Clamp(-Vector3.Dot(direction, up), -1f, 1f)) * Mathf.Rad2Deg;
            float azimuth = Mathf.Atan2(Vector3.Dot(direction, forward), Vector3.Dot(direction, outward)) * Mathf.Rad2Deg;
            across = Ramp(85f, AcrossKeyAzimuth, azimuth) * Ramp(45f, 75f, elevation) * (1f - Ramp(120f, 150f, elevation));
            raise = Ramp(60f, RaiseKeyElevation, elevation) * (1f - across);
        }

        /// <summary>Weight of the hip shape for a thigh direction in the body frame.</summary>
        public static float HipWeight(Vector3 direction, Vector3 up, Vector3 forward)
        {
            float flexion = Mathf.Atan2(Vector3.Dot(direction, forward), -Vector3.Dot(direction, up)) * Mathf.Rad2Deg;
            return Ramp(25f, HipFlexKey, flexion);
        }

        /// <summary>Sets the shapes from the current bones.</summary>
        public void Update()
        {
            Quaternion toChest = Quaternion.Inverse(_chest.rotation);
            Quaternion toHips = Quaternion.Inverse(_hips.rotation);
            float shoulderLeft = UpdateSide(_left, toChest, toHips, out float hipLeft);
            float shoulderRight = UpdateSide(_right, toChest, toHips, out float hipRight);
            FoldDrive = new Vector4(hipLeft, hipRight, shoulderLeft, shoulderRight);
        }

        private float UpdateSide(Side side, Quaternion toChest, Quaternion toHips, out float hip)
        {
            Vector3 arm = toChest * (side.ForeArm.position - side.Arm.position).normalized;
            ShoulderWeights(arm, _chestUp, _chestLeft * side.Sign, _chestForward, out float raise, out float across);
            Vector3 thigh = toHips * (side.Knee.position - side.Thigh.position).normalized;
            hip = HipWeight(thigh, _hipsUp, _hipsForward);
            Set(side.Raise, raise * 100f, ref side.RaiseWeight);
            Set(side.Across, across * 100f, ref side.AcrossWeight);
            Set(side.Hip, hip * 100f, ref side.HipWeight);
            return Mathf.Max(raise, across);
        }

        private void Set(int shape, float weight, ref float current)
        {
            if (Mathf.Abs(weight - current) > Epsilon)
            {
                current = weight;
                _renderer.SetBlendShapeWeight(shape, weight);
            }
        }

        private static float Ramp(float from, float to, float x)
        {
            float t = Mathf.Clamp01((x - from) / (to - from));
            return t * t * (3f - (2f * t));
        }
    }
}
