using System.Collections.Generic;
using UnityEngine;
using Wrestling.Core.Match;
using Wrestling.Core.Wrestlers;
using Wrestling.Data.Animation;

namespace Wrestling.Presentation
{
    /// <summary>Segment lengths in metres, derived from a wrestler's height, leg ratio and wingspan.</summary>
    public readonly struct BodyProportions
    {
        public BodyProportions(BodyProfile body)
        {
            Height = body.HeightCm / 100f;
            float wingspan = body.WingspanCm / 100f;
            float leg = Height * body.LegRatio.ToFloat();
            Thigh = leg * 0.52f;
            Shin = leg * 0.48f;
            Neck = Height * 0.04f;
            HeadRadius = Height * 0.065f;
            Torso = Height - leg - Neck - (HeadRadius * 2f);
            ShoulderHalfWidth = Height * 0.115f;
            HipHalfWidth = Height * 0.055f;
            float arm = (wingspan - (2f * ShoulderHalfWidth)) / 2f;
            UpperArm = arm * 0.44f;
            Forearm = arm * 0.39f;
            Foot = Height * 0.14f;
            LimbRadius = Height * 0.032f;
            TorsoHalfWidth = Height * 0.095f;
            TorsoHalfDepth = Height * 0.065f;
            ShoulderDrop = Height * 0.03f;
        }

        private BodyProportions(float height, float thigh, float shin, float torso, float neck, float shoulderHalfWidth, float shoulderDrop,
            float hipHalfWidth, float upperArm, float forearm, float foot)
        {
            Height = height;
            Thigh = thigh;
            Shin = shin;
            Torso = torso;
            Neck = neck;
            HeadRadius = height * 0.065f;
            ShoulderHalfWidth = shoulderHalfWidth;
            ShoulderDrop = shoulderDrop;
            HipHalfWidth = hipHalfWidth;
            UpperArm = upperArm;
            Forearm = forearm;
            Foot = foot;
            LimbRadius = height * 0.032f;
            TorsoHalfWidth = height * 0.095f;
            TorsoHalfDepth = height * 0.065f;
        }

        /// <summary>Proportions measured from a character model's bind pose, so poses fit that model's limbs exactly.</summary>
        public static BodyProportions FromBindPose(in Pose bind, float height)
        {
            Vector3 shoulderMid = (bind.ShoulderL + bind.ShoulderR) * 0.5f;
            return new BodyProportions(
                height,
                thigh: Vector3.Distance(bind.HipL, bind.KneeL),
                shin: Vector3.Distance(bind.KneeL, bind.AnkleL),
                torso: Vector3.Distance(bind.Pelvis, bind.Neck),
                neck: Vector3.Distance(bind.Neck, bind.Head) * 0.4f,
                shoulderHalfWidth: Vector3.Distance(bind.ShoulderL, bind.ShoulderR) * 0.5f,
                shoulderDrop: Mathf.Max(0f, Vector3.Dot(bind.Neck - shoulderMid, bind.Spine)),
                hipHalfWidth: Vector3.Distance(bind.HipL, bind.HipR) * 0.5f,
                upperArm: Vector3.Distance(bind.ShoulderL, bind.ElbowL),
                forearm: Vector3.Distance(bind.ElbowL, bind.HandL),
                foot: Vector3.Distance(bind.AnkleL, bind.ToeL) / 0.75f);
        }

        public float Height { get; }

        /// <summary>How far the shoulder joints sit below the neck along the spine.</summary>
        public float ShoulderDrop { get; }

        public float Thigh { get; }

        public float Shin { get; }

        public float Torso { get; }

        public float Neck { get; }

        public float HeadRadius { get; }

        public float ShoulderHalfWidth { get; }

        public float HipHalfWidth { get; }

        public float UpperArm { get; }

        public float Forearm { get; }

        public float Foot { get; }

        public float LimbRadius { get; }

        public float TorsoHalfWidth { get; }

        public float TorsoHalfDepth { get; }
    }

    /// <summary>Joint positions of one figure in world space.</summary>
    public struct Pose
    {
        public Vector3 Pelvis;
        public Vector3 Neck;
        public Vector3 Head;

        /// <summary>Direction the chest faces.</summary>
        public Vector3 ChestNormal;

        /// <summary>Unit vector from pelvis to neck.</summary>
        public Vector3 Spine;

