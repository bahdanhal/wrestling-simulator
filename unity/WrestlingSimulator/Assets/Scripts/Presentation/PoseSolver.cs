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

        /// <summary>Athletic facial focus (0 relaxed, 1 intense brow furrow and eye squint).</summary>
        public float Focus;

        /// <summary>Physical exertion (0 calm, 1 maximum effort grimace and jaw clench/breathing).</summary>
        public float Effort;

        /// <summary>World-space point the wrestler's eyes track when <see cref="HasGazeTarget"/> is true.</summary>
        public Vector3 GazeTarget;

        public bool HasGazeTarget;

        /// <summary>Direction the front of the pelvis faces; zero takes it from the hip line.</summary>
        public Vector3 PelvisNormal;

        /// <summary>Direction the face points; zero follows the chest.</summary>
        public Vector3 HeadNormal;

        /// <summary>Wrist-to-knuckles direction of each hand; zero (with a zero palm) keeps the wrist neutral.</summary>
        public Vector3 HandDirL;
        public Vector3 HandDirR;

        /// <summary>Direction each palm faces; zero (with a zero hand direction) keeps the wrist neutral.</summary>
        public Vector3 PalmNormalL;
        public Vector3 PalmNormalR;

        /// <summary>How each hand shapes itself on what it holds (open when the hand is free).</summary>
        public HandGripStyle LeftGripStyle;
        public HandGripStyle RightGripStyle;

        /// <summary>Radius of what each hand wraps (see <see cref="GripAperture"/>); zero takes the style's own.</summary>
        public float LeftGripAperture;
        public float RightGripAperture;

        /// <summary>Degrees the chest has turned towards a reach behind the back (thoracic counter-torsion), towards the left when positive.</summary>
        public float ReachTorsion;

        /// <summary>Metres each shoulder blade is drawn back for a reach behind the back.</summary>
        public float RetractionL;
        public float RetractionR;

        /// <summary>Explicit pelvis construction axis for retargeted motion; zero derives it from the thighs and trunk.</summary>
        public Vector3 PelvisUp;

        /// <summary>Keep the supplied foot direction and lower-leg roll instead of flattening low feet onto an assumed contact plane.</summary>
        public bool UnconstrainedFeet;

        /// <summary>Unit vector the pelvis faces, square to its axis: <see cref="PelvisNormal"/>, else from the hip line, else the chest's.</summary>
        public Vector3 PelvisFront
        {
            get
            {
                Vector3 axis = PelvisAxis;
                if (axis.sqrMagnitude < 1e-6f)
                {
                    axis = Vector3.up;
                }

                if (PelvisNormal.sqrMagnitude > 1e-6f)
                {
                    Vector3 given = Vector3.ProjectOnPlane(PelvisNormal, axis);
                    if (given.sqrMagnitude > 1e-6f)
                    {
                        return given.normalized;
                    }
                }

                Vector3 hipLine = HipL - HipR;
                if (hipLine.sqrMagnitude > 1e-6f)
                {
                    Vector3 fromHips = Vector3.Cross(axis, hipLine.normalized);
                    if (fromHips.sqrMagnitude > 1e-6f)
                    {
                        return fromHips.normalized;
                    }
                }

                return ChestNormal.sqrMagnitude > 1e-6f ? ChestNormal.normalized : Vector3.forward;
            }
        }

        /// <summary>
        /// Share of the bend between thighs and trunk taken by the pelvis; the lumbar and thoracic
        /// spine take the rest. A wrestler leans by bending at the hips and rounding the back, with
        /// the pelvis tipping only part of the way (tipping it all the way pushes the buttocks up
        /// and back and leaves the back board-straight).
        /// </summary>
        private const float PelvisShare = 0.45f;

        /// <summary>Direction the pelvis points up the spine: between the thighs' line and the trunk's.</summary>
        public Vector3 PelvisAxis
        {
            get
            {
                if (PelvisUp.sqrMagnitude > 1e-8f)
                {
                    return PelvisUp.normalized;
                }

                Vector3 hips = (HipL + HipR) * 0.5f;
                Vector3 knees = (KneeL + KneeR) * 0.5f;
                Vector3 alongThighs = hips - knees;
                if (Spine.sqrMagnitude < 1e-6f || alongThighs.sqrMagnitude < 1e-4f || KneeL == Vector3.zero || Vector3.Dot(alongThighs.normalized, Spine) < -0.95f)
                {
                    return Spine;
                }

                return Vector3.Slerp(alongThighs.normalized, Spine, PelvisShare).normalized;
            }
        }

        /// <summary>Point on the curved spine from the pelvis (0) to the neck (1): it leaves the pelvis along its axis and bends smoothly up to the neck.</summary>
        public Vector3 SpinePoint(float t)
        {
            float length = (Neck - Pelvis).magnitude;
            Vector3 control = Pelvis + (PelvisAxis * (length * 0.5f));
            float u = 1f - t;
            return (Pelvis * (u * u)) + (control * (2f * u * t)) + (Neck * (t * t));
        }

        public Vector3 Waist => SpinePoint(0.3f);

        public Vector3 Chest => SpinePoint(0.7f);
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
        private const float AnkleHeight = ClipKinematics.AnkleHeight;
        private const float KneeOnMatHeight = ClipKinematics.KneeOnMatHeight;

        /// <summary>Body and legs, with arms in their default place; hands are solved afterwards.</summary>
        public static Pose SolveBody(WrestlerFrame frame, BodyProportions body, PoseRole role)
        {
            if (frame.GroundFrom.HasValue && frame.GroundTo.HasValue)
            {
                WrestlerFrame from = frame;
                WrestlerFrame to = frame;
                from.Ground = frame.GroundFrom.Value;
                to.Ground = frame.GroundTo.Value;
                Pose a = SolveDiscreteBody(from, body, role, frame.GroundFromLying);
                Pose b = SolveDiscreteBody(to, body, role, frame.GroundToLying);
                return BlendConstruction(in a, in b, body, Mathf.Clamp01(frame.GroundBlend));
            }

            return SolveDiscreteBody(frame, body, role);
        }

        /// <summary>
        /// Changes construction over the authored interval, keeping the trunk rigid and solving
        /// fixed-length legs to the interpolated foot contacts. Blending joint positions directly
        /// would shorten limbs; switching construction at the interval midpoint would teleport hips.
        /// </summary>
        private static Pose BlendConstruction(in Pose a, in Pose b, BodyProportions body, float t)
        {
            if (t <= 0f) return a;
            if (t >= 1f) return b;
            Quaternion trunkA = Quaternion.LookRotation(a.ChestNormal, a.Spine);
            Quaternion trunkB = Quaternion.LookRotation(b.ChestNormal, b.Spine);
            Quaternion trunk = Quaternion.Slerp(trunkA, trunkB, t);
            var pose = new Pose
            {
                Pelvis = Vector3.Lerp(a.Pelvis, b.Pelvis, t),
                Spine = trunk * Vector3.up,
                Left = trunk * Vector3.left,
                ChestNormal = trunk * Vector3.forward,
            };
            FinishUpperBody(ref pose, body);
            pose.Neck = pose.Pelvis + (pose.Spine * Mathf.Lerp((a.Neck - a.Pelvis).magnitude, (b.Neck - b.Pelvis).magnitude, t));
            Vector3 shoulderBase = pose.Neck - (pose.Spine * body.ShoulderDrop);
            pose.ShoulderL = shoulderBase + (pose.Left * body.ShoulderHalfWidth);
            pose.ShoulderR = shoulderBase - (pose.Left * body.ShoulderHalfWidth);
            Vector3 headDirection = Vector3.Slerp((a.Head - a.Neck).normalized, (b.Head - b.Neck).normalized, t).normalized;
            pose.Head = pose.Neck + (headDirection * (body.Neck + body.HeadRadius));
            KeepAboveMat(ref pose.Head, body.HeadRadius);

            Quaternion hipsA = Quaternion.LookRotation(a.PelvisFront, a.PelvisAxis);
            Quaternion hipsB = Quaternion.LookRotation(b.PelvisFront, b.PelvisAxis);
            Quaternion hips = Quaternion.Slerp(hipsA, hipsB, t);
            Vector3 hipLeft = hips * Vector3.left;
            pose.PelvisNormal = hips * Vector3.forward;
            pose.HipL = pose.Pelvis + (hipLeft * body.HipHalfWidth);
            pose.HipR = pose.Pelvis - (hipLeft * body.HipHalfWidth);

            Vector3 footL = Vector3.Lerp(a.AnkleL, b.AnkleL, t);
            Vector3 footR = Vector3.Lerp(a.AnkleR, b.AnkleR, t);
            Vector3 poleL = Vector3.Lerp(a.KneeL, b.KneeL, t) - Vector3.Lerp(pose.HipL, footL, 0.5f);
            Vector3 poleR = Vector3.Lerp(a.KneeR, b.KneeR, t) - Vector3.Lerp(pose.HipR, footR, 0.5f);
            pose.KneeL = TwoBone(pose.HipL, footL, body.Thigh, body.Shin, poleL, out pose.AnkleL);
            pose.KneeR = TwoBone(pose.HipR, footR, body.Thigh, body.Shin, poleR, out pose.AnkleR);
            pose.ToeL = pose.AnkleL + Vector3.Slerp(a.ToeL - a.AnkleL, b.ToeL - b.AnkleL, t);
            pose.ToeR = pose.AnkleR + Vector3.Slerp(a.ToeR - a.AnkleR, b.ToeR - b.AnkleR, t);
            KeepAboveMat(ref pose.ToeL, 0.03f);
            KeepAboveMat(ref pose.ToeR, 0.03f);
            return pose;
        }

        private static Pose SolveDiscreteBody(WrestlerFrame frame, BodyProportions body, PoseRole role, bool? forceLying = null)
        {
            Vector3 forward = WrestlerFrame.OnMat(frame.Facing).normalized;
            if (forward.sqrMagnitude < 1e-6f)
            {
                forward = Vector3.right;
            }

            Vector3 left = Vector3.Cross(forward, Vector3.up).normalized;
            bool lying = forceLying ?? (frame.Ground == GroundState.Prone || frame.Ground == GroundState.Supine || frame.Ground == GroundState.Bridge
                || (frame.Ground != GroundState.Standing && frame.TorsoRotation >= 45f));
            Pose pose;
            if (lying)
            {
                pose = Lying(frame, body, forward, left);
            }
            else
            {
                switch (frame.Ground)
                {
                    case GroundState.Standing:
                        pose = Standing(frame, body, forward, left);
                        break;
                    case GroundState.Seated:
                        pose = Seated(frame, body, forward, left);
                        break;
                    default:
                        pose = Kneeling(frame, body, forward, left, role);
                        break;
                }
            }

            ConstrainLegs(ref pose, body);
            return pose;
        }

        private static void ConstrainLegs(ref Pose pose, BodyProportions body)
        {
            Vector3 ankleL = pose.AnkleL;
            Vector3 ankleR = pose.AnkleR;
            Vector3 poleL = pose.KneeL - Vector3.Lerp(pose.HipL, ankleL, 0.5f);
            Vector3 poleR = pose.KneeR - Vector3.Lerp(pose.HipR, ankleR, 0.5f);
            pose.KneeL = TwoBone(pose.HipL, ankleL, body.Thigh, body.Shin, poleL, out pose.AnkleL);
            pose.KneeR = TwoBone(pose.HipR, ankleR, body.Thigh, body.Shin, poleR, out pose.AnkleR);
            pose.ToeL += pose.AnkleL - ankleL;
            pose.ToeR += pose.AnkleR - ankleR;
        }

        /// <summary>
        /// Places both hands: in their grip sockets on the opponent when gripping, otherwise in a ready
        /// or supporting position. The clavicles lift and reach with the arms, the elbows swing clear
        /// of both bodies, and each hand is turned to its socket or task.
        /// </summary>
        public static void SolveArms(ref Pose pose, BodyProportions body, PoseRole role, WrestlerFrame frame, in Pose opponent, IEnumerable<Grip> grips)
        {
            if (!frame.GroundFrom.HasValue || !frame.GroundTo.HasValue)
            {
                SolveDiscreteArms(ref pose, body, role, frame, in opponent, grips);
                return;
            }

            WrestlerFrame from = frame;
            WrestlerFrame to = frame;
            from.Ground = frame.GroundFrom.Value;
            to.Ground = frame.GroundTo.Value;
            Pose a = pose;
            Pose b = pose;
            SolveDiscreteArms(ref a, body, role, from, in opponent, grips, frame.GroundFromLying);
            SolveDiscreteArms(ref b, body, role, to, in opponent, grips, frame.GroundToLying);
            float t = Mathf.Clamp01(frame.GroundBlend);
            if (t <= 0f) { pose = a; return; }
            if (t >= 1f) { pose = b; return; }

            Quaternion chest = Quaternion.Slerp(Quaternion.LookRotation(a.ChestNormal, a.Spine), Quaternion.LookRotation(b.ChestNormal, b.Spine), t);
            pose.ChestNormal = chest * Vector3.forward;
            pose.Left = chest * Vector3.left;
            pose.HeadNormal = Vector3.Slerp(a.HeadNormal, b.HeadNormal, t);
            pose.ShoulderL = Vector3.Lerp(a.ShoulderL, b.ShoulderL, t);
            pose.ShoulderR = Vector3.Lerp(a.ShoulderR, b.ShoulderR, t);
            Vector3 handL = Vector3.Lerp(a.HandL, b.HandL, t);
            Vector3 handR = Vector3.Lerp(a.HandR, b.HandR, t);
            // Endpoint reaches can each lie inside the cone while their interpolation does not.
            handL = IntoCone(in pose, pose.ShoulderL, handL, pose.Left);
            handR = IntoCone(in pose, pose.ShoulderR, handR, -pose.Left);
            Vector3 poleL = Vector3.Lerp(a.ElbowL, b.ElbowL, t) - Vector3.Lerp(pose.ShoulderL, handL, 0.5f);
            Vector3 poleR = Vector3.Lerp(a.ElbowR, b.ElbowR, t) - Vector3.Lerp(pose.ShoulderR, handR, 0.5f);
            pose.ElbowL = TwoBone(pose.ShoulderL, handL, body.UpperArm, body.Forearm, poleL, out pose.HandL);
            pose.ElbowR = TwoBone(pose.ShoulderR, handR, body.UpperArm, body.Forearm, poleR, out pose.HandR);
            bool hasOpponent = opponent.Spine.sqrMagnitude > 1e-6f;
            float otherScale = hasOpponent ? OpponentScale(in pose, in opponent) : 1f;
            pose.ElbowL = Swivel(pose.ShoulderL, pose.ElbowL, pose.HandL, in pose, body, in opponent, otherScale, hasOpponent);
            pose.ElbowR = Swivel(pose.ShoulderR, pose.ElbowR, pose.HandR, in pose, body, in opponent, otherScale, hasOpponent);
            pose.ElbowL = ArmAboveMat(pose.ShoulderL, pose.HandL, body.UpperArm, body.Forearm, pose.ElbowL - Vector3.Lerp(pose.ShoulderL, pose.HandL, 0.5f), out pose.HandL);
            pose.ElbowR = ArmAboveMat(pose.ShoulderR, pose.HandR, body.UpperArm, body.Forearm, pose.ElbowR - Vector3.Lerp(pose.ShoulderR, pose.HandR, 0.5f), out pose.HandR);
            Quaternion leftHand = Quaternion.Slerp(Skeleton.Orientation(a.HandDirL, a.PalmNormalL), Skeleton.Orientation(b.HandDirL, b.PalmNormalL), t);
            Quaternion rightHand = Quaternion.Slerp(Skeleton.Orientation(a.HandDirR, a.PalmNormalR), Skeleton.Orientation(b.HandDirR, b.PalmNormalR), t);
            pose.HandDirL = leftHand * Vector3.up;
            pose.PalmNormalL = leftHand * Vector3.forward;
            pose.HandDirR = rightHand * Vector3.up;
            pose.PalmNormalR = rightHand * Vector3.forward;
            pose.LeftGripStyle = a.LeftGripStyle;
            pose.RightGripStyle = a.RightGripStyle;
            pose.LeftGripAperture = Mathf.Lerp(a.LeftGripAperture, b.LeftGripAperture, t);
            pose.RightGripAperture = Mathf.Lerp(a.RightGripAperture, b.RightGripAperture, t);
            pose.ReachTorsion = Mathf.Lerp(a.ReachTorsion, b.ReachTorsion, t);
            pose.RetractionL = Mathf.Lerp(a.RetractionL, b.RetractionL, t);
            pose.RetractionR = Mathf.Lerp(a.RetractionR, b.RetractionR, t);
        }

        private static void SolveDiscreteArms(ref Pose pose, BodyProportions body, PoseRole role, WrestlerFrame frame, in Pose opponent, IEnumerable<Grip> grips, bool? forceLying = null)
        {
            bool hasOpponent = opponent.Spine.sqrMagnitude > 1e-6f;
            GripSocket? left = null;
            GripSocket? right = null;
            if (hasOpponent)
            {
                foreach (Grip grip in grips)
                {
                    (GripSocket? l, GripSocket? r) = HandGrips.Sockets(grip, in pose, body, in opponent);
                    left = l ?? left;
                    right = r ?? right;
                }
            }

            bool lying = forceLying ?? (frame.Ground == GroundState.Prone || frame.Ground == GroundState.Supine || frame.Ground == GroundState.Bridge);
            Vector3 reachForward = Vector3.ProjectOnPlane(pose.ChestNormal, Vector3.up).normalized;
            if (reachForward.sqrMagnitude < 1e-6f)
            {
                reachForward = Vector3.ProjectOnPlane(pose.Spine, Vector3.up).normalized;
            }

            RestShoulders(ref pose, body);
            Pose current = pose;
            Pose other = opponent;

            (Vector3 Target, FreeHand Kind) DefaultHand(Vector3 shoulder, float side)
            {
                if (lying)
                {
                    if (frame.Ground == GroundState.Prone)
                    {
                        // Flat on the stomach: forearms on the mat in front of the shoulders.
                        Vector3 onMat = shoulder + (current.Spine * (body.Forearm * 0.9f)) + (current.Left * (side * 0.12f));
                        onMat.y = body.LimbRadius;
                        return (onMat, FreeHand.Forearms);
                    }

                    // On the back: arms out, framing to fight off the pin.
                    return (shoulder + (current.Left * (side * body.UpperArm)) + (Vector3.up * 0.25f) + (current.Spine * 0.1f), FreeHand.Framing);
                }

                if (role == PoseRole.Top && hasOpponent)
                {
                    return (Vector3.Lerp(other.Waist, other.Chest, 0.5f) + (other.Left * (side * body.ShoulderHalfWidth * 0.6f)), FreeHand.OnOpponent);
                }

                if (frame.Ground != GroundState.Standing && frame.Posture < 0.5f)
                {
                    // Hands posted on the mat under the shoulders.
                    Vector3 posted = shoulder + (reachForward * 0.15f);
                    posted.y = body.LimbRadius;
                    return (posted, FreeHand.Posted);
                }

                // Hand fighting: each hand works the opponent's facing wrist and forearm (my left
                // against their right), reaching as far as the arm allows.
                Vector3 ready = shoulder + (reachForward * ((body.UpperArm + body.Forearm) * 0.6f)) - (Vector3.up * 0.12f) - (current.Left * (side * 0.08f));
                if (!hasOpponent)
                {
                    return (ready, FreeHand.Ready);
                }

                Vector3 otherForward = Vector3.ProjectOnPlane(other.ChestNormal, Vector3.up).normalized;
                Vector3 otherShoulder = side > 0f ? other.ShoulderR : other.ShoulderL;
                Vector3 opponentForearm = otherShoulder + (otherForward * ((body.UpperArm + body.Forearm) * 0.55f)) - (Vector3.up * 0.15f);
                bool inReach = (opponentForearm - shoulder).magnitude < (body.UpperArm + body.Forearm) * 1.05f;
                return inReach ? (opponentForearm, FreeHand.HandFight) : (ready, FreeHand.Ready);
            }

            (Vector3 targetL, FreeHand kindL) = left.HasValue ? (left.Value.Wrist, FreeHand.Gripping) : DefaultHand(pose.ShoulderL, 1f);
            (Vector3 targetR, FreeHand kindR) = right.HasValue ? (right.Value.Wrist, FreeHand.Gripping) : DefaultHand(pose.ShoulderR, -1f);
            Vector3 elbowPoleL = ElbowPole(in pose, true, targetL);
            Vector3 elbowPoleR = ElbowPole(in pose, false, targetR);
            float compact = lying ? 0f : CompactBase(frame, role);
            if (compact > 0f)
            {
                // The compact base: free forearms flat on the mat, elbows tucked in under the shoulders.
                if (!left.HasValue)
                {
                    CompactForearm(in pose, body, true, reachForward, compact, ref targetL, ref elbowPoleL, ref kindL);
                }

                if (!right.HasValue)
                {
                    CompactForearm(in pose, body, false, reachForward, compact, ref targetR, ref elbowPoleR, ref kindR);
                }
            }

            // A reach behind the back turns the chest towards it before the arm takes the rest.
            TwistForReach(ref pose, targetL, targetR);
            float otherScale = hasOpponent ? OpponentScale(in pose, in opponent) : 1f;
            SolveArm(ref pose, body, true, targetL, elbowPoleL, in opponent, otherScale, hasOpponent);
            SolveArm(ref pose, body, false, targetR, elbowPoleR, in opponent, otherScale, hasOpponent);

            if (left.HasValue)
            {
                pose.HandDirL = left.Value.Direction;
                pose.PalmNormalL = left.Value.Palm;
                pose.LeftGripStyle = left.Value.Style;
                pose.LeftGripAperture = left.Value.Aperture;
            }
            else
            {
                (pose.HandDirL, pose.PalmNormalL, pose.LeftGripAperture) = FreeHandFrame(kindL, pose.HandL - pose.ElbowL, pose.HandL, reachForward, in opponent);
                pose.LeftGripStyle = HandGripStyle.OpenGuard;
            }

            if (right.HasValue)
            {
                pose.HandDirR = right.Value.Direction;
                pose.PalmNormalR = right.Value.Palm;
                pose.RightGripStyle = right.Value.Style;
                pose.RightGripAperture = right.Value.Aperture;
            }
            else
            {
                (pose.HandDirR, pose.PalmNormalR, pose.RightGripAperture) = FreeHandFrame(kindR, pose.HandR - pose.ElbowR, pose.HandR, reachForward, in opponent);
                pose.RightGripStyle = HandGripStyle.OpenGuard;
            }

            pose.HeadNormal = hasOpponent ? LookAt(in pose, in opponent) : Vector3.zero;
        }

        /// <summary>What a free hand is doing, which sets how it is turned and opened.</summary>
        private enum FreeHand
        {
            Gripping,
            Posted,
            Forearms,
            Framing,
            OnOpponent,
            HandFight,
            Ready,
        }

        /// <summary>Share of the arm's reach above or across the chest taken by the clavicle, and its limits (degrees).</summary>
        private const float ClavicleShare = 0.28f;
        private const float MaxElevation = 30f;
        private const float MaxProtraction = 30f;
        private const float MaxRetraction = 10f;

        /// <summary>
        /// The glenohumeral reach cone in the chest's frame (degrees): how far the upper arm can go
        /// behind the body's coronal plane, across the midline and up from hanging. Past them a
        /// person turns the thoracic spine and draws the shoulder blade back rather than forcing the
        /// humerus further.
        /// </summary>
        public const float MaxPosteriorExtension = 42f;
        public const float MaxCrossBody = 50f;
        public const float MaxArmElevation = 145f;

        /// <summary>Most the chest turns (degrees) to bring a reach behind the back into the cone, and how far (m) the shoulder retracts and drops for the rest.</summary>
        public const float MaxCounterTorsion = 18f;
        public const float MaxReachRetraction = 0.035f;

        /// <summary>Posterior excess (degrees) over which the shoulder blade retracts fully.</summary>
        private const float RetractionRange = 30f;

        /// <summary>Where along the shoulder line (share from the midline) the clavicle starts.</summary>
        private const float ClavicleRoot = 0.18f;

        /// <summary>Most the head turns from the chest (degrees) to watch the opponent.</summary>
        private const float MaxHeadTurn = 50f;

        /// <summary>Shoulders in their rest place on the chest (so solving twice does not lift them twice).</summary>
        private static void RestShoulders(ref Pose pose, BodyProportions body)
        {
            Vector3 shoulderBase = pose.Neck - (pose.Spine * body.ShoulderDrop);
            pose.ShoulderL = shoulderBase + (pose.Left * body.ShoulderHalfWidth);
            pose.ShoulderR = shoulderBase - (pose.Left * body.ShoulderHalfWidth);
            KeepAboveMat(ref pose.ShoulderL, body.LimbRadius);
            KeepAboveMat(ref pose.ShoulderR, body.LimbRadius);
            pose.RetractionL = 0f;
            pose.RetractionR = 0f;
        }

        /// <summary>
        /// One arm towards its target: solved from the rest shoulder, the clavicle then lifts and
        /// reaches with the upper arm (and retracts for a reach behind the back), the target is
        /// brought into the shoulder's reach cone, the arm is solved again from the moved shoulder,
        /// and the elbow swings about the shoulder-hand line to clear both bodies.
        /// </summary>
        private static void SolveArm(ref Pose pose, BodyProportions body, bool left, Vector3 target, Vector3 pole, in Pose opponent, float otherScale, bool hasOpponent)
        {
            Vector3 rest = left ? pose.ShoulderL : pose.ShoulderR;
            Vector3 lateral = left ? pose.Left : -pose.Left;
            Vector3 shoulderBase = pose.Neck - (pose.Spine * body.ShoulderDrop);
            Vector3 root = shoulderBase + (lateral * (body.ShoulderHalfWidth * ClavicleRoot));
            Vector3 elbow = TwoBone(rest, target, body.UpperArm, body.Forearm, pole, out _);
            Vector3 shoulder = Clavicle(rest, root, elbow, pose.Spine, pose.ChestNormal, lateral);
            shoulder = Retract(ref pose, left, shoulder, target, lateral);
            target = IntoCone(in pose, shoulder, target, lateral);
            elbow = TwoBone(shoulder, target, body.UpperArm, body.Forearm, pole, out Vector3 hand);
            elbow = Swivel(shoulder, elbow, hand, in pose, body, in opponent, otherScale, hasOpponent);
            elbow = ArmAboveMat(shoulder, hand, body.UpperArm, body.Forearm, elbow - Vector3.Lerp(shoulder, hand, 0.5f), out hand);
            if (left)
            {
                pose.ShoulderL = shoulder;
                pose.ElbowL = elbow;
                pose.HandL = hand;
            }
            else
            {
                pose.ShoulderR = shoulder;
                pose.ElbowR = elbow;
                pose.HandR = hand;
            }
        }

        /// <summary>
        /// One arm to a target from where its shoulder is now (a hold re-aimed after the arms are
        /// placed): the shoulder retracts for a reach behind, the target comes into the reach cone,
        /// and the elbow bends towards <paramref name="pole"/>.
        /// </summary>
        public static void ReachArm(ref Pose pose, BodyProportions body, bool left, Vector3 target, Vector3 pole)
        {
            Vector3 lateral = left ? pose.Left : -pose.Left;
            Vector3 shoulder = Retract(ref pose, left, left ? pose.ShoulderL : pose.ShoulderR, target, lateral);
            target = IntoCone(in pose, shoulder, target, lateral);
            Vector3 elbow = TwoBone(shoulder, target, body.UpperArm, body.Forearm, pole, out Vector3 hand);
            if (left)
            {
                pose.ShoulderL = shoulder;
                pose.ElbowL = elbow;
                pose.HandL = hand;
            }
            else
            {
                pose.ShoulderR = shoulder;
                pose.ElbowR = elbow;
                pose.HandR = hand;
            }
        }

        /// <summary>
        /// The elbow's bend direction for a reach, continuous over every direction the arm can
        /// point: from the target's elevation and how far behind the chest it lies, in the chest's
        /// frame. The point of the elbow faces down and back (towards the hips) for low and forward
        /// reaches, swings out and back as the arm rises overhead, and drops straight down for a
        /// reach behind, so it never flips across the shoulder.
        /// </summary>
        public static Vector3 ElbowPole(in Pose pose, bool left, Vector3 target)
        {
            Vector3 up = pose.Spine.sqrMagnitude > 1e-6f ? pose.Spine.normalized : Vector3.up;
            Vector3 front = pose.ChestNormal.sqrMagnitude > 1e-6f ? pose.ChestNormal.normalized : Vector3.forward;
            Vector3 lateral = (left ? pose.Left : -pose.Left).normalized;
            Vector3 shoulder = left ? pose.ShoulderL : pose.ShoulderR;
            Vector3 reach = target - shoulder;
            Vector3 direction = reach.sqrMagnitude > 1e-8f ? reach.normalized : -up;
            float elevation = Mathf.Acos(Mathf.Clamp(-Vector3.Dot(direction, up), -1f, 1f)) * Mathf.Rad2Deg;
            float overhead = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(60f, 150f, elevation));
            float behind = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.7f, -Vector3.Dot(direction, front)));
            Vector3 pole = (-up * (0.8f * (1f - overhead))) + (up * (0.35f * overhead)) + (lateral * (0.6f + (0.3f * overhead))) - (front * (0.2f + (0.3f * overhead)));
            pole = Vector3.Lerp(pole, (-up * 0.9f) + (lateral * 0.3f), behind);
            return pole.sqrMagnitude > 1e-8f ? pole.normalized : -up;
        }

        /// <summary>
        /// Brings a target inside the shoulder's reach cone (<see cref="MaxPosteriorExtension"/>,
        /// <see cref="MaxCrossBody"/>, <see cref="MaxArmElevation"/>), keeping its distance. Returns
        /// the target unchanged when it is already inside.
        /// </summary>
        public static Vector3 IntoCone(in Pose pose, Vector3 shoulder, Vector3 target, Vector3 lateral)
        {
            Vector3 reach = target - shoulder;
            float distance = reach.magnitude;
            if (distance < 1e-5f || pose.Spine.sqrMagnitude < 1e-6f || pose.ChestNormal.sqrMagnitude < 1e-6f)
            {
                return target;
            }

            Vector3 clamped = ConeDirection(reach / distance, pose.Spine.normalized, pose.ChestNormal.normalized, lateral.normalized, out _);
            return shoulder + (clamped * distance);
        }

        /// <summary>
        /// A unit arm direction brought inside the reach cone, and how far (degrees) it was behind
        /// the coronal plane past <see cref="MaxPosteriorExtension"/>.
        /// </summary>
        public static Vector3 ConeDirection(Vector3 direction, Vector3 up, Vector3 front, Vector3 lateral, out float posteriorExcess)
        {
            Vector3 d = direction;
            float behind = Mathf.Asin(Mathf.Clamp(-Vector3.Dot(d, front), -1f, 1f)) * Mathf.Rad2Deg;
            posteriorExcess = Mathf.Max(0f, behind - MaxPosteriorExtension);
            if (posteriorExcess > 0f)
            {
                d = TowardsPlane(d, front, posteriorExcess, lateral);
            }

            float across = Mathf.Asin(Mathf.Clamp(-Vector3.Dot(d, lateral), -1f, 1f)) * Mathf.Rad2Deg;
            if (across > MaxCrossBody)
            {
                d = TowardsPlane(d, lateral, across - MaxCrossBody, front);
            }

            float elevation = Mathf.Acos(Mathf.Clamp(-Vector3.Dot(d, up), -1f, 1f)) * Mathf.Rad2Deg;
            if (elevation > MaxArmElevation)
            {
                d = Vector3.RotateTowards(d, -up, (elevation - MaxArmElevation) * Mathf.Deg2Rad, 0f);
            }

            return d.normalized;
        }

        /// <summary>Turns <paramref name="d"/> by <paramref name="degrees"/> towards the plane square to <paramref name="normal"/> (to <paramref name="fallback"/> if it points along the normal).</summary>
        private static Vector3 TowardsPlane(Vector3 d, Vector3 normal, float degrees, Vector3 fallback)
        {
            Vector3 onPlane = Vector3.ProjectOnPlane(d, normal);
            if (onPlane.sqrMagnitude < 1e-8f)
            {
                onPlane = fallback;
            }

            return Vector3.RotateTowards(d, onPlane.normalized, degrees * Mathf.Deg2Rad, 0f);
        }

        /// <summary>
        /// Thoracic counter-torsion: when a hand's target lies further behind the chest than the
        /// shoulder can reach, the chest turns towards that side (by up to
        /// <see cref="MaxCounterTorsion"/>) about the spine, carrying the shoulders (and any arm not
        /// being re-aimed) with it. Opposite pulls cancel.
        /// </summary>
        public static void TwistForReach(ref Pose pose, Vector3? targetL, Vector3? targetR)
        {
            if (pose.Spine.sqrMagnitude < 1e-6f || pose.ChestNormal.sqrMagnitude < 1e-6f)
            {
                return;
            }

            Vector3 up = pose.Spine.normalized;
            Vector3 front = pose.ChestNormal.normalized;

            // Turning the chest towards the reaching arm's side swings that shoulder back.
            float turn = PosteriorExcess(targetL, pose.ShoulderL, up, front, pose.Left) - PosteriorExcess(targetR, pose.ShoulderR, up, front, -pose.Left);
            // However often the arms are solved, the chest turns no further than the limit in all.
            turn = Mathf.Clamp(pose.ReachTorsion + turn, -MaxCounterTorsion, MaxCounterTorsion) - pose.ReachTorsion;
            if (Mathf.Abs(turn) < 0.01f)
            {
                return;
            }

            // Positive turns the chest towards its left: about the spine, the front swings to the left side.
            pose.ReachTorsion += turn;
            Vector3 axis = pose.Spine.normalized;
            Quaternion rotation = Quaternion.AngleAxis(Vector3.Dot(Vector3.Cross(pose.ChestNormal, pose.Left), axis) >= 0f ? turn : -turn, axis);
            Vector3 pivot = pose.Neck;
            Vector3 Turn(Vector3 p) => pivot + (rotation * (p - pivot));
            pose.ShoulderL = Turn(pose.ShoulderL);
            pose.ShoulderR = Turn(pose.ShoulderR);
            if (!targetL.HasValue)
            {
                pose.ElbowL = Turn(pose.ElbowL);
                pose.HandL = Turn(pose.HandL);
            }

            if (!targetR.HasValue)
            {
                pose.ElbowR = Turn(pose.ElbowR);
                pose.HandR = Turn(pose.HandR);
            }

            pose.ChestNormal = (rotation * pose.ChestNormal).normalized;
            pose.Left = (rotation * pose.Left).normalized;
            pose.HandDirL = rotation * pose.HandDirL;
            pose.HandDirR = rotation * pose.HandDirR;
            pose.PalmNormalL = rotation * pose.PalmNormalL;
            pose.PalmNormalR = rotation * pose.PalmNormalR;
        }

        /// <summary>How far (degrees) a reach from <paramref name="shoulder"/> lies behind the coronal plane past the cone.</summary>
        private static float PosteriorExcess(Vector3? target, Vector3 shoulder, Vector3 up, Vector3 front, Vector3 lateral)
        {
            if (!target.HasValue || (target.Value - shoulder).sqrMagnitude < 1e-8f)
            {
                return 0f;
            }

            ConeDirection((target.Value - shoulder).normalized, up, front, lateral.normalized, out float excess);
            return excess;
        }

        /// <summary>
        /// The shoulder blade drawn back and down for a reach the counter-torsion could not bring
        /// inside the cone: up to <see cref="MaxReachRetraction"/> along the back in all, however
        /// often the arm is re-aimed (the pose keeps how far it is drawn back).
        /// </summary>
        private static Vector3 Retract(ref Pose pose, bool left, Vector3 shoulder, Vector3 target, Vector3 lateral)
        {
            Vector3 reach = target - shoulder;
            if (reach.sqrMagnitude < 1e-8f || pose.Spine.sqrMagnitude < 1e-6f || pose.ChestNormal.sqrMagnitude < 1e-6f)
            {
                return shoulder;
            }

            Vector3 front = pose.ChestNormal.normalized;
            float applied = left ? pose.RetractionL : pose.RetractionR;
            // Measured from where the shoulder sits before any retraction.
            Vector3 back = ((front * 0.9f) + (pose.Spine.normalized * 0.4f)).normalized;
            Vector3 unretracted = shoulder + (back * applied);
            ConeDirection((target - unretracted).normalized, pose.Spine.normalized, front, lateral.normalized, out float excess);
            float wanted = MaxReachRetraction * Mathf.SmoothStep(0f, 1f, excess / RetractionRange);
            if (left)
            {
                pose.RetractionL = wanted;
            }
            else
            {
                pose.RetractionR = wanted;
            }

            return unretracted - (back * wanted);
        }

        /// <summary>
        /// A free forearm in the compact base: the elbow on the mat just in front of and inside the
        /// shoulder (the upper arm near vertical), the forearm flat along the mat angled in towards
        /// the head, blended in by <paramref name="weight"/>.
        /// </summary>
        private static void CompactForearm(in Pose pose, BodyProportions body, bool left, Vector3 reachForward, float weight, ref Vector3 target, ref Vector3 pole, ref FreeHand kind)
        {
            Vector3 shoulder = left ? pose.ShoulderL : pose.ShoulderR;
            Vector3 inward = Vector3.ProjectOnPlane(left ? -pose.Left : pose.Left, Vector3.up);
            inward = inward.sqrMagnitude > 1e-6f ? inward.normalized : Vector3.zero;
            float drop = shoulder.y - ClipKinematics.ForearmElbowHeight;
            float run = Mathf.Sqrt(Mathf.Max(0f, (body.UpperArm * body.UpperArm) - (drop * drop)));
            Vector3 elbowWay = ((reachForward * 0.6f) + (inward * 0.6f)).normalized;
            Vector3 elbow = Flat(shoulder, ClipKinematics.ForearmElbowHeight) + (elbowWay * Mathf.Max(run, 0.04f));
            Vector3 along = ((reachForward * 0.9f) + (inward * 0.35f)).normalized;
            float rise = ClipKinematics.ForearmWristHeight - ClipKinematics.ForearmElbowHeight;
            Vector3 hand = elbow + (along * Mathf.Sqrt(Mathf.Max(0f, (body.Forearm * body.Forearm) - (rise * rise)))) + (Vector3.up * rise);
            target = Vector3.Lerp(target, hand, weight);
            pole = Vector3.Lerp(pole, elbow - Vector3.Lerp(shoulder, hand, 0.5f), weight);
            if (weight >= 0.5f)
            {
                kind = FreeHand.Forearms;
            }
        }

        /// <summary>
        /// The shoulder joint moved by the clavicle: elevated as the upper arm rises above
        /// horizontal, protracted as it reaches forward or across the chest, retracted a little when
        /// it goes back (scapulohumeral rhythm), each about <see cref="ClavicleShare"/> of the reach.
        /// </summary>
        private static Vector3 Clavicle(Vector3 shoulder, Vector3 root, Vector3 elbow, Vector3 up, Vector3 front, Vector3 lateral)
        {
            Vector3 humerus = elbow - shoulder;
            Vector3 clavicle = shoulder - root;
            if (humerus.sqrMagnitude < 1e-8f || clavicle.sqrMagnitude < 1e-8f || up.sqrMagnitude < 1e-6f || front.sqrMagnitude < 1e-6f)
            {
                return shoulder;
            }

            humerus.Normalize();
            float raised = Mathf.Asin(Mathf.Clamp(Vector3.Dot(humerus, up), -1f, 1f)) * Mathf.Rad2Deg;
            float elevation = Mathf.Min(ClavicleShare * Mathf.Max(0f, raised + 30f), MaxElevation);
            float forward = Vector3.Dot(humerus, front);
            float outward = Vector3.Dot(humerus, lateral);
            float horizontal = Mathf.Sqrt((forward * forward) + (outward * outward));
            float swing = Mathf.Atan2(forward, outward) * Mathf.Rad2Deg;
            float protraction = Mathf.Clamp(ClavicleShare * swing, -MaxRetraction, MaxProtraction) * Mathf.Clamp01(horizontal / 0.5f);

            float length = clavicle.magnitude;
            Vector3 direction = clavicle / length;
            direction = Vector3.RotateTowards(direction, up, elevation * Mathf.Deg2Rad, 0f);
            direction = Vector3.RotateTowards(direction, protraction >= 0f ? front : -front, Mathf.Abs(protraction) * Mathf.Deg2Rad, 0f);
            return root + (direction * length);
        }

        /// <summary>
        /// Swings the elbow about the shoulder-hand line (the hand stays put) to the nearest angle
        /// where neither arm segment sinks into the wrestler's own ribcage or the opponent's trunk.
        /// </summary>
        private static Vector3 Swivel(Vector3 shoulder, Vector3 elbow, Vector3 hand, in Pose self, BodyProportions body, in Pose other, float otherScale, bool hasOpponent)
        {
            Vector3 ownBottom = self.Pelvis;
            Vector3 ownTop = self.SpinePoint(0.75f);
            float ownRadius = body.TorsoHalfDepth + (body.LimbRadius * 0.8f);
            Vector3 otherBottom = other.Pelvis;
            Vector3 otherTop = other.SpinePoint(0.8f);
            float otherRadius = ((body.TorsoHalfDepth * otherScale) + (body.LimbRadius * 0.8f)) * 0.92f;

            float Penetration(Vector3 e)
            {
                // The hand end of the forearm is where the grip is: only the rest of the arm counts.
                Vector3 wristSide = Vector3.Lerp(e, hand, 0.7f);
                float depth = Mathf.Max(0f, ownRadius - BodyContact.SegmentDistance(e, wristSide, ownBottom, ownTop));
                if (hasOpponent)
                {
                    depth += Mathf.Max(0f, otherRadius - BodyContact.SegmentDistance(shoulder, e, otherBottom, otherTop));
                    depth += Mathf.Max(0f, otherRadius - BodyContact.SegmentDistance(e, wristSide, otherBottom, otherTop));
                }

                return depth;
            }

            float baseline = Penetration(elbow);
            Vector3 axis = hand - shoulder;
            if (baseline < 0.004f || axis.sqrMagnitude < 1e-6f)
            {
                return elbow;
            }

            axis.Normalize();
            Vector3 arm = elbow - shoulder;
            Vector3 best = elbow;
            float bestScore = baseline;
            for (int step = 1; step <= 10; step++)
            {
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    float angle = sign * step * 15f;
                    Vector3 candidate = shoulder + (Quaternion.AngleAxis(angle, axis) * arm);
                    if (candidate.y < body.LimbRadius)
                    {
                        continue;
                    }

                    // Prefer the smallest swing, and elbows that stay below the shoulder.
                    float score = Penetration(candidate) + (Mathf.Abs(angle) * 0.0002f) + (Mathf.Max(0f, candidate.y - shoulder.y) * 0.05f);
                    if (score < bestScore)
                    {
                        best = candidate;
                        bestScore = score;
                    }
                }
            }

            return best;
        }

        /// <summary>The opponent's size relative to this wrestler's, from shoulder width.</summary>
        private static float OpponentScale(in Pose self, in Pose other)
        {
            float mine = (self.ShoulderL - self.ShoulderR).magnitude;
            float theirs = (other.ShoulderL - other.ShoulderR).magnitude;
            return mine > 1e-3f && theirs > 1e-3f ? Mathf.Clamp(theirs / mine, 0.7f, 1.4f) : 1f;
        }

        /// <summary>How a free hand is turned and opened for what it is doing.</summary>
        private static (Vector3 Direction, Vector3 Palm, float Aperture) FreeHandFrame(FreeHand kind, Vector3 forearm, Vector3 hand, Vector3 reachForward, in Pose other)
        {
            Vector3 along = forearm.sqrMagnitude > 1e-8f ? forearm.normalized : reachForward;
            switch (kind)
            {
                case FreeHand.Posted:
                    // Flat on the mat, fingers forward along the forearm's line.
                    return (Skeleton.PostedHandDirection(along, reachForward), Vector3.down, 1f);
                case FreeHand.Forearms:
                    return (Skeleton.PostedHandDirection(along, reachForward), Vector3.down, 0.85f);
                case FreeHand.Framing:
                {
                    Vector3 push = other.Spine.sqrMagnitude > 1e-6f ? other.Chest - hand : Vector3.up;
                    return (along, push.normalized, 0.6f);
                }

                case FreeHand.OnOpponent:
                {
                    // Pressed flat on the opponent's back or side.
                    Vector3 palm = (other.SpinePoint(0.5f) - hand).normalized;
                    return (Vector3.ProjectOnPlane(along, palm).normalized, palm, 0.85f);
                }

                case FreeHand.HandFight:
                {
                    // On top of the opponent's forearm, palm down and towards them.
                    Vector3 towards = Vector3.ProjectOnPlane(-other.ChestNormal, Vector3.up).normalized;
                    return (along, ((-Vector3.up * 0.8f) + (towards * 0.45f)).normalized, 0.45f);
                }

                default:
                    // Ready: wrist cocked up a little, palm forward and down.
                    return ((along + (Vector3.up * 0.25f)).normalized, ((reachForward * 0.7f) - (Vector3.up * 0.3f)).normalized, 0.4f);
            }
        }

        /// <summary>The face turned towards the opponent's head and chest, as far as the neck allows.</summary>
        private static Vector3 LookAt(in Pose self, in Pose other)
        {
            Vector3 headAxis = self.Head - self.Neck;
            if (headAxis.sqrMagnitude < 1e-6f || self.ChestNormal.sqrMagnitude < 1e-6f)
            {
                return Vector3.zero;
            }

            Vector3 chest = Vector3.ProjectOnPlane(self.ChestNormal, headAxis);
            Vector3 look = Vector3.ProjectOnPlane(Vector3.Lerp(other.Head, other.Chest, 0.5f) - self.Head, headAxis);
            if (chest.sqrMagnitude < 1e-6f || look.sqrMagnitude < 1e-6f)
            {
                return Vector3.zero;
            }

            // An opponent behind gets no look: the turn fades out before the side to turn to becomes
            // ambiguous, so the head never swaps from one shoulder to the other.
            chest.Normalize();
            look.Normalize();
            float behind = Mathf.SmoothStep(0f, 1f, (Vector3.Dot(chest, look) + 0.6f) / 0.6f);
            return Vector3.RotateTowards(chest, look, MaxHeadTurn * behind * Mathf.Deg2Rad, 0f);
        }

        private static Pose Standing(WrestlerFrame frame, BodyProportions body, Vector3 forward, Vector3 left)
        {
            var pose = new Pose { Left = left };
            float legLength = body.Thigh + body.Shin;
            // A wrestling stance sinks the hips on bent knees (over the toes) rather than folding at
            // the hips with straight legs, which pushes the buttocks out behind the feet.
            float hipHeight = ClipKinematics.StandingHipHeight(frame.Level, legLength);
            // A wrestling stance leans in even at full posture: chest over the knees, ready to react.
            float lean = ClipKinematics.StandingLean(frame.Posture, frame.Level, frame.Balance);
            pose.Pelvis = WrestlerFrame.OnMat(frame.Position, hipHeight) - (forward * (Mathf.Sin(lean * Mathf.Deg2Rad) * 0.04f));
            pose.Spine = Tilt(forward, lean);
            pose.ChestNormal = Vector3.Cross(pose.Spine, left).normalized;
            FinishUpperBody(ref pose, body);

            // The chest faces the opponent; the hips follow the feet (bladed in a staggered stance,
            // leading the turn when a move steps round), the spine twisting between them.
            Quaternion hipTurn = Quaternion.AngleAxis(StanceTorsion(frame, left), Vector3.up);
            Vector3 hipForward = hipTurn * forward;
            Vector3 hipLeft = hipTurn * left;
            pose.PelvisNormal = hipForward;
            pose.HipL = pose.Pelvis + (hipLeft * body.HipHalfWidth);
            pose.HipR = pose.Pelvis - (hipLeft * body.HipHalfWidth);
            pose.AnkleL = WrestlerFrame.OnMat(frame.LeftFoot, AnkleHeight);
            pose.AnkleR = WrestlerFrame.OnMat(frame.RightFoot, AnkleHeight);
            pose.KneeL = TwoBone(pose.HipL, pose.AnkleL, body.Thigh, body.Shin, hipForward + (hipLeft * 0.2f), out pose.AnkleL);
            pose.KneeR = TwoBone(pose.HipR, pose.AnkleR, body.Thigh, body.Shin, hipForward - (hipLeft * 0.2f), out pose.AnkleR);
            pose.ToeL = Toe(pose.AnkleL, (hipForward + (hipLeft * 0.12f)).normalized, body);
            pose.ToeR = Toe(pose.AnkleR, (hipForward - (hipLeft * 0.12f)).normalized, body);
            return pose;
        }

        /// <summary>Share of the feet's turn from square that the hips take, and the most the spine twists (degrees).</summary>
        private const float HipBlade = 0.6f;
        private const float MaxTorsion = 38f;

        /// <summary>How far (degrees, about up) the hips turn from the chest: part of the way to square with the feet.</summary>
        private static float StanceTorsion(WrestlerFrame frame, Vector3 left)
        {
            Vector3 feet = WrestlerFrame.OnMat(frame.LeftFoot - frame.RightFoot);
            if (feet.sqrMagnitude < 1e-4f)
            {
                return 0f;
            }

            float turn = Vector3.SignedAngle(left, feet.normalized, Vector3.up);
            if (Mathf.Abs(turn) > 90f)
            {
                // Feet crossed in a step: the hips are square to the line, not turned past it.
                turn -= Mathf.Sign(turn) * 180f;
            }

            return Mathf.Clamp(turn * HipBlade, -MaxTorsion, MaxTorsion);
        }

        /// <summary>Turns the chest about the spine by <paramref name="degrees"/> towards <paramref name="side"/> (+1 the left), leaving the pelvis.</summary>
        private static void TwistChest(ref Pose pose, float degrees, float side)
        {
            float radians = degrees * Mathf.Deg2Rad;
            Vector3 chest = (pose.ChestNormal * Mathf.Cos(radians)) + (pose.Left * (Mathf.Sin(radians) * (side < 0f ? -1f : 1f)));
            pose.ChestNormal = Vector3.ProjectOnPlane(chest, pose.Spine).normalized;
            pose.Left = Vector3.Cross(pose.ChestNormal, pose.Spine).normalized;
        }

        /// <summary>A kneeling or seated wrestler being turned rolls the chest first; the hips stay on the mat.</summary>
        private const float GroundTwistShare = 0.85f;

        /// <summary>
        /// How far a wrestler is in the compact defensive par terre base (see
        /// <see cref="ClipKinematics.CompactBase"/>): only the bottom wrestler on both knees with a
        /// low posture; attacking or rising from the knees, or on top, is active kneeling.
        /// </summary>
        public static float CompactBase(WrestlerFrame frame, PoseRole role) =>
            role == PoseRole.Bottom && frame.Ground == GroundState.BothKnees && frame.TorsoRotation < 45f ? ClipKinematics.CompactBase(frame.Posture) : 0f;

        /// <summary>Knees this far (m) out from the hip joints: a little for kneeling up, wide for the compact base.</summary>
        private const float KneeSpread = 0.04f;
        private const float CompactKneeSpread = 0.09f;

        /// <summary>
        /// On the knees. Kneeling up (one knee, or attacking from both) the hips stand over the knees
        /// on the thighs. In the compact defensive base the pelvis sinks back onto the heels, the knees
        /// spread wide and the trunk lies low and long so the forearms rest flat on the mat with the
        /// elbows tucked in (the arms are placed by <see cref="SolveArms"/>).
        /// </summary>
        private static Pose Kneeling(WrestlerFrame frame, BodyProportions body, Vector3 forward, Vector3 left, PoseRole role)
        {
            var pose = new Pose { Left = left };
            Vector3 centre = WrestlerFrame.OnMat(frame.Position);
            float compact = CompactBase(frame, role);
            float compactHeight = ClipKinematics.CompactHipHeight(frame.Level);
            float pelvisHeight = Mathf.Lerp(ClipKinematics.KneelingHipHeight(frame.Level, body.Thigh), compactHeight, compact);
            // The thigh's run from the hip forward to the knee on the mat.
            float back = Mathf.Sqrt(Mathf.Max(0f, (body.Thigh * body.Thigh) - Mathf.Pow(pelvisHeight - KneeOnMatHeight, 2f)));
            float posture = role == PoseRole.Top ? Mathf.Min(frame.Posture, 0.35f) : frame.Posture;
            float lean = Mathf.Lerp(ClipKinematics.KneelingLean(posture), ClipKinematics.CompactLean(compactHeight, body.Torso, body.ShoulderDrop, body.UpperArm), compact);
            pose.Pelvis = centre + (Vector3.up * pelvisHeight) - (forward * (back * 0.5f));
            pose.Spine = Tilt(forward, lean);
            pose.ChestNormal = Vector3.Cross(pose.Spine, left).normalized;
            pose.PelvisNormal = pose.ChestNormal;
            TwistChest(ref pose, frame.TorsoRotation * GroundTwistShare, frame.RollSide);
            FinishUpperBody(ref pose, body);

            pose.HipL = pose.Pelvis + (left * body.HipHalfWidth);
            pose.HipR = pose.Pelvis - (left * body.HipHalfWidth);
            Vector3 kneeForward = forward * back;
            float spread = Mathf.Lerp(KneeSpread, CompactKneeSpread, compact);
            bool oneKnee = frame.Ground == GroundState.OneKnee;
            bool leftLeads = frame.Stance != Stance.StaggeredRightLead;

            pose.KneeL = Kneel(pose.HipL, kneeForward, left * spread);
            pose.KneeR = Kneel(pose.HipR, kneeForward, -left * spread);
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
            pose.PelvisNormal = pose.ChestNormal;
            // A hip heist or sit-out turns the chest over the planted hips.
            TwistChest(ref pose, frame.TorsoRotation * GroundTwistShare, frame.RollSide);
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
            float rollSign = frame.RollSide < 0f ? -1f : 1f;
            Vector3 chest = (-Vector3.up * Mathf.Cos(radians)) + (left * (Mathf.Sin(radians) * rollSign));
            pose.ChestNormal = chest.normalized;
            pose.Spine = forward;
            pose.Left = Vector3.Cross(pose.ChestNormal, pose.Spine).normalized;
            // Turning over, the shoulders lead and the hips follow behind them.
            float hipRadians = (rotation - (HipRollLag * Mathf.Sin(radians))) * Mathf.Deg2Rad;
            pose.PelvisNormal = ((-Vector3.up * Mathf.Cos(hipRadians)) + (left * (Mathf.Sin(hipRadians) * rollSign))).normalized;
            Vector3 hipLeft = Vector3.Cross(pose.PelvisNormal, pose.Spine).normalized;
            bool bridge = frame.Ground == GroundState.Bridge;
            float restHeight = ClipKinematics.LyingRestHeight(rotation, body.TorsoHalfDepth, body.ShoulderHalfWidth, false);
            Vector3 centre = WrestlerFrame.OnMat(frame.Position);
            pose.Pelvis = centre - (forward * (body.Torso * 0.5f)) + (Vector3.up * ClipKinematics.LyingRestHeight(rotation, body.TorsoHalfDepth, body.ShoulderHalfWidth, bridge));
            pose.Neck = centre + (forward * (body.Torso * 0.5f)) + (Vector3.up * (bridge ? restHeight + 0.12f : restHeight));
            pose.Spine = (pose.Neck - pose.Pelvis).normalized;
            pose.Head = pose.Neck + (pose.Spine * (body.Neck + body.HeadRadius)) - (Vector3.up * (bridge ? 0.18f : 0f));
            KeepAboveMat(ref pose.Head, body.HeadRadius);
            Vector3 shoulderBase = pose.Neck - (pose.Spine * body.ShoulderDrop);
            pose.ShoulderL = shoulderBase + (pose.Left * body.ShoulderHalfWidth);
            pose.ShoulderR = shoulderBase - (pose.Left * body.ShoulderHalfWidth);
            KeepAboveMat(ref pose.ShoulderL, body.LimbRadius);
            KeepAboveMat(ref pose.ShoulderR, body.LimbRadius);

            pose.HipL = pose.Pelvis + (hipLeft * body.HipHalfWidth);
            pose.HipR = pose.Pelvis - (hipLeft * body.HipHalfWidth);
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
                pose.KneeL = Flat(pose.HipL + (legDirection * body.Thigh) + (hipLeft * spread), body.LimbRadius);
                pose.KneeR = Flat(pose.HipR + (legDirection * body.Thigh) - (hipLeft * spread), body.LimbRadius);
                pose.AnkleL = Flat(pose.KneeL + (legDirection * body.Shin) + (hipLeft * spread), AnkleHeight);
                pose.AnkleR = Flat(pose.KneeR + (legDirection * body.Shin) - (hipLeft * spread), AnkleHeight);
                pose.ToeL = pose.AnkleL + (legDirection * 0.05f) + (pose.PelvisNormal * (body.Foot * 0.6f));
                pose.ToeR = pose.AnkleR + (legDirection * 0.05f) + (pose.PelvisNormal * (body.Foot * 0.6f));
                KeepAboveMat(ref pose.ToeL, 0.03f);
                KeepAboveMat(ref pose.ToeR, 0.03f);
            }

            return pose;
        }

        /// <summary>Degrees the hips trail the shoulders by when a lying wrestler is half turned.</summary>
        private const float HipRollLag = 18f;

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
            RotateFrames(ref pose, rotation);
        }

        /// <summary>Turns the pose's segment directions (pelvis, head, hands) by <paramref name="rotation"/>.</summary>
        public static void RotateFrames(ref Pose pose, Quaternion rotation)
        {
            pose.PelvisUp = rotation * pose.PelvisUp;
            pose.PelvisNormal = rotation * pose.PelvisNormal;
            pose.HeadNormal = rotation * pose.HeadNormal;
            pose.HandDirL = rotation * pose.HandDirL;
            pose.HandDirR = rotation * pose.HandDirR;
            pose.PalmNormalL = rotation * pose.PalmNormalL;
            pose.PalmNormalR = rotation * pose.PalmNormalR;
        }

        /// <summary>The frame's whole-body rotation: pitch about the left axis, then bank about the facing.</summary>
        public static Quaternion WholeBodyRotation(WrestlerFrame frame)
        {
            Vector3 forward = WrestlerFrame.OnMat(frame.Facing).normalized;
            Vector3 left = Vector3.Cross(forward, Vector3.up).normalized;
            return Quaternion.AngleAxis(frame.Bank, forward) * Quaternion.AngleAxis(frame.Pitch, -left);
        }

        /// <summary>The waist point that a whole-body rotation turns about (see <see cref="ApplyWholeBody"/>).</summary>
        public static Vector3 WholeBodyPivot(in Pose pose) => Vector3.Lerp(pose.Pelvis, pose.Neck, ClipKinematics.WholeBodyPivotShare);

        /// <summary>
        /// A contact point on the surface of a posed body: where a held body is seated on it at
        /// that anatomical site (see <see cref="ClipKinematics.Seat"/>), the site's depth out from
        /// the curved spine along <see cref="AnchorNormal"/>.
        /// </summary>
        public static Vector3 AnchorOn(in Pose pose, BodyProportions body, AnchorPoint point)
        {
            (float share, float _, float _, float _, float depth) = ClipKinematics.Seat(point);
            return pose.SpinePoint(share) + (AnchorNormal(in pose, point) * (depth * body.Height));
        }

        /// <summary>The outward surface normal at an anchor site: the back over the hips, lower back and shoulders (tipping up the spine at the shoulders), the front at the chest, up and out over one shoulder.</summary>
        public static Vector3 AnchorNormal(in Pose pose, AnchorPoint point)
        {
            (float _, float frontSign, float spineBlend, float leftSign, float _) = ClipKinematics.Seat(point);
            Vector3 front = point == AnchorPoint.Hips ? pose.PelvisFront : pose.ChestNormal;
            Vector3 normal = (front * frontSign) + (pose.Spine * spineBlend) + (pose.Left * leftSign);
            return normal.sqrMagnitude > 1e-8f ? normal.normalized : Vector3.up;
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
                if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(Vector3.up, direction);
                if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(Vector3.right, direction);
            }

            return root + (direction * along) + (bend.normalized * across);
        }

        /// <summary>
        /// Keeps a posted wrist and elbow above the mat without changing either bone length.
        /// The elbow moves around the shoulder-wrist axis; only when that circle cannot reach
        /// the mat plane does the wrist slide along the plane to the nearest feasible reach.
        /// </summary>
        private static Vector3 ArmAboveMat(Vector3 root, Vector3 target, float upper, float lower, Vector3 pole, out Vector3 end)
        {
            float floor = ClipKinematics.ForearmElbowHeight;
            target.y = Mathf.Max(target.y, ClipKinematics.ForearmWristHeight);
            Vector3 elbow = TwoBone(root, target, upper, lower, pole, out end);
            if (elbow.y >= floor) return elbow;

            if (ElbowAbovePlane(root, end, elbow, floor, out Vector3 onPlane)) return onPlane;

            // At a very short low reach, a longer upper arm would put the elbow below the
            // mat for every swivel. The two limb spheres intersect the mat in circles: their
            // radii give the range of horizontal wrist distances that keeps both lengths.
            float rootRise = root.y - floor;
            float endRise = target.y - floor;
            float upperRun = Mathf.Sqrt(Mathf.Max(0f, (upper * upper) - (rootRise * rootRise)));
            float lowerRun = Mathf.Sqrt(Mathf.Max(0f, (lower * lower) - (endRise * endRise)));
            Vector3 horizontal = Vector3.ProjectOnPlane(target - root, Vector3.up);
            float distance = horizontal.magnitude;
            Vector3 direction = distance > 1e-6f ? horizontal / distance : Vector3.ProjectOnPlane(pole, Vector3.up).normalized;
            if (direction.sqrMagnitude < 1e-6f) direction = Vector3.forward;
            float closest = Mathf.Clamp(distance, Mathf.Abs(upperRun - lowerRun) + 0.001f, Mathf.Max(0.001f, upperRun + lowerRun - 0.001f));
            target = new Vector3(root.x, target.y, root.z) + (direction * closest);
            elbow = TwoBone(root, target, upper, lower, pole, out end);
            return ElbowAbovePlane(root, end, elbow, floor, out onPlane) ? onPlane : elbow;
        }

        private static bool ElbowAbovePlane(Vector3 root, Vector3 end, Vector3 elbow, float floor, out Vector3 result)
        {
            Vector3 axis = (end - root).normalized;
            Vector3 centre = root + (axis * Vector3.Dot(elbow - root, axis));
            Vector3 radial = elbow - centre;
            float radius = radial.magnitude;
            Vector3 vertical = Vector3.ProjectOnPlane(Vector3.up, axis);
            float rise = vertical.magnitude;
            if (radius < 1e-7f || rise < 1e-7f)
            {
                result = elbow;
                return elbow.y >= floor - 1e-6f;
            }

            float up = (floor - centre.y) / (radius * rise);
            if (up > 1f + 1e-5f)
            {
                result = elbow;
                return false;
            }

            vertical /= rise;
            up = Mathf.Clamp(up, -1f, 1f);
            Vector3 side = Vector3.Cross(axis, vertical).normalized;
            float across = Mathf.Sqrt(Mathf.Max(0f, 1f - (up * up))) * (Vector3.Dot(radial, side) < 0f ? -1f : 1f);
            result = centre + (radius * ((vertical * up) + (side * across)));
            return true;
        }
    }
}
