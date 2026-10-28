using UnityEngine;
using Wrestling.Core.Match;

namespace Wrestling.Presentation
{
    /// <summary>
    /// Keeps the two figures from passing through each other. Each body is approximated by its
    /// torso, head and thighs; overlapping volumes push the bodies apart, the more mobile body
    /// (standing over kneeling over lying) moving further. Display only.
    /// </summary>
    public static class BodyContact
    {
        /// <summary>Overlap left in place so bodies stay in contact rather than floating apart.</summary>
        private const float ContactTolerance = 0.02f;
        private const int Iterations = 3;

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
            float mobilityA = Mobility(groundA);
            float mobilityB = Mobility(groundB);
            float shareA = mobilityA / (mobilityA + mobilityB);
            for (int iteration = 0; iteration < Iterations; iteration++)
            {
                Segment[] volumesA = Volumes(in a, bodyA);
                Segment[] volumesB = Volumes(in b, bodyB);
                Vector3 push = Vector3.zero;
                foreach (Segment first in volumesA)
                {
                    foreach (Segment second in volumesB)
                    {
                        (Vector3 pa, Vector3 pb) = Closest(first, second);
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
                    return;
                }

                Translate(ref a, push * shareA, bodyA);
                Translate(ref b, -push * (1f - shareA), bodyB);
            }
        }

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
            float torso = (body.TorsoHalfWidth + body.TorsoHalfDepth) * 0.5f;
            return new[]
            {
                new Segment(pose.Pelvis, pose.Neck, torso),
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
        private static (Vector3 OnFirst, Vector3 OnSecond) Closest(Segment first, Segment second)
        {
            Vector3 d1 = first.B - first.A;
            Vector3 d2 = second.B - second.A;
            Vector3 r = first.A - second.A;
            float a = Vector3.Dot(d1, d1);
            float e = Vector3.Dot(d2, d2);
            float f = Vector3.Dot(d2, r);
            float s;
            float t;
            if (a <= 1e-8f && e <= 1e-8f)
            {
                return (first.A, second.A);
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

            return (first.A + (d1 * s), second.A + (d2 * t));
        }
    }
}