        /// <summary>Unit vector towards the wrestler's left side.</summary>
        public Vector3 Left;

        public Vector3 ShoulderL;
        public Vector3 ShoulderR;
        public Vector3 ElbowL;
        public Vector3 ElbowR;
        public Vector3 HandL;
        public Vector3 HandR;
        public Vector3 HipL;
        public Vector3 HipR;
        public Vector3 KneeL;
        public Vector3 KneeR;
        public Vector3 AnkleL;
        public Vector3 AnkleR;
        public Vector3 ToeL;
        public Vector3 ToeR;

        public Vector3 Waist => Vector3.Lerp(Pelvis, Neck, 0.3f);

        public Vector3 Chest => Vector3.Lerp(Pelvis, Neck, 0.7f);
    }

    /// <summary>The wrestler's role in the current bout position, which shapes the pose.</summary>
    public enum PoseRole
    {
        Neutral,
        Top,
        Bottom,
    }

    /// <summary>
    /// Procedural posing from the engine's body state. The simulation owns every outcome; this only
    /// derives a readable figure from the state (level, posture, ground state, torso rotation, feet,
    /// grips). Motion-matched animation will replace the procedural limbs; the inputs stay the same.
    /// </summary>
    public static class PoseSolver
    {
        private const float AnkleHeight = 0.07f;
        private const float KneeOnMatHeight = 0.06f;

        /// <summary>Body and legs, with arms in their default place; hands are solved afterwards.</summary>
        public static Pose SolveBody(WrestlerFrame frame, BodyProportions body, PoseRole role)
        {
            Vector3 forward = WrestlerFrame.OnMat(frame.Facing).normalized;
            if (forward.sqrMagnitude < 1e-6f)
            {
                forward = Vector3.right;
            }

            Vector3 left = Vector3.Cross(forward, Vector3.up).normalized;
            bool lying = frame.Ground == GroundState.Prone || frame.Ground == GroundState.Supine || frame.Ground == GroundState.Bridge
                || (frame.Ground != GroundState.Standing && frame.TorsoRotation >= 45f);
            if (lying)
            {
                return Lying(frame, body, forward, left);
            }

            switch (frame.Ground)
            {
                case GroundState.Standing:
                    return Standing(frame, body, forward, left);
                case GroundState.Seated:
                    return Seated(frame, body, forward, left);
                default:
                    return Kneeling(frame, body, forward, left, role);
            }
        }

