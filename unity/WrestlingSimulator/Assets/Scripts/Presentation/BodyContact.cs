using UnityEngine;
using Wrestling.Core.Match;

namespace Wrestling.Presentation
{
    /// <summary>
    /// Settles the two figures against each other. Each body is approximated by its pelvis, chest,
    /// head and thighs; overlapping volumes push the bodies apart over several passes, each body
    /// moving by its share (the more mobile and the lighter moves further: standing over kneeling
    /// over lying). Torsos left just short of touching in a tie-up or with one wrestler over the
    /// other lean in until chest meets chest or back. Through all of it planted feet stay where
    /// they stand and knees on the mat stay on it. Display only.
    /// </summary>
    public static class BodyContact
    {
        /// <summary>Overlap left in place so bodies stay in contact rather than floating apart.</summary>
        private const float ContactTolerance = 0.02f;
        private const int Iterations = 6;

        /// <summary>Torsos closer than this (m) without touching lean in until they meet, by at most <see cref="MaxLean"/> degrees.</summary>
        private const float PressureRange = 0.1f;
        private const float MaxLean = 9f;

        /// <summary>An ankle this low (m) is a planted foot; a knee this low is on the mat.</summary>
        private const float PlantedAnkle = 0.12f;
        private const float KneeDown = 0.1f;

        private readonly struct Segment
        {
            public Segment(Vector3 a, Vector3 b, float radius)
            {
                A = a;
                B = b;
                Radius = radius;
            }

            public Vector3 A { get; }

            public Vector3 B { get; }

            public float Radius { get; }
        }

        public static void Separate(ref Pose a, BodyProportions bodyA, GroundState groundA, ref Pose b, BodyProportions bodyB, GroundState groundB)
        {
            float freedomA = Mobility(groundA) / Mass(bodyA);
            float freedomB = Mobility(groundB) / Mass(bodyB);
            float shareA = freedomA / (freedomA + freedomB);
            Pose restA = a;
            Pose restB = b;
            for (int iteration = 0; iteration < Iterations; iteration++)
            {
                Segment[] volumesA = Volumes(in a, bodyA);
                Segment[] volumesB = Volumes(in b, bodyB);
                Vector3 push = Vector3.zero;
                foreach (Segment first in volumesA)
                {
                    foreach (Segment second in volumesB)
                    {
                        (Vector3 pa, Vector3 pb) = Closest(first.A, first.B, second.A, second.B);
                        Vector3 between = pa - pb;
                        float distance = between.magnitude;
                        float overlap = first.Radius + second.Radius - ContactTolerance - distance;
                        if (overlap <= 0f)
                        {
                            continue;
                        }

                        Vector3 direction = distance > 1e-4f ? between / distance : Horizontal(a.Pelvis - b.Pelvis);
                        Vector3 candidate = direction * overlap;
                        if (candidate.sqrMagnitude > push.sqrMagnitude)
                        {
                            push = candidate;
                        }
                    }
                }

                if (push.sqrMagnitude < 1e-8f)
                {
                    break;
                }

                Translate(ref a, push * shareA, bodyA);
                Translate(ref b, -push * (1f - shareA), bodyB);
            }

            Press(ref a, bodyA, groundA, ref b, bodyB, groundB, shareA);
            LockLegs(ref a, in restA);
            LockLegs(ref b, in restB);
        }

        /// <summary>Distance between two segments.</summary>
        public static float SegmentDistance(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1)
        {
            (Vector3 pa, Vector3 pb) = Closest(a0, a1, b0, b1);
            return (pa - pb).magnitude;
        }

