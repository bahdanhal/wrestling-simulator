using System;
using System.IO;
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
    /// <summary>Checks actual rig ankles after applying retargeted source samples, independently of physics or foot-contact assumptions.</summary>
    public static class RecordedRetargetChecks
    {
        private const float ActualTolerance = 0.00025f;
        private static int _unreachableWrists;
        private static float _maxWristActual;
        private static float _maxWristResidual;

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
            _unreachableWrists = 0;
            _maxWristActual = _maxWristResidual = 0f;
            var content = ContentLocator.LoadDirect();
            MotionTake[] takes = content.Motion.Takes.Where(t => t.Id.StartsWith("harmony4d.", StringComparison.Ordinal)).ToArray();
            if (takes.Length == 0)
            {
                throw new InvalidOperationException("No real Harmony4D takes are available for recorded retarget checks.");
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CharacterLibrary library = AssetDatabase.LoadAssetAtPath<CharacterLibrary>(CharacterLibraryBuilder.LibraryPath);
            int samples = 0;
            int unreachable = 0;
            float maxActual = 0f;
            float maxResidual = 0f;
            foreach (Gender gender in new[] { Gender.Male, Gender.Female })
            {
                for (int profile = 0; profile < 2; profile++)
                {
                    var root = new GameObject("Retarget check " + gender + profile);
                    try
                    {
                        var model = gender == Gender.Male ? library.Male : library.Female;
                        var wrestler = new Wrestler
                        {
                            Id = "retarget-check", Name = new PersonName { Given = "Retarget", Family = "Check" }, Country = "POL",
                            Gender = gender, Attributes = new AttributeSet(45),
                            Body = new BodyProfile
                            {
                                HeightCm = 180, WingspanCm = profile == 0 ? 170 : 190,
                                LegRatio = Fix64.Parse(profile == 0 ? "0.43" : "0.53"),
                                NaturalWeightKg = Fix64.FromInt(75),
                            },
                        };
                        WrestlerCharacter figure = root.AddComponent<WrestlerCharacter>();
                        figure.Build(model, new WrestlerLook(wrestler, model.Skins.Length), Color.red);
                        foreach (MotionTake take in takes)
                        {
                            for (int mirrored = 0; mirrored < 2; mirrored++)
                            {
                                var phase = new MotionPhase { Clip = "check only", Take = take.Id, FromFrame = 0, ToFrame = take.FrameCount - 1, Executor = 0 };
                                var playback = new MotionPlayback();
                                var origin = new Pose { Pelvis = new Vector3(0.3f, 0f, -0.2f) };
                                var opponent = new Pose { Pelvis = new Vector3(1.3f, 0f, 0.3f) };
                                playback.Begin(take, phase, in origin, in opponent, 1.8f, 1.8f, mirrored == 1);
                                int localUnreachable = 0;
                                float localMaxActual = 0f;
                                float localMaxResidual = 0f;
                                for (int frame = 0; frame < take.FrameCount; frame++)
                                {
                                    var pair = playback.Sample((float)frame / (take.FrameCount - 1));
                                    foreach (Pose source in new[] { pair.Executor, pair.Opponent })
                                    {
                                        Check(figure.Rig, in source, ref localUnreachable, ref localMaxActual, ref localMaxResidual);
                                        samples++;
                                    }
                                }

                                unreachable += localUnreachable;
                                maxActual = Mathf.Max(maxActual, localMaxActual);
                                maxResidual = Mathf.Max(maxResidual, localMaxResidual);
                                Debug.Log(FormattableString.Invariant($"[retarget] {gender} profile {profile}, {take.Id}, mirrored {mirrored}: source samples {take.FrameCount * 2}, unreachable ankle targets {localUnreachable}, max actual-to-reached {localMaxActual:F6} m, max explicit residual {localMaxResidual:F4} m"));
                            }
                        }

                        // Deliberately impossible targets exercise both extension and minimum-reach
                        // limits. They must be reported while preserving pelvis and segment lengths.
                        Pose impossible = figure.Rig.Bind;
                        impossible.AnkleL = impossible.Pelvis + (Vector3.up * 5f);
                        impossible.HandL = impossible.Pelvis + (Vector3.up * 5f);
                        Pose calibrated = impossible;
                        var initial = figure.Rig.RetargetRecordedLegs(ref calibrated);
                        impossible.PelvisUp = calibrated.PelvisUp;
                        impossible.PelvisNormal = calibrated.PelvisNormal;
                        impossible.AnkleR = calibrated.HipR;
                        int impossibleCount = 0;
                        float impossibleActual = 0f, impossibleResidual = 0f;
                        Check(figure.Rig, in impossible, ref impossibleCount, ref impossibleActual, ref impossibleResidual);
                        if (initial.LeftResidual < 1f || impossibleCount == 0)
                        {
                            throw new InvalidOperationException("Deliberately unreachable ankle targets were not exposed.");
                        }
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(root);
                    }
                }
            }

            Debug.Log(FormattableString.Invariant($"[retarget] PASS {samples} source actor-frames across {takes.Length} real takes, two morphologies, both sexes and both mirror states; actual-to-reached max {maxActual:F6} m. Unreachable ankle targets {unreachable}, explicit residual max {maxResidual:F4} m. These are trajectory/rig correctness checks, not sole-contact or realism acceptance."));
            Debug.Log(FormattableString.Invariant($"[retarget-arms] PASS actual-to-reached max {_maxWristActual:F6} m; unreachable wrist targets {_unreachableWrists}, explicit residual max {_maxWristResidual:F4} m (including deliberately impossible targets). Wrist preservation is not palm-contact preservation."));
        }

        private static void Check(CharacterRig rig, in Pose source, ref int unreachable, ref float maxActual, ref float maxResidual)
        {
            Pose fit = source;
            Vector3 pelvis = source.Pelvis;
            Vector3 pelvisAxis = source.PelvisAxis;
            Vector3 toeL = source.ToeL - source.AnkleL;
            Vector3 toeR = source.ToeR - source.AnkleR;
            CharacterRig.RecordedLegsResult result = rig.RetargetRecordedLegs(ref fit);
            Require(Vector3.Distance(pelvis, fit.Pelvis) < 1e-6f, "Retarget moved the source pelvis trajectory.");
            Require(Vector3.Distance(pelvisAxis.normalized, fit.PelvisAxis) < 1e-5f, "Retarget changed the source pelvis construction frame.");
            Require(Vector3.Distance(toeL, fit.ToeL - fit.AnkleL) < 1e-5f && Vector3.Distance(toeR, fit.ToeR - fit.AnkleR) < 1e-5f, "Retarget changed the supplied inferred toe vectors.");
            rig.Apply(in fit, 0f, 0f);
            Vector3 neck = rig.Find("Neck").position;
            Vector3 pelvisBone = rig.Find("Hips").position;
            rig.Apply(in fit, 0f, 0f, true);
            Require(Vector3.Distance(neck, rig.Find("Neck").position) < 1e-6f
                && Vector3.Distance(pelvisBone, rig.Find("Hips").position) < 1e-6f, "Wrist retarget moved the trunk.");
            CharacterRig.RecordedArmsResult arms = rig.RecordedArms.Value;
            CheckArm(rig, true, arms.LeftTarget, arms.LeftReached, arms.LeftResidual);
            CheckArm(rig, false, arms.RightTarget, arms.RightReached, arms.RightResidual);
            CheckLeg(rig, true, fit.HipL, fit.AnkleL, result.LeftTarget, result.LeftResidual, ref unreachable, ref maxActual, ref maxResidual);
            CheckLeg(rig, false, fit.HipR, fit.AnkleR, result.RightTarget, result.RightResidual, ref unreachable, ref maxActual, ref maxResidual);
        }

        private static void CheckArm(CharacterRig rig, bool left, Vector3 target, Vector3 reached, float residual)
        {
            string side = left ? "Left" : "Right";
            Vector3 shoulder = rig.Find(side + "Arm").position;
            Vector3 elbow = rig.Find(side + "ForeArm").position;
            Vector3 wrist = rig.Find(side + "Hand").position;
            Finite(shoulder); Finite(elbow); Finite(wrist);
            float upper = Vector3.Distance(left ? rig.Bind.ShoulderL : rig.Bind.ShoulderR, left ? rig.Bind.ElbowL : rig.Bind.ElbowR);
            float forearm = Vector3.Distance(left ? rig.Bind.ElbowL : rig.Bind.ElbowR, left ? rig.Bind.HandL : rig.Bind.HandR);
            Require(Mathf.Abs(Vector3.Distance(shoulder, elbow) - upper) < ActualTolerance
                && Mathf.Abs(Vector3.Distance(elbow, wrist) - forearm) < ActualTolerance, "Retarget stretched arm segments.");
            float actualError = Vector3.Distance(wrist, reached);
            Require(actualError < ActualTolerance, $"Actual {side} wrist missed its reachable endpoint by {actualError:F6} m.");
            float distance = Vector3.Distance(shoulder, target);
            float expectedResidual = Mathf.Max(0f, distance - upper - forearm, Mathf.Abs(upper - forearm) - distance);
            Require(Mathf.Abs(residual - expectedResidual) < ActualTolerance, "Wrist residual did not match reachable interval.");
            if (residual <= 1e-5f)
                Require(Vector3.Distance(wrist, target) < ActualTolerance, "Reachable source wrist was not preserved.");
            else _unreachableWrists++;
            _maxWristActual = Mathf.Max(_maxWristActual, actualError);
            _maxWristResidual = Mathf.Max(_maxWristResidual, residual);
        }

        private static void CheckLeg(CharacterRig rig, bool left, Vector3 hip, Vector3 reached, Vector3 target, float residual, ref int unreachable, ref float maxActual, ref float maxResidual)
        {
            string side = left ? "Left" : "Right";
            Vector3 actualHip = rig.Find(side + "UpLeg").position;
            Vector3 actualKnee = rig.Find(side + "Leg").position;
            Vector3 actualAnkle = rig.Find(side + "Foot").position;
            Finite(actualHip); Finite(actualKnee); Finite(actualAnkle);
            float thigh = Vector3.Distance(left ? rig.Bind.HipL : rig.Bind.HipR, left ? rig.Bind.KneeL : rig.Bind.KneeR);
            float shin = Vector3.Distance(left ? rig.Bind.KneeL : rig.Bind.KneeR, left ? rig.Bind.AnkleL : rig.Bind.AnkleR);
            Require(Vector3.Distance(hip, actualHip) < ActualTolerance, "Calibrated hip did not match actual rig.");
            Require(Mathf.Abs(Vector3.Distance(actualHip, actualKnee) - thigh) < ActualTolerance, "Thigh length changed.");
            Require(Mathf.Abs(Vector3.Distance(actualKnee, actualAnkle) - shin) < ActualTolerance, "Shin length changed.");
            float actualError = Vector3.Distance(actualAnkle, reached);
            Require(actualError < ActualTolerance, $"Actual {side} ankle missed solved target by {actualError:F6} m.");
            float distance = Vector3.Distance(hip, target);
            float expectedResidual = Mathf.Max(0f, distance - thigh - shin, Mathf.Abs(thigh - shin) - distance);
            Require(Mathf.Abs(residual - expectedResidual) < ActualTolerance, "Unreachable residual did not match the fixed-length reach interval.");
            if (residual <= 1e-5f)
            {
                Require(Vector3.Distance(actualAnkle, target) < ActualTolerance, "Reachable source ankle trajectory was not preserved.");
            }
            else
            {
                unreachable++;
            }

            maxActual = Mathf.Max(maxActual, actualError);
            maxResidual = Mathf.Max(maxResidual, residual);
        }

        private static void Finite(Vector3 value) => Require(!float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z)
            && !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z), "Retarget produced a non-finite joint.");

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