        /// <summary>Places both hands: on grip points of the opponent when gripping, otherwise in a ready or supporting position.</summary>
        public static void SolveArms(ref Pose pose, BodyProportions body, PoseRole role, WrestlerFrame frame, in Pose opponent, IEnumerable<Grip> grips)
        {
            Vector3? left = null;
            Vector3? right = null;
            foreach (Grip grip in grips)
            {
                (Vector3? l, Vector3? r) = GripTargets(grip, in pose, in opponent);
                left = l ?? left;
                right = r ?? right;
            }

            bool lying = frame.Ground == GroundState.Prone || frame.Ground == GroundState.Supine || frame.Ground == GroundState.Bridge;
            Vector3 reachForward = Vector3.ProjectOnPlane(pose.ChestNormal, Vector3.up).normalized;
            if (reachForward.sqrMagnitude < 1e-6f)
            {
                reachForward = Vector3.ProjectOnPlane(pose.Spine, Vector3.up).normalized;
            }

            Pose current = pose;
            Pose other = opponent;

            Vector3 DefaultHand(Vector3 shoulder, float side)
            {
                if (lying)
                {
                    if (frame.Ground == GroundState.Prone)
                    {
                        // Flat on the stomach: forearms on the mat in front of the shoulders.
                        Vector3 onMat = shoulder + (current.Spine * (body.Forearm * 0.9f)) + (current.Left * (side * 0.12f));
                        onMat.y = body.LimbRadius;
                        return onMat;
                    }

                    // On the back: arms out, framing to fight off the pin.
                    return shoulder + (current.Left * (side * body.UpperArm)) + (Vector3.up * 0.25f) + (current.Spine * 0.1f);
                }

                if (role == PoseRole.Top)
                {
                    Vector3 target = Vector3.Lerp(other.Waist, other.Chest, 0.5f) + (other.Left * (side * body.ShoulderHalfWidth * 0.6f));
                    return target;
                }

                if (frame.Ground != GroundState.Standing && frame.Posture < 0.5f)
                {
                    // Hands posted on the mat under the shoulders.
                    Vector3 posted = shoulder + (reachForward * 0.15f);
                    posted.y = body.LimbRadius;
                    return posted;
                }

                // Hand fighting: each hand works the opponent's facing wrist and forearm (my left
                // against their right), reaching as far as the arm allows.
                Vector3 otherForward = Vector3.ProjectOnPlane(other.ChestNormal, Vector3.up).normalized;
                Vector3 otherShoulder = side > 0f ? other.ShoulderR : other.ShoulderL;
                Vector3 opponentForearm = otherShoulder + (otherForward * ((body.UpperArm + body.Forearm) * 0.55f)) - (Vector3.up * 0.15f);
                Vector3 ready = shoulder + (reachForward * ((body.UpperArm + body.Forearm) * 0.6f)) - (Vector3.up * 0.12f) - (current.Left * (side * 0.08f));
                bool inReach = (opponentForearm - shoulder).magnitude < (body.UpperArm + body.Forearm) * 1.05f && other.Spine != Vector3.zero;
                return inReach ? opponentForearm : ready;
            }

            Vector3 handL = left ?? DefaultHand(pose.ShoulderL, 1f);
            Vector3 handR = right ?? DefaultHand(pose.ShoulderR, -1f);
            Vector3 elbowPoleL = (-Vector3.up * 0.7f) + (pose.Left * 0.6f) - (reachForward * 0.2f);
            Vector3 elbowPoleR = (-Vector3.up * 0.7f) - (pose.Left * 0.6f) - (reachForward * 0.2f);
            pose.ElbowL = TwoBone(pose.ShoulderL, handL, body.UpperArm, body.Forearm, elbowPoleL, out pose.HandL);
            pose.ElbowR = TwoBone(pose.ShoulderR, handR, body.UpperArm, body.Forearm, elbowPoleR, out pose.HandR);
            KeepAboveMat(ref pose.ElbowL, body.LimbRadius);
            KeepAboveMat(ref pose.ElbowR, body.LimbRadius);
            KeepAboveMat(ref pose.HandL, body.LimbRadius);
            KeepAboveMat(ref pose.HandR, body.LimbRadius);
        }

        /// <summary>Where a grip puts the owner's hands on the opponent (null for a hand the grip leaves free).</summary>
        private static (Vector3? Left, Vector3? Right) GripTargets(Grip grip, in Pose self, in Pose opponent)
        {
            bool leftHand = grip.Side == GripSide.Left;
            // The owner's left hand works on the opponent's right side when facing each other.
            Vector3 oppSameSide = leftHand ? opponent.ShoulderR : opponent.ShoulderL;
            Vector3 behind = -opponent.ChestNormal * 0.12f;
            switch (grip.Type)
            {
                case GripType.CollarTie:
                    return One(leftHand, opponent.Neck + behind);
                case GripType.WristControl:
                    return One(leftHand, leftHand ? opponent.HandR : opponent.HandL);
                case GripType.TwoOnOne:
                {
                    Vector3 wrist = leftHand ? opponent.HandR : opponent.HandL;
                    Vector3 elbow = leftHand ? opponent.ElbowR : opponent.ElbowL;
                    return (wrist, elbow);
                }

                case GripType.Underhook:
                    return One(leftHand, Vector3.Lerp(oppSameSide, opponent.Chest, 0.3f) - (opponent.Spine * 0.12f) + behind);
                case GripType.Overhook:
                    return One(leftHand, Vector3.Lerp(oppSameSide, leftHand ? opponent.ElbowR : opponent.ElbowL, 0.5f) + behind);
                case GripType.BodyLock:
                case GripType.GutWrenchLock:
                    return (opponent.Waist + (opponent.Left * 0.05f) + behind, opponent.Waist - (opponent.Left * 0.05f) + behind);
                case GripType.WaistFromBehind:
                    return (opponent.Waist + (opponent.ChestNormal * 0.1f) + (opponent.Left * 0.08f), opponent.Waist + (opponent.ChestNormal * 0.1f) - (opponent.Left * 0.08f));
                case GripType.FrontHeadlock:
                    return (opponent.Neck + (opponent.Left * 0.06f) + behind, opponent.Neck - (opponent.Left * 0.06f) + behind);
                case GripType.HeadAndArm:
                    return leftHand ? ((Vector3?)(opponent.Neck + behind), (Vector3?)opponent.ElbowL) : (opponent.ElbowR, opponent.Neck + behind);
                case GripType.SingleLeg:
                {
                    Vector3 knee = leftHand ? opponent.KneeR : opponent.KneeL;
                    return (knee + (opponent.Left * 0.05f), knee - (opponent.Left * 0.05f));
                }

                case GripType.DoubleLeg:
                    return (opponent.KneeR - (opponent.ChestNormal * 0.08f), opponent.KneeL - (opponent.ChestNormal * 0.08f));
                case GripType.Ankle:
                    return One(leftHand, leftHand ? opponent.AnkleR : opponent.AnkleL);
                case GripType.Crotch:
                    return One(leftHand, opponent.Pelvis - (Vector3.up * 0.1f));
                case GripType.LegLace:
                    return (opponent.AnkleR, opponent.AnkleL);
                default:
                    return (null, null);
            }
        }

