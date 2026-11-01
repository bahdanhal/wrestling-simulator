using System;
using UnityEditor;
using UnityEngine;
using Wrestling.Presentation;

namespace Wrestling.Editor
{
    public static class AnkleReachProjectionChecks
    {
        public static void RunFromCommandLine()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        private static void Run()
        {
            Vector3 ankle = Vector3.zero;
            void Check(float leftY, float leftUpper, float leftLower, float rightY, float rightUpper,
                float rightLower, float limit, bool feasible, float expected)
            {
                var result = AnkleReachProjection.Solve(new Vector3(0, leftY, 0), ankle, leftUpper, leftLower,
                    new Vector3(0, rightY, 0), ankle, rightUpper, rightLower, limit);
                Require(result.Feasible == feasible && Mathf.Abs(result.DeltaY - expected) < 1e-5f,
                    $"Unexpected projection: feasible={result.Feasible}, dy={result.DeltaY}.");
            }
            Check(1.1f, .5f, .5f, 1.1f, .5f, .5f, .35f, true, -.1f);
            Check(1.2f, .5f, .5f, 1.1f, .45f, .45f, .35f, true, -.2f);
            Check(.1f, .6f, .2f, .1f, .6f, .2f, .35f, true, .3f); // avoid the inner hole
            Check(.1f, .6f, .2f, -.1f, .6f, .2f, .35f, false, 0f); // disjoint holes
            Check(1.5f, .5f, .5f, 1.5f, .5f, .5f, .35f, false, 0f);
            Check(.8f, .5f, .5f, .9f, .5f, .5f, .35f, true, 0f);
            var horizontal = AnkleReachProjection.Solve(new Vector3(1.1f, 0f, 0f), ankle, .5f, .5f,
                Vector3.up, ankle, .5f, .5f);
            Require(!horizontal.Feasible && horizontal.DeltaY == 0f, "Horizontal reach cannot be fixed vertically.");
            var nan = AnkleReachProjection.Solve(new Vector3(float.NaN, 0, 0), ankle, .5f, .5f,
                Vector3.up, ankle, .5f, .5f);
            var infinity = AnkleReachProjection.Solve(Vector3.up, ankle, .5f, .5f,
                Vector3.up, ankle, .5f, .5f, float.PositiveInfinity);
            var invalidLength = AnkleReachProjection.Solve(Vector3.up, ankle, 0f, .5f,
                Vector3.up, ankle, .5f, .5f);
            Require(!nan.Feasible && !infinity.Feasible && !invalidLength.Feasible, "Invalid values were accepted.");
            Debug.Log("[ankle-reach-projection] PASS symmetric, unequal, inner-hole, unsatisfiable, horizontal and finite-input checks.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
