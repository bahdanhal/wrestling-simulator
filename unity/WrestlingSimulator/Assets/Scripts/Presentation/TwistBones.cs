using System;
using UnityEngine;

namespace Wrestling.Presentation
{
    /// <summary>
    /// Roll-distribution bones on the forearms, upper arms and thighs (created by the character
    /// importer), so a limb's skin turns gradually along its length instead of collapsing at one
    /// joint. Each frame the axial twist of a joint is taken from the final bone rotations
    /// (swing-twist decomposition relative to the bind pose) and given to the limb's twist bone:
    /// the forearm's twist bone carries the wrist's roll, and the upper-arm and thigh twist bones
    /// hold back the shoulder's and hip's roll near the joint. Display only.
    /// </summary>
    public sealed class TwistBones
    {
        /// <summary>Suffix of the twist bones' names (the motion rig ignores bones that end with it).</summary>
        public const string Suffix = "Twist";

        /// <summary>The largest share of a limb vertex's weight moved to its twist bone.</summary>
        public const float MaxShare = 0.85f;

        /// <summary>
        /// The twisted limbs: the bone that gets a twist child, the joint at its far end, and whether
        /// the twist bone cancels the limb's own roll at its root (upper arm, thigh) rather than
        /// carrying the far joint's roll (forearm). The twist share of a vertex runs from 0 to
        /// <see cref="MaxShare"/> along the limb, towards the end whose roll it carries or cancels.
        /// </summary>
        public static readonly (string Bone, string Distal, bool CancelsRoot)[] Limbs =
        {
            ("LeftForeArm", "LeftHand", false),
            ("RightForeArm", "RightHand", false),
            ("LeftArm", "LeftForeArm", true),
            ("RightArm", "RightForeArm", true),
            ("LeftUpLeg", "LeftLeg", true),
            ("RightUpLeg", "RightLeg", true),
        };

        private readonly Link[] _links;

        private struct Link
        {
            public Transform Twist;
            public Transform Bone;
            public Transform Other;
            public Quaternion RelativeBind;
            public Quaternion TwistBind;
            public Vector3 MeasureAxis;
            public Vector3 TwistAxis;
            public bool CancelsRoot;
        }

        /// <param name="find">Finds a rig bone by its name without the Mixamo prefix, or returns null.</param>
        /// <remarks>Build it while the rig is still in its bind pose.</remarks>
        public TwistBones(Func<string, Transform> find)
        {
            _links = new Link[Limbs.Length];
            int count = 0;
            foreach ((string boneName, string distalName, bool cancelsRoot) in Limbs)
            {
                Transform twist = find(boneName + Suffix);
                Transform bone = find(boneName);
                Transform distal = find(distalName);
                if (twist == null || bone == null || distal == null || bone.parent == null)
                {
                    throw new InvalidOperationException($"Character rig has no twist bone for '{boneName}'; re-import the character models.");
                }

                Vector3 axisInBone = (Quaternion.Inverse(bone.rotation) * (distal.position - bone.position)).normalized;
                var link = new Link
                {
                    Twist = twist,
                    Bone = bone,
                    TwistBind = twist.localRotation,
                    TwistAxis = axisInBone,
                    CancelsRoot = cancelsRoot,
                };

                if (cancelsRoot)
                {
                    // Roll of the limb relative to the bone it hangs from, about the limb's bind axis there.
                    link.Other = bone.parent;
                    link.RelativeBind = Quaternion.Inverse(bone.parent.rotation) * bone.rotation;
                    link.MeasureAxis = link.RelativeBind * axisInBone;
                }
                else
                {
                    // Roll of the far joint relative to this bone, about this bone's axis.
                    link.Other = distal;
                    link.RelativeBind = Quaternion.Inverse(bone.rotation) * distal.rotation;
                    link.MeasureAxis = axisInBone;
                }

                _links[count++] = link;
            }
        }

        /// <summary>Sets every twist bone from the current rotations of its limb.</summary>
        public void Update()
        {
            for (int i = 0; i < _links.Length; i++)
            {
                ref Link link = ref _links[i];
                Quaternion relative = link.CancelsRoot
                    ? Quaternion.Inverse(link.Other.rotation) * link.Bone.rotation
                    : Quaternion.Inverse(link.Bone.rotation) * link.Other.rotation;
                float angle = TwistAngle(relative * Quaternion.Inverse(link.RelativeBind), link.MeasureAxis);
                link.Twist.localRotation = Quaternion.AngleAxis(link.CancelsRoot ? -angle : angle, link.TwistAxis) * link.TwistBind;
            }
        }

        /// <summary>Angle in degrees of the twist part of <paramref name="rotation"/> about the unit <paramref name="axis"/> (rotation = swing × twist).</summary>
        public static float TwistAngle(Quaternion rotation, Vector3 axis)
        {
            float along = (rotation.x * axis.x) + (rotation.y * axis.y) + (rotation.z * axis.z);
            float angle = 2f * Mathf.Atan2(along, rotation.w) * Mathf.Rad2Deg;
            if (angle > 180f)
            {
                angle -= 360f;
            }
            else if (angle < -180f)
            {
                angle += 360f;
            }

            return angle;
        }
    }
}
