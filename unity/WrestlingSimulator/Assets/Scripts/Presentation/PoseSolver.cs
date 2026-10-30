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

        /// <summary>
        /// Places both hands: in their grip sockets on the opponent when gripping, otherwise in a ready
        /// or supporting position. The clavicles lift and reach with the arms, the elbows swing clear
        /// of both bodies, and each hand is turned to its socket or task.
        /// </summary>
        public static void SolveArms(ref Pose pose, BodyProportions body, PoseRole role, WrestlerFrame frame, in Pose opponent, IEnumerable<Grip> grips)
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

            bool lying = frame.Ground == GroundState.Prone || frame.Ground == GroundState.Supine || frame.Ground == GroundState.Bridge;
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
            Vector3 elbowPoleL = (-Vector3.up * 0.7f) + (pose.Left * 0.6f) - (reachForward * 0.2f);
            Vector3 elbowPoleR = (-Vector3.up * 0.7f) - (pose.Left * 0.6f) - (reachForward * 0.2f);
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
        }

        /// <summary>
        /// One arm towards its target: solved from the rest shoulder, the clavicle then lifts and
        /// reaches with the upper arm, the arm is solved again from the moved shoulder, and the elbow
        /// swings about the shoulder-hand line to clear both bodies.
        /// </summary>
        private static void SolveArm(ref Pose pose, BodyProportions body, bool left, Vector3 target, Vector3 pole, in Pose opponent, float otherScale, bool hasOpponent)
        {
            Vector3 rest = left ? pose.ShoulderL : pose.ShoulderR;
            Vector3 lateral = left ? pose.Left : -pose.Left;
            Vector3 shoulderBase = pose.Neck - (pose.Spine * body.ShoulderDrop);
            Vector3 root = shoulderBase + (lateral * (body.ShoulderHalfWidth * ClavicleRoot));
            Vector3 elbow = TwoBone(rest, target, body.UpperArm, body.Forearm, pole, out _);
            Vector3 shoulder = Clavicle(rest, root, elbow, pose.Spine, pose.ChestNormal, lateral);
            elbow = TwoBone(shoulder, target, body.UpperArm, body.Forearm, pole, out Vector3 hand);
            elbow = Swivel(shoulder, elbow, hand, in pose, body, in opponent, otherScale, hasOpponent);
            KeepAboveMat(ref elbow, body.LimbRadius);
            KeepAboveMat(ref hand, body.LimbRadius);
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
                    // Flat on the mat, fingers forward and a little out.
                    return (Vector3.ProjectOnPlane(reachForward + (Vector3.ProjectOnPlane(along, Vector3.up) * 0.4f), Vector3.up).normalized, Vector3.down, 1f);
                case FreeHand.Forearms:
                    return (Vector3.ProjectOnPlane(along, Vector3.up).normalized, Vector3.down, 0.85f);
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
            float hipHeight = Mathf.Min(AnkleHeight + (legLength * (0.4f + (0.5f * frame.Level))), AnkleHeight + (legLength * 0.985f));
            // A wrestling stance leans in even at full posture: chest over the knees, ready to react.
            float lean = Mathf.Lerp(62f, 24f, frame.Posture) + ((1f - frame.Level) * 14f) + ((1f - frame.Balance) * 12f);
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
            pose.PelvisNormal = pose.ChestNormal;
            TwistChest(ref pose, frame.TorsoRotation * GroundTwistShare, frame.RollSide);
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
                    return pose.SpinePoint(0.3f) - (pose.ChestNormal * body.TorsoHalfDepth);
                case AnchorPoint.Shoulders:
                    return pose.Neck - (pose.Spine * (body.Torso * 0.08f)) - (pose.ChestNormal * (body.TorsoHalfDepth * 0.4f));
                case AnchorPoint.Chest:
                    return pose.SpinePoint(0.65f) + (pose.ChestNormal * body.TorsoHalfDepth);
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