        private static (Vector3? Left, Vector3? Right) One(bool leftHand, Vector3 target) => leftHand ? ((Vector3?)target, (Vector3?)null) : (null, target);

        private static Pose Standing(WrestlerFrame frame, BodyProportions body, Vector3 forward, Vector3 left)
        {
            var pose = new Pose { Left = left };
            float legLength = body.Thigh + body.Shin;
            float hipHeight = Mathf.Min(AnkleHeight + (legLength * (0.45f + (0.53f * frame.Level))), AnkleHeight + (legLength * 0.985f));
            // A wrestling stance leans in even at full posture: chest over the knees, ready to react.
            float lean = Mathf.Lerp(72f, 26f, frame.Posture) + ((1f - frame.Level) * 18f) + ((1f - frame.Balance) * 12f);
            pose.Pelvis = WrestlerFrame.OnMat(frame.Position, hipHeight) - (forward * (Mathf.Sin(lean * Mathf.Deg2Rad) * 0.12f));
            pose.Spine = Tilt(forward, lean);
            pose.ChestNormal = Vector3.Cross(pose.Spine, left).normalized;
            FinishUpperBody(ref pose, body);

            pose.HipL = pose.Pelvis + (left * body.HipHalfWidth);
            pose.HipR = pose.Pelvis - (left * body.HipHalfWidth);
            pose.AnkleL = WrestlerFrame.OnMat(frame.LeftFoot, AnkleHeight);
            pose.AnkleR = WrestlerFrame.OnMat(frame.RightFoot, AnkleHeight);
            pose.KneeL = TwoBone(pose.HipL, pose.AnkleL, body.Thigh, body.Shin, forward + (left * 0.2f), out pose.AnkleL);
            pose.KneeR = TwoBone(pose.HipR, pose.AnkleR, body.Thigh, body.Shin, forward - (left * 0.2f), out pose.AnkleR);
            pose.ToeL = Toe(pose.AnkleL, forward, body);
            pose.ToeR = Toe(pose.AnkleR, forward, body);
            return pose;
        }

