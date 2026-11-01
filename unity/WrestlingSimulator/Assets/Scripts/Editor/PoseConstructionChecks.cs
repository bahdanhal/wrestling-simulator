using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Wrestling.Core.Match;
using Wrestling.Core.Numerics;
using Wrestling.Core.Wrestlers;
using Wrestling.Data.Animation;
using Wrestling.Presentation;
using Pose = Wrestling.Presentation.Pose;

namespace Wrestling.Editor
{
    /// <summary>Geometry regression checks for interpolated constructions; not a realism score.</summary>
    public static class PoseConstructionChecks
    {
        public static void RunFromCommandLine()
        {
            try
            {
                var content = ContentLocator.LoadDirect();
                var body = new BodyProportions(new BodyProfile { HeightCm = 175, WingspanCm = 180, LegRatio = Fix64.FromRatio(47, 100) });
                int poses = 0;
                int pathProblems = 0;
                foreach (var technique in content.Techniques.All)
                {
                    foreach (var phase in technique.Phases)
                    {
                        MoveClip clip = content.Animations.Get(phase.Animation);
                        bool executorBottom = clip.Frame == ClipFrame.Bottom && ClipFeasibility.IsBottom(clip, true);
                        for (int sample = 0; sample <= 240; sample++)
                        {
                            var keys = clip.Sample(sample / 240f);
                            Check(keys.Executor, body, executorBottom ? PoseRole.Bottom : PoseRole.Top, clip.Id);
                            Check(keys.Opponent, body, executorBottom ? PoseRole.Top : PoseRole.Bottom, clip.Id);
                            poses += 2;
                        }

                        IReadOnlyList<string> problems = ClipFeasibility.CheckSampledPath(clip, phase.MinimumTicks);
                        foreach (string problem in problems) Debug.LogWarning("[pose-path] " + problem);
                        pathProblems += problems.Count;
                    }
                }

                Debug.Log($"[pose-checks] {poses} sampled poses have fixed thigh, shin, upper-arm and forearm lengths; {pathProblems} inherited sampled-path free-fall diagnostics require reference review. These checks do not establish human realism.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void Check(ActorKey key, BodyProportions body, PoseRole role, string clip)
        {
            float heading = key.Heading * Mathf.Deg2Rad;
            var position = new Vector2(key.X, key.Z);
            var facing = new Vector2(Mathf.Cos(heading), Mathf.Sin(heading));
            var left = new Vector2(-facing.y, facing.x) * 0.2f;
            var frame = new WrestlerFrame
            {
                Position = position, Facing = facing, LeftFoot = position + left, RightFoot = position - left,
                Level = key.Level, Posture = key.Posture, Balance = 1f, Ground = key.Ground,
                GroundFrom = key.GroundFrom, GroundTo = key.GroundTo, GroundBlend = key.GroundBlend,
                GroundFromLying = key.GroundFromLying, GroundToLying = key.GroundToLying,
                TorsoRotation = key.Roll, RollSide = key.RollSide,
            };
            Pose pose = PoseSolver.SolveBody(frame, body, role);
            Pose other = default;
            PoseSolver.SolveArms(ref pose, body, role, frame, in other, Array.Empty<Grip>());
            Length(pose.HipL, pose.KneeL, body.Thigh, clip + " left thigh");
            Length(pose.KneeL, pose.AnkleL, body.Shin, clip + " left shin");
            Length(pose.HipR, pose.KneeR, body.Thigh, clip + " right thigh");
            Length(pose.KneeR, pose.AnkleR, body.Shin, clip + " right shin");
            Length(pose.ShoulderL, pose.ElbowL, body.UpperArm, clip + " left upper arm");
            Length(pose.ElbowL, pose.HandL, body.Forearm, clip + " left forearm");
            Length(pose.ShoulderR, pose.ElbowR, body.UpperArm, clip + " right upper arm");
            Length(pose.ElbowR, pose.HandR, body.Forearm, clip + " right forearm");
            if (pose.ElbowL.y < ClipKinematics.ForearmElbowHeight - 0.0001f || pose.ElbowR.y < ClipKinematics.ForearmElbowHeight - 0.0001f
                || pose.HandL.y < ClipKinematics.ForearmWristHeight - 0.0001f || pose.HandR.y < ClipKinematics.ForearmWristHeight - 0.0001f)
            {
                throw new InvalidOperationException(clip + ": solved arm target below mat plane.");
            }
        }

        private static void Length(Vector3 a, Vector3 b, float expected, string path)
        {
            float actual = Vector3.Distance(a, b);
            if (float.IsNaN(actual) || float.IsInfinity(actual) || Mathf.Abs(actual - expected) > 0.0001f)
            {
                throw new InvalidOperationException($"{path}: {actual} m, expected {expected} m.");
            }
        }
    }
}