        /// <summary>
        /// Chest-to-chest (standing, facing) or chest-to-back (one over the other on the mat):
        /// torsos just short of touching lean in about the pelvis until they meet, each standing
        /// wrestler by their share, a wrestler over the other alone.
        /// </summary>
        private static void Press(ref Pose a, BodyProportions bodyA, GroundState groundA, ref Pose b, BodyProportions bodyB, GroundState groundB, float shareA)
        {
            (Vector3 pa, Vector3 pb) = Closest(a.SpinePoint(0.45f), a.SpinePoint(0.9f), b.SpinePoint(0.3f), b.SpinePoint(0.9f));
            Vector3 between = pb - pa;
            float distance = between.magnitude;
            float gap = distance - TorsoRadius(bodyA) - TorsoRadius(bodyB) + ContactTolerance;
            if (gap <= 0f || gap > PressureRange || distance < 1e-4f)
            {
                return;
            }

            Vector3 towardsB = between / distance;
            bool standing = groundA == GroundState.Standing && groundB == GroundState.Standing;
            if (standing)
            {
                bool facing = Vector3.Dot(a.ChestNormal, b.ChestNormal) < -0.3f && Vector3.Dot(a.ChestNormal, towardsB) > 0.3f && Vector3.Dot(b.ChestNormal, -towardsB) > 0.3f;
                if (facing)
                {
                    Lean(ref a, pa, towardsB, gap * shareA);
                    Lean(ref b, pb, -towardsB, gap * (1f - shareA));
                }

                return;
            }

            if (groundA != GroundState.Standing && Vector3.Dot(a.ChestNormal, towardsB) > 0.5f && pa.y > pb.y + 0.05f)
            {
                Lean(ref a, pa, towardsB, gap);
            }
            else if (groundB != GroundState.Standing && Vector3.Dot(b.ChestNormal, -towardsB) > 0.5f && pb.y > pa.y + 0.05f)
            {
                Lean(ref b, pb, -towardsB, gap);
            }
        }

        /// <summary>Tips everything above the pelvis about it so <paramref name="point"/> moves <paramref name="distance"/> along <paramref name="direction"/>.</summary>
        private static void Lean(ref Pose pose, Vector3 point, Vector3 direction, float distance)
        {
            Vector3 lever = point - pose.Pelvis;
            float arm = Vector3.ProjectOnPlane(lever, direction).magnitude;
            Vector3 axis = Vector3.Cross(lever, direction);
            if (arm < 0.1f || axis.sqrMagnitude < 1e-8f || distance <= 0f)
            {
                return;
            }

            float degrees = Mathf.Min(distance / arm * Mathf.Rad2Deg, MaxLean);
            Quaternion turn = Quaternion.AngleAxis(degrees, axis.normalized);
            if (Vector3.Dot((turn * lever) - lever, direction) < 0f)
            {
                turn = Quaternion.AngleAxis(-degrees, axis.normalized);
            }

            Vector3 pelvis = pose.PelvisFront;
            Vector3 pivot = pose.Pelvis;
            Vector3 Turn(Vector3 p) => pivot + (turn * (p - pivot));
            pose.Neck = Turn(pose.Neck);
            pose.Head = Turn(pose.Head);
            pose.ShoulderL = Turn(pose.ShoulderL);
            pose.ShoulderR = Turn(pose.ShoulderR);
            pose.ElbowL = Turn(pose.ElbowL);
            pose.ElbowR = Turn(pose.ElbowR);
            pose.HandL = Turn(pose.HandL);
            pose.HandR = Turn(pose.HandR);
            pose.ChestNormal = turn * pose.ChestNormal;
            pose.Left = turn * pose.Left;
            pose.Spine = (pose.Neck - pose.Pelvis).normalized;
            PoseSolver.RotateFrames(ref pose, turn);
            // The pelvis stays where it was; the spine takes the lean.
            pose.PelvisNormal = pelvis;
        }

        /// <summary>
        /// Puts planted feet back where they stood (the knee re-bending to reach them from the moved
        /// hip), and keeps knees and shins that were on the mat on it.
        /// </summary>
        private static void LockLegs(ref Pose pose, in Pose rest)
        {
            if ((pose.Pelvis - rest.Pelvis).sqrMagnitude < 1e-8f)
            {
                return;
            }

            LockLeg(pose.HipL, ref pose.KneeL, ref pose.AnkleL, ref pose.ToeL, rest.HipL, rest.KneeL, rest.AnkleL, rest.ToeL);
            LockLeg(pose.HipR, ref pose.KneeR, ref pose.AnkleR, ref pose.ToeR, rest.HipR, rest.KneeR, rest.AnkleR, rest.ToeR);
        }

