using System;
using System.Numerics;
using Wrestling.Core.Match;

namespace Wrestling.Data.Animation
{
    /// <summary>
    /// The body geometry a clip key stands for: hip height and trunk lean for each ground state,
    /// the compact par terre base, where a held body is seated on the other, and the whole-body
    /// lift and turn. The bout viewer's pose solver builds its figures from these formulas, and the
    /// content validator (<see cref="ClipFeasibility"/>) measures clips with them, so the motion
    /// that is checked is the motion that is shown. Presentation geometry only (floats): the
    /// simulation never reads it.
    /// </summary>
    public static class ClipKinematics
    {
        /// <summary>Height (m) of the ankle joint of a foot flat on the mat, and of a knee resting on it.</summary>
        public const float AnkleHeight = 0.07f;

        public const float KneeOnMatHeight = 0.06f;

        /// <summary>Height (m) of the elbow and of the wrist of a forearm lying flat on the mat (the compact base).</summary>
        public const float ForearmElbowHeight = 0.045f;

        public const float ForearmWristHeight = 0.025f;

        /// <summary>Share of the way from the pelvis to the neck of the point whole-body rotations turn about (the waist).</summary>
        public const float WholeBodyPivotShare = 0.35f;

        /// <summary>Standard gravity (m/s²).</summary>
        public const float Gravity = 9.81f;

        /// <summary>
        /// Hip-joint height (m) standing: a wrestling stance sinks the hips on bent knees rather than
        /// folding at the hips, so level sets how deep the knees bend.
        /// </summary>
        public static float StandingHipHeight(float level, float legLength) =>
            Math.Min(AnkleHeight + (legLength * (0.4f + (0.5f * level))), AnkleHeight + (legLength * 0.985f));

        /// <summary>
        /// Trunk lean from vertical (degrees) standing: even at full posture the chest stays over
        /// the knees; low hips and lost balance lean it further.
        /// </summary>
        public static float StandingLean(float posture, float level, float balance) =>
            Lerp(62f, 24f, posture) + ((1f - level) * 14f) + ((1f - balance) * 12f);

        /// <summary>The standing level that puts the hip joints at <paramref name="hipHeight"/> (inverse of <see cref="StandingHipHeight"/>), within 0–1.</summary>
        public static float StandingLevelForHip(float hipHeight, float legLength) =>
            Clamp01((((hipHeight - AnkleHeight) / Math.Max(legLength, 1e-3f)) - 0.4f) / 0.5f);

        /// <summary>The standing posture that leans the trunk <paramref name="lean"/> degrees (inverse of <see cref="StandingLean"/>), within 0–1.</summary>
        public static float StandingPostureForLean(float lean, float level, float balance) =>
            Clamp01((62f - (lean - ((1f - level) * 14f) - ((1f - balance) * 12f))) / 38f);

        /// <summary>Hip-joint height (m) kneeling up on the thighs (active kneeling: on one knee, or attacking from the knees).</summary>
        public static float KneelingHipHeight(float level, float thigh) =>
            KneeOnMatHeight + (thigh * Lerp(0.55f, 0.97f, Clamp01(level / 0.6f)));

        /// <summary>Trunk lean from vertical (degrees) kneeling up.</summary>
        public static float KneelingLean(float posture) => Lerp(80f, 8f, posture);

        /// <summary>
        /// How far a bottom wrestler on both knees is in the compact defensive base (1) rather than
        /// up on hands and knees (0): it closes as the posture drops below about 0.45.
        /// </summary>
        public static float CompactBase(float posture) => 1f - SmoothStep(0.35f, 0.5f, posture);

        /// <summary>Hip-joint height (m) in the compact base: the pelvis sunk back onto the heels.</summary>
        public static float CompactHipHeight(float level) => Lerp(0.16f, 0.28f, Clamp01(level));

        /// <summary>
        /// Trunk lean (degrees) in the compact base: low enough that the shoulders sit just over
        /// forearms lying flat on the mat, the upper arms close to vertical.
        /// </summary>
        public static float CompactLean(float hipHeight, float torso, float shoulderDrop, float upperArm)
        {
            float shoulders = ForearmElbowHeight + (upperArm * 0.92f);
            float reach = Math.Max(torso - shoulderDrop, 1e-3f);
            float cos = Clamp((shoulders - hipHeight) / reach, -1f, 1f);
            return Clamp((float)(Math.Acos(cos) * 180.0 / Math.PI), 50f, 85f);
        }

