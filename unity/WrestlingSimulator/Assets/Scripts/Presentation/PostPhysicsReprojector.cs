using UnityEngine;

namespace Wrestling.Presentation
{
    /// <summary>
    /// The second, display-only half of the grapple solve. Before physics the pose solver sets the
    /// motor targets and the holds' anchors; the bodies then move under load and the limbs settle a
    /// few centimetres away from where the targets put them. After physics, on the bones shown:
    /// <list type="bullet">
    /// <item>each hand that holds (an engine grip's <see cref="GripLink"/>, or a throw's hold) is
    /// re-anchored by an analytical two-bone arm solve from the simulated shoulder, so its palm
    /// sits exactly on the point it holds on the opponent's simulated body, the hand keeping the
    /// orientation the physics gave it;</item>
    /// <item>each free hand posted on the mat is turned flat, palm down, fingers along the forearm's
    /// line on the mat (<see cref="Skeleton.PostedHandDirection"/>), within the wrist's range, and
    /// its palm set on the mat surface.</item>
    /// </list>
    /// Feet need no pass: each physical foot is a flat box on the mat and the shown foot is that box.
    /// Nothing here is fed back to the physics: next frame the targets are posed afresh.
    /// </summary>
    public static class PostPhysicsReprojector
    {
        /// <summary>A palm further than this (m) from its hold is being torn off it and is left as the physics has it.</summary>
        private const float MaxCorrection = 0.3f;

        /// <summary>A free hand whose wrist is this low (m) is posted on the mat; above the higher height it is not.</summary>
        private const float PostedWrist = 0.1f;
        private const float FreeWrist = 0.16f;

        /// <summary>The most (m) a posted palm is moved up or down to sit on the mat.</summary>
        private const float MaxPostCorrection = 0.08f;

        private static readonly Bone[] Arms = { Bone.LeftUpperArm, Bone.RightUpperArm };
        private static readonly Bone[] ForeArms = { Bone.LeftLowerArm, Bone.RightLowerArm };
        private static readonly Bone[] Hands = { Bone.LeftHand, Bone.RightHand };
        private static readonly string[] HandSegments = { "LeftHand", "RightHand" };

        /// <summary>What one wrestler's pass did: holding hands, and their palms' summed distance from what they hold before and after.</summary>
        public struct Result
        {
            public int Holds;
            public float GapBefore;
            public float GapAfter;
        }

        /// <summary>
        /// Re-anchors <paramref name="figure"/>'s hands after the physics step. <paramref name="holds"/>
        /// are the links holding each hand (left, right; null when the hand holds nothing), and
        /// <paramref name="free"/> says which hands are open and free to post on the mat.
        /// </summary>
        public static Result Apply(WrestlerCharacter figure, GripLink[] holds, bool[] free, Vector3 chestForward)
        {
            var result = new Result();
            CharacterRig rig = figure.Rig;
            PhysicalBody body = figure.Physical;
            if (rig == null || body == null || body.Weight <= 0f)
            {
                return result;
            }

            for (int side = 0; side < 2; side++)
            {
                GripLink link = holds[side];
                if (link != null && link.Holding)
                {
                    (float before, float after) = Hold(rig, side, link);
                    result.Holds++;
                    result.GapBefore += before;
                    result.GapAfter += after;
                }
                else if (free[side])
                {
                    Post(rig, body, side, chestForward);
                }
            }

            return result;
        }

        /// <summary>Puts the palm on the held point; returns the palm's distance from it before and after.</summary>
        private static (float Before, float After) Hold(CharacterRig rig, int side, GripLink link)
        {
            Transform hand = rig.BoneOf(Hands[side]);
            // The physics shows the hand bone at its link's rotation, so the hold's palm point sits
            // at the same offset in the bone's frame.
            Vector3 palm = hand.rotation * link.HolderAnchor;
            Vector3 held = link.HeldPoint;
            float before = (hand.position + palm - held).magnitude;
            if (before < 1e-4f || before > MaxCorrection)
            {
                return (before, before);
            }

            MoveWrist(rig, side, held - palm);
            return (before, (hand.position + palm - held).magnitude);
        }

        /// <summary>A free hand low over the mat laid flat on it, palm down, within the wrist's range.</summary>
        private static void Post(CharacterRig rig, PhysicalBody body, int side, Vector3 chestForward)
        {
            Transform hand = rig.BoneOf(Hands[side]);
            float weight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(PostedWrist, FreeWrist, hand.position.y));
            if (weight <= 0f)
            {
                return;
            }

            bool left = side == 0;
            Quaternion forearm = rig.SegmentRotation(ForeArms[side]);
            Vector3 forearmDir = forearm * Vector3.up;
            Vector3 neutral = Skeleton.NeutralPalm(forearmDir, forearm * Vector3.forward, left, rig.Calibration);
            (Vector3 direction, Vector3 palmNormal) = Skeleton.Wrist(forearmDir, neutral, Skeleton.PostedHandDirection(forearmDir, chestForward), Vector3.down, Skeleton.MaxLoadedExtension);
            Quaternion flat = Skeleton.Orientation(direction, palmNormal);
            rig.SetSegmentRotation(Hands[side], Quaternion.Slerp(rig.SegmentRotation(Hands[side]), flat, weight));

            // The palm's middle rests one hand-radius above the mat.
            Vector3 palm = hand.rotation * body.PalmAnchor(HandSegments[side]);
            float rise = (body.Radius(HandSegments[side]) - (hand.position + palm).y) * weight;
            if (Mathf.Abs(rise) > 0.002f && Mathf.Abs(rise) < MaxPostCorrection)
            {
                MoveWrist(rig, side, hand.position + (Vector3.up * rise));
            }
        }

        /// <summary>
        /// Analytical two-bone solve on the shown arm: from the shoulder as simulated, the elbow
        /// keeping its bend plane, the wrist to <paramref name="wrist"/> (as near as the arm reaches);
        /// the hand keeps its orientation in the world.
        /// </summary>
        private static void MoveWrist(CharacterRig rig, int side, Vector3 wrist)
        {
            Transform arm = rig.BoneOf(Arms[side]);
            Transform foreArm = rig.BoneOf(ForeArms[side]);
            Transform hand = rig.BoneOf(Hands[side]);
            Quaternion handRotation = hand.rotation;
            Vector3 shoulder = arm.position;
            Vector3 elbow = foreArm.position;
            Vector3 current = hand.position;
            float upper = (elbow - shoulder).magnitude;
            float lower = (current - elbow).magnitude;
            if (upper < 1e-4f || lower < 1e-4f)
            {
                return;
            }

            Vector3 pole = elbow - Vector3.Lerp(shoulder, current, 0.5f);
            Vector3 newElbow = PoseSolver.TwoBone(shoulder, wrist, upper, lower, pole, out Vector3 reached);
            arm.rotation = Quaternion.FromToRotation(elbow - shoulder, newElbow - shoulder) * arm.rotation;
            Vector3 movedElbow = foreArm.position;
            foreArm.rotation = Quaternion.FromToRotation(hand.position - movedElbow, reached - movedElbow) * foreArm.rotation;
            hand.rotation = handRotation;
        }
    }
}