        private static void LockLeg(Vector3 hip, ref Vector3 knee, ref Vector3 ankle, ref Vector3 toe, Vector3 restHip, Vector3 restKnee, Vector3 restAnkle, Vector3 restToe)
        {
            if (restKnee.y < KneeDown)
            {
                // Kneeling: the shin slides with the body but stays on the mat.
                knee.y = restKnee.y;
                ankle.y = restAnkle.y;
                toe.y = restToe.y;
                return;
            }

            if (restAnkle.y >= PlantedAnkle)
            {
                return;
            }

            float thigh = (restKnee - restHip).magnitude;
            float shin = (restAnkle - restKnee).magnitude;
            Vector3 pole = restKnee - Vector3.Lerp(restHip, restAnkle, 0.5f);
            knee = PoseSolver.TwoBone(hip, restAnkle, thigh, shin, pole, out Vector3 reached);
            ankle = reached;
            toe = restToe + (reached - restAnkle);
        }

        /// <summary>Relative mass from trunk volume (only ratios are used).</summary>
        private static float Mass(BodyProportions body) => Mathf.Max(1e-4f, body.Torso * body.TorsoHalfWidth * body.TorsoHalfDepth);

        private static float TorsoRadius(BodyProportions body) => (body.TorsoHalfWidth + body.TorsoHalfDepth) * 0.5f;

        private static float Mobility(GroundState ground)
        {
            switch (ground)
            {
                case GroundState.Standing:
                    return 1f;
                case GroundState.OneKnee:
                case GroundState.BothKnees:
                case GroundState.Seated:
                    return 0.5f;
                default:
                    return 0.15f;
            }
        }

        private static Segment[] Volumes(in Pose pose, BodyProportions body)
        {
            float torso = TorsoRadius(body);
            Vector3 waist = pose.SpinePoint(0.45f);
            return new[]
            {
                new Segment(pose.Pelvis, waist, torso),
                new Segment(waist, pose.Neck, torso),
                new Segment(pose.Head, pose.Head, body.HeadRadius * 0.9f),
                new Segment(pose.HipL, pose.KneeL, body.LimbRadius * 1.4f),
                new Segment(pose.HipR, pose.KneeR, body.LimbRadius * 1.4f),
            };
        }

        private static Vector3 Horizontal(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.right;
        }

        private static void Translate(ref Pose pose, Vector3 offset, BodyProportions body)
        {
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

            Vector3 Move(Vector3 point)
            {
                Vector3 moved = point + offset;
                return moved.y < body.LimbRadius * 0.5f ? new Vector3(moved.x, Mathf.Max(point.y, body.LimbRadius * 0.5f), moved.z) : moved;
            }
        }

        /// <summary>Closest points between two segments (Ericson, Real-Time Collision Detection 5.1.9).</summary>
        private static (Vector3 OnFirst, Vector3 OnSecond) Closest(Vector3 firstA, Vector3 firstB, Vector3 secondA, Vector3 secondB)
        {
            Vector3 d1 = firstB - firstA;
            Vector3 d2 = secondB - secondA;
            Vector3 r = firstA - secondA;
            float a = Vector3.Dot(d1, d1);
            float e = Vector3.Dot(d2, d2);
            float f = Vector3.Dot(d2, r);
            float s;
            float t;
            if (a <= 1e-8f && e <= 1e-8f)
            {
                return (firstA, secondA);
            }

            if (a <= 1e-8f)
            {
                s = 0f;
                t = Mathf.Clamp01(f / e);
            }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= 1e-8f)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-c / a);
                }
                else
                {
                    float b = Vector3.Dot(d1, d2);
                    float denominator = (a * e) - (b * b);
                    s = denominator > 1e-8f ? Mathf.Clamp01(((b * f) - (c * e)) / denominator) : 0f;
                    t = ((b * s) + f) / e;
                    if (t < 0f)
                    {
                        t = 0f;
                        s = Mathf.Clamp01(-c / a);
                    }
                    else if (t > 1f)
                    {
                        t = 1f;
                        s = Mathf.Clamp01((b - c) / a);
                    }
                }
            }

            return (firstA + (d1 * s), secondA + (d2 * t));
        }
    }
}