        /// <summary>Pelvis height (m) of a body lying on the mat, rolled <paramref name="rollDegrees"/> (0 chest down, 90 on the side).</summary>
        public static float LyingRestHeight(float rollDegrees, float torsoHalfDepth, float shoulderHalfWidth, bool bridge)
        {
            float onSide = (float)Math.Sin(rollDegrees * Math.PI / 180.0);
            float rest = Lerp(torsoHalfDepth, shoulderHalfWidth * 0.9f, onSide);
            return bridge ? rest + 0.22f : rest;
        }

        /// <summary>True when a key lies on the mat (the lying pose rather than kneeling or standing).</summary>
        public static bool IsLying(GroundState ground, float roll) =>
            ground == GroundState.Prone || ground == GroundState.Supine || ground == GroundState.Bridge
            || (ground != GroundState.Standing && roll >= 45f);

        /// <summary>
        /// Where a held body's waist sits on the carrier at an anchor: a point on the carrier's
        /// curved spine (share from the pelvis to the neck), the surface normal there as a blend of
        /// the chest's front (sign), the spine and the carrier's left (sign), and the seat radius
        /// from the spine to the held waist (the carrier's anatomical depth at that site plus the
        /// held body's own thickness, <see cref="ActorKey.Contact"/>). Depths are shares of the
        /// carrier's height.
        /// </summary>
        public static (float SpineShare, float FrontSign, float SpineBlend, float LeftSign, float DepthShare) Seat(AnchorPoint point)
        {
            switch (point)
            {
                case AnchorPoint.Hips:
                    // Over the back of the iliac crest and sacrum: about 0.13 m behind the hip joints.
                    return (0f, -1f, 0.25f, 0f, 0.074f);
                case AnchorPoint.LowerBack:
                    return (0.3f, -1f, 0f, 0f, 0.074f);
                case AnchorPoint.Shoulders:
                    // Across the trapezius ridge behind the neck, up over the shoulder blades: about 0.19 m.
                    return (0.88f, -1f, 0.5f, 0f, 0.109f);
                case AnchorPoint.Chest:
                    return (0.65f, 1f, 0f, 0f, 0.086f);
                case AnchorPoint.LeftShoulder:
                case AnchorPoint.RightShoulder:
                    // Up and out over the acromion, half way between the spine's line and the side:
                    // about 0.24 m from the upper spine, which puts the seated waist (with the held
                    // body's thickness) about 0.25 m out, beyond the shoulder's half width.
                    return (0.9f, 0f, 1f, point == AnchorPoint.LeftShoulder ? 1f : -1f, 0.137f);
                default:
                    return (WholeBodyPivotShare, 1f, 0f, 0f, 0f);
            }
        }

        /// <summary>Whether <paramref name="point"/> carries the held body on the shoulders (across them or up on one).</summary>
        public static bool OnShoulders(AnchorPoint point) =>
            point == AnchorPoint.Shoulders || point == AnchorPoint.LeftShoulder || point == AnchorPoint.RightShoulder;

        /// <summary>The reference body clips are checked on: segment lengths (m) of a 1.75 m wrestler with average proportions.</summary>
        public readonly struct Body
        {
            public Body(float height, float legRatio)
            {
                Height = height;
                float leg = height * legRatio;
                Thigh = leg * 0.52f;
                Shin = leg * 0.48f;
                float neck = height * 0.04f;
                float headRadius = height * 0.065f;
                Torso = height - leg - neck - (headRadius * 2f);
                ShoulderHalfWidth = height * 0.115f;
                ShoulderDrop = height * 0.03f;
                TorsoHalfDepth = height * 0.065f;
                LimbRadius = height * 0.032f;
                UpperArm = height * 0.16f;
            }

            public static Body Reference => new Body(1.75f, 0.47f);

            public float Height { get; }

            public float Thigh { get; }

            public float Shin { get; }

            public float Torso { get; }

            public float ShoulderHalfWidth { get; }

            public float ShoulderDrop { get; }

            public float TorsoHalfDepth { get; }

            public float LimbRadius { get; }

            public float UpperArm { get; }
        }

        /// <summary>A body's trunk as a key places it: pelvis, neck, the chest's front and the wrestler's left (unit vectors).</summary>
        public struct Trunk
        {
            public Vector3 Pelvis;
            public Vector3 Neck;
            public Vector3 Front;
            public Vector3 Left;

            public Vector3 Spine => Vector3.Normalize(Neck - Pelvis);

            public Vector3 SpinePoint(float t) => Vector3.Lerp(Pelvis, Neck, t);