        private static Pose Kneeling(WrestlerFrame frame, BodyProportions body, Vector3 forward, Vector3 left, PoseRole role)
        {
            var pose = new Pose { Left = left };
            Vector3 centre = WrestlerFrame.OnMat(frame.Position);
            float rise = Mathf.Clamp01(frame.Level / 0.6f);
            float pelvisHeight = KneeOnMatHeight + (body.Thigh * Mathf.Lerp(0.55f, 0.97f, rise));
            float back = Mathf.Sqrt(Mathf.Max(0f, (body.Thigh * body.Thigh) - Mathf.Pow(pelvisHeight - KneeOnMatHeight, 2f)));
            float posture = role == PoseRole.Top ? Mathf.Min(frame.Posture, 0.35f) : frame.Posture;
            float lean = Mathf.Lerp(80f, 8f, posture);
            pose.Pelvis = centre + (Vector3.up * pelvisHeight) - (forward * (back * 0.5f));
            pose.Spine = Tilt(forward, lean);
            pose.ChestNormal = Vector3.Cross(pose.Spine, left).normalized;
            FinishUpperBody(ref pose, body);

            pose.HipL = pose.Pelvis + (left * body.HipHalfWidth);
            pose.HipR = pose.Pelvis - (left * body.HipHalfWidth);
            Vector3 kneeForward = forward * (back * 0.5f);
            bool oneKnee = frame.Ground == GroundState.OneKnee;
            bool leftLeads = frame.Stance != Stance.StaggeredRightLead;

            pose.KneeL = Kneel(pose.HipL, kneeForward, left * 0.04f);
            pose.KneeR = Kneel(pose.HipR, kneeForward, -left * 0.04f);
            pose.AnkleL = pose.KneeL - (forward * body.Shin) + (Vector3.up * (AnkleHeight - KneeOnMatHeight));
            pose.AnkleR = pose.KneeR - (forward * body.Shin) + (Vector3.up * (AnkleHeight - KneeOnMatHeight));
            if (oneKnee)
            {
                // Lead foot planted in front, the other knee down.
                Vector3 hip = leftLeads ? pose.HipL : pose.HipR;
                Vector3 foot = new Vector3(hip.x, AnkleHeight, hip.z) + (forward * (body.Thigh * 0.85f));
                Vector3 knee = TwoBone(hip, foot, body.Thigh, body.Shin, forward + Vector3.up, out Vector3 ankle);
                if (leftLeads)
                {
                    pose.KneeL = knee;
                    pose.AnkleL = ankle;
                }
                else
                {
                    pose.KneeR = knee;
                    pose.AnkleR = ankle;
                }
            }

            pose.ToeL = pose.AnkleL - (forward * (body.Foot * 0.6f)) - (Vector3.up * 0.04f);
            pose.ToeR = pose.AnkleR - (forward * (body.Foot * 0.6f)) - (Vector3.up * 0.04f);
            if (oneKnee)
            {
                if (leftLeads)
                {
                    pose.ToeL = Toe(pose.AnkleL, forward, body);
                }
                else
                {
                    pose.ToeR = Toe(pose.AnkleR, forward, body);
                }
            }

            return pose;
        }

        private static Pose Seated(WrestlerFrame frame, BodyProportions body, Vector3 forward, Vector3 left)
        {
            var pose = new Pose { Left = left };
            pose.Pelvis = WrestlerFrame.OnMat(frame.Position, 0.13f);
            pose.Spine = Tilt(forward, -15f);
            pose.ChestNormal = Vector3.Cross(pose.Spine, left).normalized;
            FinishUpperBody(ref pose, body);
            pose.HipL = pose.Pelvis + (left * body.HipHalfWidth);
            pose.HipR = pose.Pelvis - (left * body.HipHalfWidth);
            Vector3 reach = forward * ((body.Thigh + body.Shin) * 0.85f);
            pose.KneeL = TwoBone(pose.HipL, Flat(pose.HipL + reach, AnkleHeight), body.Thigh, body.Shin, Vector3.up, out pose.AnkleL);
            pose.KneeR = TwoBone(pose.HipR, Flat(pose.HipR + reach, AnkleHeight), body.Thigh, body.Shin, Vector3.up, out pose.AnkleR);
            pose.ToeL = pose.AnkleL + (forward * 0.05f) + (Vector3.up * (body.Foot * 0.7f));
            pose.ToeR = pose.AnkleR + (forward * 0.05f) + (Vector3.up * (body.Foot * 0.7f));
            return pose;
        }

