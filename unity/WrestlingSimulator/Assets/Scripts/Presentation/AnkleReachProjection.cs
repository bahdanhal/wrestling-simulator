using UnityEngine;

namespace Wrestling.Presentation
{
    /// <summary>
    /// Finds the least vertical translation of both hips that permits fixed-length legs to
    /// reach their unchanged ankle targets. This is geometric reach, not a contact model.
    /// </summary>
    public static class AnkleReachProjection
    {
        public readonly struct Result
        {
            public readonly float DeltaY;
            public readonly bool Feasible;
            public Result(float deltaY, bool feasible) { DeltaY = deltaY; Feasible = feasible; }
        }

        private readonly struct Interval
        {
            public readonly double Min;
            public readonly double Max;
            public Interval(double min, double max) { Min = min; Max = max; }
        }

        /// <summary>Returns (0, false) when inputs are invalid or no common correction exists.</summary>
        public static Result Solve(Vector3 leftHip, Vector3 leftAnkle, float leftUpper, float leftLower,
            Vector3 rightHip, Vector3 rightAnkle, float rightUpper, float rightLower, float maxCorrection = .35f)
        {
            if (!Finite(maxCorrection) || maxCorrection < 0f
                || !Intervals(leftHip, leftAnkle, leftUpper, leftLower, out Interval l0, out Interval l1)
                || !Intervals(rightHip, rightAnkle, rightUpper, rightLower, out Interval r0, out Interval r1))
                return new Result(0f, false);

            bool found = false;
            float best = 0f;
            Consider(l0, r0, maxCorrection, ref found, ref best);
            Consider(l0, r1, maxCorrection, ref found, ref best);
            Consider(l1, r0, maxCorrection, ref found, ref best);
            Consider(l1, r1, maxCorrection, ref found, ref best);
            return new Result(best, found);
        }

        private static bool Intervals(Vector3 hip, Vector3 ankle, float upper, float lower,
            out Interval negative, out Interval positive)
        {
            negative = default; positive = default;
            if (!Finite(hip.x) || !Finite(hip.y) || !Finite(hip.z)
                || !Finite(ankle.x) || !Finite(ankle.y) || !Finite(ankle.z)
                || !Finite(upper) || !Finite(lower) || upper <= 0f || lower <= 0f)
                return false;

            // Double precision avoids intermediate float overflow and improves boundary accuracy.
            double dx = (double)hip.x - ankle.x, dz = (double)hip.z - ankle.z;
            double horizontalSquared = (dx * dx) + (dz * dz);
            double maxReach = (double)upper + lower;
            double minReach = System.Math.Abs((double)upper - lower);
            double maxSquared = (maxReach * maxReach) - horizontalSquared;
            if (maxSquared < 0d) return false;
            double outer = System.Math.Sqrt(maxSquared);
            double inner = System.Math.Sqrt(System.Math.Max(0d, (minReach * minReach) - horizontalSquared));
            double vertical = (double)hip.y - ankle.y;
            negative = new Interval(-outer - vertical, -inner - vertical);
            positive = new Interval(inner - vertical, outer - vertical);
            return true;
        }

        private static void Consider(Interval a, Interval b, float limit, ref bool found, ref float best)
        {
            double min = System.Math.Max(-limit, System.Math.Max(a.Min, b.Min));
            double max = System.Math.Min(limit, System.Math.Min(a.Max, b.Max));
            if (min > max) return;
            float candidate = (float)System.Math.Max(min, System.Math.Min(0d, max));
            if (!found || Mathf.Abs(candidate) < Mathf.Abs(best)) { best = candidate; found = true; }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