            public Vector3 Waist => SpinePoint(WholeBodyPivotShare);

            /// <summary>Trunk lean from vertical (degrees).</summary>
            public float LeanDegrees => (float)(Math.Acos(Clamp(Spine.Y, -1f, 1f)) * 180.0 / Math.PI);

            public void Move(Vector3 offset)
            {
                Pelvis += offset;
                Neck += offset;
            }

            /// <summary>The held waist's place when seated on this body at <paramref name="anchor"/>.</summary>
            public Vector3 SeatFor(AnchorPoint anchor, float contact, float height)
            {
                (float share, float sign, float blend, float leftSign, float depth) = Seat(anchor);
                Vector3 normal = (Front * sign) + (Spine * blend) + (Left * leftSign);
                normal = normal.LengthSquared() > 1e-8f ? Vector3.Normalize(normal) : Vector3.UnitY;
                return SpinePoint(share) + (normal * ((depth * height) + contact));
            }
        }

        /// <summary>
        /// The trunk of one actor at a key, in the clip's frame (x forward, y up, z left), before
        /// any anchor hold: the ground state's own pose, then the whole-body pitch, bank and lift.
        /// <paramref name="compactBase"/> marks the bottom wrestler of a par terre clip.
        /// </summary>
        public static Trunk Place(ActorKey key, Body body, bool compactBase)
        {
            Trunk trunk;
            if (key.GroundFrom.HasValue && key.GroundTo.HasValue)
            {
                Trunk a = PlaceDiscrete(key, body, compactBase, key.GroundFrom.Value, key.GroundFromLying);
                Trunk b = PlaceDiscrete(key, body, compactBase, key.GroundTo.Value, key.GroundToLying);
                float t = Clamp01(key.GroundBlend);
                Quaternion rotation = Quaternion.Slerp(Orientation(a), Orientation(b), t);
                Vector3 pelvis = Vector3.Lerp(a.Pelvis, b.Pelvis, t);
                trunk = new Trunk
                {
                    Pelvis = pelvis,
                    Neck = pelvis + (Vector3.Transform(Vector3.UnitY, rotation) * Lerp((a.Neck - a.Pelvis).Length(), (b.Neck - b.Pelvis).Length(), t)),
                    Front = Vector3.Transform(Vector3.UnitZ, rotation),
                    Left = -Vector3.Transform(Vector3.UnitX, rotation),
                };
                // Preserve endpoint geometry exactly (bridging has its own neck height).
                if (t <= 0f) trunk = a;
                else if (t >= 1f) trunk = b;
            }
            else
            {
                trunk = PlaceDiscrete(key, body, compactBase, key.Ground);
            }

            double heading = key.Heading * Math.PI / 180.0;
            var forward = new Vector3((float)Math.Cos(heading), 0f, (float)Math.Sin(heading));
            var left = new Vector3(-forward.Z, 0f, forward.X);
            Turn(ref trunk, key, forward, left, body);
            return trunk;
        }

        private static Quaternion Orientation(Trunk trunk)
        {
            Vector3 right = -trunk.Left;
            Vector3 up = trunk.Spine;
            Vector3 front = Vector3.Normalize(Vector3.Cross(right, up));
            right = Vector3.Normalize(Vector3.Cross(up, front));
            return Quaternion.CreateFromRotationMatrix(new Matrix4x4(
                right.X, right.Y, right.Z, 0f,
                up.X, up.Y, up.Z, 0f,
                front.X, front.Y, front.Z, 0f,
                0f, 0f, 0f, 1f));
        }