        /// <summary>
        /// Lying along the facing direction (head towards it), rolled by the torso rotation:
        /// 0° chest down, 90° on the side, 180° on the back. A bridge lifts the back off the mat.
        /// </summary>
        private static Pose Lying(WrestlerFrame frame, BodyProportions body, Vector3 forward, Vector3 left)
        {
            var pose = new Pose();
            float rotation = frame.Ground == GroundState.Prone ? Mathf.Min(frame.TorsoRotation, 90f)
                : frame.Ground == GroundState.Supine || frame.Ground == GroundState.Bridge ? Mathf.Max(frame.TorsoRotation, 90f)
                : frame.TorsoRotation;
            float radians = rotation * Mathf.Deg2Rad;
            Vector3 chest = (-Vector3.up * Mathf.Cos(radians)) + (left * (Mathf.Sin(radians) * (frame.RollSide < 0f ? -1f : 1f)));
            pose.ChestNormal = chest.normalized;
            pose.Spine = forward;
            pose.Left = Vector3.Cross(pose.ChestNormal, pose.Spine).normalized;
            bool bridge = frame.Ground == GroundState.Bridge;
            float onSide = Mathf.Sin(radians);
            float restHeight = Mathf.Lerp(body.TorsoHalfDepth, body.ShoulderHalfWidth * 0.9f, onSide);
            Vector3 centre = WrestlerFrame.OnMat(frame.Position);
            pose.Pelvis = centre - (forward * (body.Torso * 0.5f)) + (Vector3.up * (bridge ? restHeight + 0.22f : restHeight));
            pose.Neck = centre + (forward * (body.Torso * 0.5f)) + (Vector3.up * (bridge ? restHeight + 0.12f : restHeight));
            pose.Spine = (pose.Neck - pose.Pelvis).normalized;
            pose.Head = pose.Neck + (pose.Spine * (body.Neck + body.HeadRadius)) - (Vector3.up * (bridge ? 0.18f : 0f));
            KeepAboveMat(ref pose.Head, body.HeadRadius);
            Vector3 shoulderBase = pose.Neck - (pose.Spine * body.ShoulderDrop);
            pose.ShoulderL = shoulderBase + (pose.Left * body.ShoulderHalfWidth);
            pose.ShoulderR = shoulderBase - (pose.Left * body.ShoulderHalfWidth);
            KeepAboveMat(ref pose.ShoulderL, body.LimbRadius);
            KeepAboveMat(ref pose.ShoulderR, body.LimbRadius);

            pose.HipL = pose.Pelvis + (pose.Left * body.HipHalfWidth);
            pose.HipR = pose.Pelvis - (pose.Left * body.HipHalfWidth);
            KeepAboveMat(ref pose.HipL, body.LimbRadius * 1.3f);
            KeepAboveMat(ref pose.HipR, body.LimbRadius * 1.3f);
            if (bridge)
            {
                Vector3 footL = Flat(pose.HipL - (forward * (body.Thigh * 0.9f)), AnkleHeight);
                Vector3 footR = Flat(pose.HipR - (forward * (body.Thigh * 0.9f)), AnkleHeight);
                pose.KneeL = TwoBone(pose.HipL, footL, body.Thigh, body.Shin, Vector3.up, out pose.AnkleL);
                pose.KneeR = TwoBone(pose.HipR, footR, body.Thigh, body.Shin, Vector3.up, out pose.AnkleR);
                pose.ToeL = Toe(pose.AnkleL, -forward, body);
                pose.ToeR = Toe(pose.AnkleR, -forward, body);
            }
            else
            {
                float spread = 0.08f;
                Vector3 legDirection = -forward;
                pose.KneeL = Flat(pose.HipL + (legDirection * body.Thigh) + (pose.Left * spread), body.LimbRadius);
                pose.KneeR = Flat(pose.HipR + (legDirection * body.Thigh) - (pose.Left * spread), body.LimbRadius);
                pose.AnkleL = Flat(pose.KneeL + (legDirection * body.Shin) + (pose.Left * spread), AnkleHeight);
                pose.AnkleR = Flat(pose.KneeR + (legDirection * body.Shin) - (pose.Left * spread), AnkleHeight);
                pose.ToeL = pose.AnkleL + (legDirection * 0.05f) + (pose.ChestNormal * (body.Foot * 0.6f));
                pose.ToeR = pose.AnkleR + (legDirection * 0.05f) + (pose.ChestNormal * (body.Foot * 0.6f));
                KeepAboveMat(ref pose.ToeL, 0.03f);
                KeepAboveMat(ref pose.ToeR, 0.03f);
            }

            return pose;
        }

