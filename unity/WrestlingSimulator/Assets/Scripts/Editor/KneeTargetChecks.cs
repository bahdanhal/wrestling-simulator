using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wrestling.Core.Numerics;
using Wrestling.Core.Wrestlers;
using Wrestling.Data.Motion;
using Wrestling.Presentation;
using Pose = Wrestling.Presentation.Pose;

namespace Wrestling.Editor
{
    /// <summary>Checks the animated rig's knee target frames, independent of the physics lab.</summary>
    public static class KneeTargetChecks
    {
        private const float PositionTolerance = 0.0003f;
        private const float AxisTolerance = 0.1f;

        public static void RunFromCommandLine()
        {
            try
            {
                Run();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void Run()
        {
            var content = ContentLocator.LoadDirect();
            MotionTake[] takes = content.Motion.Takes.Where(t => t.Id.StartsWith("harmony4d.", StringComparison.Ordinal)).ToArray();
            if (takes.Length < 7) throw new InvalidOperationException($"Expected the expanded Harmony4D corpus, found {takes.Length}.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var library = AssetDatabase.LoadAssetAtPath<CharacterLibrary>(CharacterLibraryBuilder.LibraryPath);
            int samples = 0, impossible = 0, planeUnidentifiable = 0;
            float maxAxis = 0f, maxPosition = 0f, maxBeyond = 0f;
            foreach (Gender gender in new[] { Gender.Male, Gender.Female })
            {
                for (int profile = 0; profile < 2; profile++)
                {
                    var root = new GameObject("Knee target check");
                    try
                    {
                        var model = gender == Gender.Male ? library.Male : library.Female;
                        var wrestler = new Wrestler
                        {
                            Id = "knee-target", Name = new PersonName { Given = "Knee", Family = "Check" }, Country = "POL",
                            Gender = gender, Attributes = new AttributeSet(45),
                            Body = new BodyProfile { HeightCm = 180, WingspanCm = profile == 0 ? 170 : 190,
                                LegRatio = Fix64.Parse(profile == 0 ? "0.43" : "0.53"), NaturalWeightKg = Fix64.FromInt(75) },
                        };
                        WrestlerCharacter figure = root.AddComponent<WrestlerCharacter>();
                        figure.Build(model, new WrestlerLook(wrestler, model.Skins.Length), Color.red);
                        CharacterRig rig = figure.Rig;
                        Quaternion[] stance = new Quaternion[2];
                        Vector3[] childAxis = new Vector3[2];
                        float[] stanceBend = new float[2];
                        for (int s = 0; s < 2; s++)
                        {
                            string side = s == 0 ? "Left" : "Right";
                            Transform thigh = rig.Find(side + "UpLeg"), knee = rig.Find(side + "Leg");
                            stance[s] = Quaternion.Inverse(thigh.rotation) * knee.rotation;
                            childAxis[s] = Quaternion.Inverse(knee.rotation) * rig.KneeAxis(s == 0, false);
                            stanceBend[s] = -5f - figure.Physical.Link(side + "Leg").xDrive.lowerLimit;
                        }
                        Pose sweepPose = default;
                        foreach (MotionTake take in takes)
                        {
                            for (int mirror = 0; mirror < 2; mirror++)
                            {
                                var phase = new MotionPhase { Clip = "knee check", Take = take.Id, FromFrame = 0, ToFrame = take.FrameCount - 1, Executor = 0 };
                                var playback = new MotionPlayback();
                                var origin = new Pose { Pelvis = new Vector3(0.3f, 0f, -0.2f) };
                                var other = new Pose { Pelvis = new Vector3(1.3f, 0f, 0.3f) };
                                playback.Begin(take, phase, in origin, in other, 1.8f, 1.8f, mirror == 1);
                                for (int frame = 0; frame < take.FrameCount; frame++)
                                {
                                    var pair = playback.Sample((float)frame / (take.FrameCount - 1));
                                    foreach (Pose source in new[] { pair.Executor, pair.Opponent })
                                    {
                                        Pose fit = source;
                                        var reached = rig.RetargetRecordedLegs(ref fit);
                                        rig.Apply(in fit, 0f, 0f, true);
                                        Check(rig, figure, in fit, reached, stance, childAxis, stanceBend,
                                            ref impossible, ref maxBeyond, ref maxAxis, ref maxPosition, ref planeUnidentifiable,
                                            $"{gender} profile {profile}, {take.Id}, mirror {mirror}, frame {frame}, actor {(source.Pelvis == pair.Executor.Pelvis ? "executor" : "opponent")}");
                                        sweepPose = fit;
                                        samples++;
                                    }
                                }
                            }
                        }
                        Sweep(rig, figure, in sweepPose);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(root); }
                }
            }
            Debug.Log(FormattableString.Invariant($"[knee-targets] PASS {samples} actor-frames, {takes.Length} real takes, both mirrors, sexes and two morphologies; max axis mismatch {maxAxis:F4}°, max reached-ankle error {maxPosition:F6} m; targets beyond passive flexion envelope {impossible}, max beyond {maxBeyond:F2}°; bend-plane checks skipped near extension {planeUnidentifiable}. Both knees swept -4° to +4° through extension. Geometry checks are not human-motion validation."));
        }

        private static void Check(CharacterRig rig, WrestlerCharacter figure, in Pose fit, CharacterRig.RecordedLegsResult reached,
            Quaternion[] stance, Vector3[] childAxis, float[] stanceBend, ref int impossible, ref float maxBeyond,
            ref float maxAxis, ref float maxPosition, ref int planeUnidentifiable, string context)
        {
            for (int s = 0; s < 2; s++)
            {
                bool left = s == 0;
                string side = left ? "Left" : "Right";
                Transform thigh = rig.Find(side + "UpLeg"), knee = rig.Find(side + "Leg"), ankle = rig.Find(side + "Foot");
                Vector3 upper = knee.position - thigh.position, lower = ankle.position - knee.position;
                Vector3 plane = Vector3.Cross(upper, lower);
                ArticulationBody joint = figure.Physical.Link(side + "Leg");
                Vector3 a = (thigh.rotation * joint.parentAnchorRotation * Vector3.right).normalized;
                Vector3 b = (knee.rotation * joint.anchorRotation * Vector3.right).normalized;
                // At extension, a tiny position perturbation can rotate the inferred bend plane
                // arbitrarily. Still check the two actual joint frames at every sample; only assert
                // a plane from geometry when the limb angle makes that plane identifiable.
                float bend = Vector3.Angle(upper, lower);
                float axisError = Angle(a, b);
                float planeError = -1f;
                if (bend > 0.5f && bend < 179.5f)
                {
                    Vector3 normal = plane.normalized;
                    if (Vector3.Dot(a, normal) < 0f) normal = -normal;
                    planeError = Angle(a, normal);
                }
                else planeUnidentifiable++;
                if (axisError > AxisTolerance || planeError > AxisTolerance)
                    throw new InvalidOperationException($"{context}, {side}: parent/child axis {axisError:F4}°, bend-plane {planeError:F4}°, bend {bend:F4}°, segments {upper.magnitude:F4}/{lower.magnitude:F4} m.");
                maxAxis = Mathf.Max(maxAxis, axisError, planeError);
                Vector3 hipTarget = left ? fit.HipL : fit.HipR;
                Vector3 kneeTarget = left ? fit.KneeL : fit.KneeR;
                Vector3 ankleTarget = left ? reached.LeftReached : reached.RightReached;
                float error = Mathf.Max(Vector3.Distance(thigh.position, hipTarget), Vector3.Distance(knee.position, kneeTarget), Vector3.Distance(ankle.position, ankleTarget));
                if (error > PositionTolerance) throw new InvalidOperationException($"{side} knee alignment moved a retargeted endpoint by {error:F6} m.");
                maxPosition = Mathf.Max(maxPosition, error);
                Quaternion delta = Quaternion.Inverse(stance[s]) * Quaternion.Inverse(thigh.rotation) * knee.rotation;
                float angle = 2f * Mathf.Atan2(Vector3.Dot(new Vector3(delta.x, delta.y, delta.z), childAxis[s]), delta.w) * Mathf.Rad2Deg;
                angle = Mathf.DeltaAngle(0f, angle) + stanceBend[s];
                float beyond = Mathf.Max(0f, -5f - angle, angle - 150f);
                if (beyond > 0.01f) { impossible++; maxBeyond = Mathf.Max(maxBeyond, beyond); }
            }
        }

        // atan2 avoids the poor small-angle resolution of acos(dot).
        private static float Angle(Vector3 a, Vector3 b) => Mathf.Atan2(Vector3.Cross(a, b).magnitude, Vector3.Dot(a, b)) * Mathf.Rad2Deg;

        private static void Sweep(CharacterRig rig, WrestlerCharacter figure, in Pose start)
        {
            foreach (bool left in new[] { true, false })
            {
                Pose pose = start;
                Vector3 hip = left ? pose.HipL : pose.HipR;
                Vector3 knee = left ? pose.KneeL : pose.KneeR;
                Vector3 ankle = left ? pose.AnkleL : pose.AnkleR;
                Vector3 thigh = (knee - hip).normalized;
                float length = Vector3.Distance(knee, ankle);
                rig.Apply(in pose, 0f, 0f);
                string side = left ? "Left" : "Right";
                ArticulationBody joint = figure.Physical.Link(side + "Leg");
                Transform thighBone = rig.Find(side + "UpLeg"), kneeBone = rig.Find(side + "Leg");
                Vector3 axis = (thighBone.rotation * joint.parentAnchorRotation * Vector3.right).normalized;
                Vector3 previous = Vector3.zero;
                for (int i = -40; i <= 40; i++)
                {
                    Vector3 target = knee + (Quaternion.AngleAxis(i / 10f, axis) * thigh * length);
                    if (left) { pose.ToeL += target - pose.AnkleL; pose.AnkleL = target; }
                    else { pose.ToeR += target - pose.AnkleR; pose.AnkleR = target; }
                    rig.Apply(in pose, 0f, 0f);
                    Vector3 a = (thighBone.rotation * joint.parentAnchorRotation * Vector3.right).normalized;
                    Vector3 b = (kneeBone.rotation * joint.anchorRotation * Vector3.right).normalized;
                    float mismatch = Angle(a, b);
                    float jump = previous == Vector3.zero ? 0f : Angle(previous, a);
                    if (mismatch > AxisTolerance || jump > 5f)
                        throw new InvalidOperationException($"{side} knee at {i / 10f:F1}°: parent/child axes {mismatch:F4}°, step {jump:F4}°, limb bend {Vector3.Angle(kneeBone.position - thighBone.position, rig.Find(side + "Foot").position - kneeBone.position):F5}°.");
                    previous = a;
                }
            }
        }
    }
}