        private static Trunk PlaceDiscrete(ActorKey key, Body body, bool compactBase, GroundState ground, bool? forceLying = null)
        {
            double heading = key.Heading * Math.PI / 180.0;
            var forward = new Vector3((float)Math.Cos(heading), 0f, (float)Math.Sin(heading));
            var left = new Vector3(-forward.Z, 0f, forward.X);
            var centre = new Vector3(key.X, 0f, key.Z);
            var trunk = new Trunk { Left = left };
            if (forceLying ?? IsLying(ground, key.Roll))
            {
                float roll = ground == GroundState.Prone ? Math.Min(key.Roll, 90f)
                    : ground == GroundState.Supine || ground == GroundState.Bridge ? Math.Max(key.Roll, 90f) : key.Roll;
                bool bridge = ground == GroundState.Bridge;
                float rest = LyingRestHeight(roll, body.TorsoHalfDepth, body.ShoulderHalfWidth, bridge);
                float neckRest = rest - (bridge ? 0.1f : 0f);
                trunk.Pelvis = centre - (forward * (body.Torso * 0.5f)) + (Vector3.UnitY * rest);
                trunk.Neck = centre + (forward * (body.Torso * 0.5f)) + (Vector3.UnitY * neckRest);
                double r = roll * Math.PI / 180.0;
                trunk.Front = Vector3.Normalize((-Vector3.UnitY * (float)Math.Cos(r)) + (left * ((float)Math.Sin(r) * (key.RollSide < 0 ? -1f : 1f))));
            }
            else
            {
                float hip;
                float lean;
                Vector3 pelvis;
                switch (ground)
                {
                    case GroundState.Standing:
                        hip = StandingHipHeight(key.Level, body.Thigh + body.Shin);
                        lean = StandingLean(key.Posture, key.Level, 1f);
                        pelvis = centre + (Vector3.UnitY * hip) - (forward * ((float)Math.Sin(lean * Math.PI / 180.0) * 0.04f));
                        break;
                    case GroundState.Seated:
                        hip = 0.13f;
                        lean = -15f;
                        pelvis = centre + (Vector3.UnitY * hip);
                        break;
                    default:
                    {
                        float compact = compactBase && ground == GroundState.BothKnees ? CompactBase(key.Posture) : 0f;
                        float up = KneelingHipHeight(key.Level, body.Thigh);
                        float low = CompactHipHeight(key.Level);
                        hip = Lerp(up, low, compact);
                        lean = Lerp(KneelingLean(key.Posture), CompactLean(low, body.Torso, body.ShoulderDrop, body.UpperArm), compact);
                        float back = (float)Math.Sqrt(Math.Max(0f, (body.Thigh * body.Thigh) - ((hip - KneeOnMatHeight) * (hip - KneeOnMatHeight))));
                        pelvis = centre + (Vector3.UnitY * hip) - (forward * (back * 0.5f));
                        break;
                    }
                }

                double l = lean * Math.PI / 180.0;
                Vector3 spine = (Vector3.UnitY * (float)Math.Cos(l)) + (forward * (float)Math.Sin(l));
                trunk.Pelvis = pelvis;
                trunk.Neck = pelvis + (spine * body.Torso);
                trunk.Front = Vector3.Normalize(Vector3.Cross(spine, left));
            }

            return trunk;
        }

        /// <summary>A held body moved so its waist (before lift) lands on the seat, by the anchor's weight.</summary>
        public static void Hold(ref Trunk held, ActorKey heldKey, in Trunk carrier, Body body)
        {
            if (heldKey.AnchorWeight <= 0f || heldKey.Anchor == AnchorPoint.None)
            {
                return;
            }

            Vector3 seat = carrier.SeatFor(heldKey.Anchor, heldKey.Contact, body.Height);
            held.Move((seat - held.Waist) * Clamp01(heldKey.AnchorWeight));
        }

        /// <summary>The key's whole-body pitch (about the left axis), then bank (about the facing), about the waist, then the lift.</summary>
        private static void Turn(ref Trunk trunk, ActorKey key, Vector3 forward, Vector3 left, Body body)
        {
            if (key.Lift <= 0f && key.Pitch == 0f && key.Bank == 0f)
            {
                return;
            }

            Quaternion rotation = Quaternion.CreateFromAxisAngle(forward, Radians(key.Bank)) * Quaternion.CreateFromAxisAngle(-left, Radians(key.Pitch));
            Vector3 pivot = trunk.Waist;
            var lift = new Vector3(0f, key.Lift, 0f);
            Vector3 Move(Vector3 p)
            {
                Vector3 moved = pivot + Vector3.Transform(p - pivot, rotation) + lift;
                return moved.Y < body.LimbRadius ? new Vector3(moved.X, body.LimbRadius, moved.Z) : moved;
            }

            trunk.Pelvis = Move(trunk.Pelvis);
            trunk.Neck = Move(trunk.Neck);
            trunk.Front = Vector3.Transform(trunk.Front, rotation);
            trunk.Left = Vector3.Transform(trunk.Left, rotation);
        }

        private static float Radians(float degrees) => (float)(degrees * Math.PI / 180.0);

        public static float Lerp(float a, float b, float t) => a + ((b - a) * t);

        public static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;

        public static float Clamp(float x, float min, float max) => x < min ? min : x > max ? max : x;

        public static float SmoothStep(float from, float to, float x)
        {
            float t = Clamp01((x - from) / (to - from));
            return t * t * (3f - (2f * t));
        }
    }
}