        /// <summary>
        /// Applies the frame's whole-body lift, pitch and bank (lifts and throws) about the body's
        /// centre, keeping every joint above the mat.
        /// </summary>
        public static void ApplyWholeBody(ref Pose pose, WrestlerFrame frame, BodyProportions body)
        {
            if (frame.Lift <= 0f && Mathf.Approximately(frame.Pitch, 0f) && Mathf.Approximately(frame.Bank, 0f))
            {
                return;
            }

            Quaternion rotation = WholeBodyRotation(frame);
            Vector3 pivot = WholeBodyPivot(in pose);
            Vector3 lift = Vector3.up * frame.Lift;

            Vector3 Move(Vector3 point)
            {
                Vector3 moved = pivot + (rotation * (point - pivot)) + lift;
                if (moved.y < body.LimbRadius)
                {
                    moved.y = body.LimbRadius;
                }

                return moved;
            }

            pose.Pelvis = Move(pose.Pelvis);
            pose.Neck = Move(pose.Neck);
            pose.Head = Move(pose.Head);
            pose.ShoulderL = Move(pose.ShoulderL);
            pose.ShoulderR = Move(pose.ShoulderR);
            pose.ElbowL = Move(pose.ElbowL);
            pose.ElbowR = Move(pose.ElbowR);
            pose.HandL = Move(pose.HandL);
            pose.HandR = Move(pose.HandR);
            pose.HipL = Move(pose.HipL);
            pose.HipR = Move(pose.HipR);
            pose.KneeL = Move(pose.KneeL);
            pose.KneeR = Move(pose.KneeR);
            pose.AnkleL = Move(pose.AnkleL);
            pose.AnkleR = Move(pose.AnkleR);
            pose.ToeL = Move(pose.ToeL);
            pose.ToeR = Move(pose.ToeR);
            pose.ChestNormal = rotation * pose.ChestNormal;
            pose.Spine = (pose.Neck - pose.Pelvis).normalized;
            pose.Left = rotation * pose.Left;
        }

        /// <summary>The frame's whole-body rotation: pitch about the left axis, then bank about the facing.</summary>
        public static Quaternion WholeBodyRotation(WrestlerFrame frame)
        {
            Vector3 forward = WrestlerFrame.OnMat(frame.Facing).normalized;
            Vector3 left = Vector3.Cross(forward, Vector3.up).normalized;
            return Quaternion.AngleAxis(frame.Bank, forward) * Quaternion.AngleAxis(frame.Pitch, -left);
        }

        /// <summary>The waist point that a whole-body rotation turns about (see <see cref="ApplyWholeBody"/>).</summary>
        public static Vector3 WholeBodyPivot(in Pose pose) => Vector3.Lerp(pose.Pelvis, pose.Neck, 0.35f);

        /// <summary>A contact point on the surface of a posed body.</summary>
        public static Vector3 AnchorOn(in Pose pose, BodyProportions body, AnchorPoint point)
        {
            switch (point)
            {
                case AnchorPoint.Hips:
                    // The hip bone at the back, where a hip toss loads the opponent.
                    return pose.Pelvis - (pose.ChestNormal * (body.TorsoHalfDepth * 0.8f));
                case AnchorPoint.LowerBack:
                    return Vector3.Lerp(pose.Pelvis, pose.Neck, 0.3f) - (pose.ChestNormal * body.TorsoHalfDepth);
                case AnchorPoint.Shoulders:
                    return pose.Neck - (pose.Spine * (body.Torso * 0.08f)) - (pose.ChestNormal * (body.TorsoHalfDepth * 0.4f));
                case AnchorPoint.Chest:
                    return Vector3.Lerp(pose.Pelvis, pose.Neck, 0.65f) + (pose.ChestNormal * body.TorsoHalfDepth);
                default:
                    return WholeBodyPivot(in pose);
            }
        }

        /// <summary>Moves every joint of a pose by <paramref name="offset"/>.</summary>
        public static void Translate(ref Pose pose, Vector3 offset)
        {
            pose.Pelvis += offset;
            pose.Neck += offset;
            pose.Head += offset;
            pose.ShoulderL += offset;
            pose.ShoulderR += offset;
            pose.ElbowL += offset;
            pose.ElbowR += offset;
            pose.HandL += offset;
            pose.HandR += offset;
            pose.HipL += offset;
            pose.HipR += offset;
            pose.KneeL += offset;
            pose.KneeR += offset;
            pose.AnkleL += offset;
            pose.AnkleR += offset;
            pose.ToeL += offset;
            pose.ToeR += offset;
        }

        /// <summary>The rest pose the body mesh is built in: upright at the origin facing +Z, arms angled down.</summary>
        public static Pose BindPose(BodyProportions body)
        {
            var pose = new Pose
            {
                Left = Vector3.left,
                Spine = Vector3.up,
                ChestNormal = Vector3.forward,
            };
            float legLength = body.Thigh + body.Shin;
            pose.Pelvis = new Vector3(0f, AnkleHeight + legLength, 0f);
            FinishUpperBody(ref pose, body);
            pose.HipL = pose.Pelvis + (Vector3.left * body.HipHalfWidth);
            pose.HipR = pose.Pelvis + (Vector3.right * body.HipHalfWidth);
            pose.KneeL = pose.HipL + (Vector3.down * body.Thigh);
            pose.KneeR = pose.HipR + (Vector3.down * body.Thigh);
            pose.AnkleL = pose.KneeL + (Vector3.down * body.Shin);
            pose.AnkleR = pose.KneeR + (Vector3.down * body.Shin);
            pose.ToeL = Toe(pose.AnkleL, Vector3.forward, body);
            pose.ToeR = Toe(pose.AnkleR, Vector3.forward, body);
            Vector3 armL = Quaternion.AngleAxis(-40f, Vector3.forward) * Vector3.down;
            Vector3 armR = Quaternion.AngleAxis(40f, Vector3.forward) * Vector3.down;
            pose.ElbowL = pose.ShoulderL + (armL * body.UpperArm);
            pose.ElbowR = pose.ShoulderR + (armR * body.UpperArm);
            pose.HandL = pose.ElbowL + (armL * body.Forearm);
            pose.HandR = pose.ElbowR + (armR * body.Forearm);
            return pose;
        }

        private static void FinishUpperBody(ref Pose pose, BodyProportions body)
        {
            pose.Neck = pose.Pelvis + (pose.Spine * body.Torso);
            Vector3 headDirection = (pose.Spine + (Vector3.up * 0.6f)).normalized;
            pose.Head = pose.Neck + (headDirection * (body.Neck + body.HeadRadius));
            Vector3 shoulderBase = pose.Neck - (pose.Spine * body.ShoulderDrop);
            pose.ShoulderL = shoulderBase + (pose.Left * body.ShoulderHalfWidth);
            pose.ShoulderR = shoulderBase - (pose.Left * body.ShoulderHalfWidth);
        }

        /// <summary>Rotates "up" towards <paramref name="forward"/> by <paramref name="degrees"/> (negative leans back).</summary>
        private static Vector3 Tilt(Vector3 forward, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return ((Vector3.up * Mathf.Cos(radians)) + (forward * Mathf.Sin(radians))).normalized;
        }

        private static Vector3 Kneel(Vector3 hip, Vector3 forwardOffset, Vector3 sideOffset) =>
            Flat(hip + forwardOffset + sideOffset, KneeOnMatHeight);

        private static Vector3 Toe(Vector3 ankle, Vector3 forward, BodyProportions body) =>
            Flat(ankle + (forward * (body.Foot * 0.75f)), 0.03f);

        private static Vector3 Flat(Vector3 point, float height) => new Vector3(point.x, height, point.z);

        private static void KeepAboveMat(ref Vector3 point, float radius)
        {
            if (point.y < radius)
            {
                point.y = radius;
            }
        }

        /// <summary>
        /// Two-bone inverse kinematics: the middle joint for a chain from <paramref name="root"/>
        /// towards <paramref name="target"/>, bending towards <paramref name="pole"/>. The end point is
        /// the target, pulled in when out of reach.
        /// </summary>
        public static Vector3 TwoBone(Vector3 root, Vector3 target, float upper, float lower, Vector3 pole, out Vector3 end)
        {
            Vector3 toTarget = target - root;
            float distance = toTarget.magnitude;
            Vector3 direction = distance > 1e-5f ? toTarget / distance : Vector3.down;
            float reach = Mathf.Clamp(distance, Mathf.Abs(upper - lower) + 1e-3f, (upper + lower) - 1e-3f);
            end = root + (direction * reach);
            float cos = ((upper * upper) + (reach * reach) - (lower * lower)) / (2f * upper * reach);
            float along = upper * Mathf.Clamp(cos, -1f, 1f);
            float across = Mathf.Sqrt(Mathf.Max(0f, (upper * upper) - (along * along)));
            Vector3 bend = Vector3.ProjectOnPlane(pole, direction);
            if (bend.sqrMagnitude < 1e-6f)
            {
                bend = Vector3.ProjectOnPlane(Vector3.forward, direction);
            }

            return root + (direction * along) + (bend.normalized * across);
        }
    }
}
