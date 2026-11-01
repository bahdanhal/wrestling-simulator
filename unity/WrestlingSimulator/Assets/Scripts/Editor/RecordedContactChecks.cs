using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wrestling.Core.Numerics;
using Wrestling.Core.Wrestlers;
using Wrestling.Data.Motion;
using Wrestling.Presentation;
using Pose = Wrestling.Presentation.Pose;
using ContactPoint = Wrestling.Data.Motion.ContactPoint;

namespace Wrestling.Editor
{
    /// <summary>Software-authored contact fixtures; these are not observations of human movement.</summary>
    public static class RecordedContactChecks
    {
        public static void RunFromCommandLine()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        private static void CheckSurface(WrestlerCharacter figure, ContactSurface surface, Vector3 proximal, Vector3 distal,
            Vector3 anterior, string radiusSegment, ContactVector anchor)
        {
            Vector3 axisY = (distal - proximal).normalized;
            Vector3 axisZ = Vector3.ProjectOnPlane(anterior, axisY).normalized;
            Vector3 axisX = Vector3.Cross(axisY, axisZ).normalized;
            axisZ = Vector3.Cross(axisX, axisY).normalized;
            float radius = figure.Physical.Radius(radiusSegment);
            Vector3 expected = proximal + (axisY * (anchor.Y * Vector3.Distance(proximal, distal))) +
                (axisX * (anchor.X * radius)) + (axisZ * (anchor.Z * radius));
            if (!RecordedContactConstraints.SurfacePoint(figure, surface, ContactSide.Centre, anchor, false, out Vector3 actual) ||
                Vector3.Distance(expected, actual) > .0001f)
                throw new InvalidOperationException(surface + " canonical body proxy is incorrect.");
        }

        private static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var library = AssetDatabase.LoadAssetAtPath<CharacterLibrary>(CharacterLibraryBuilder.LibraryPath);
            foreach (Gender gender in new[] { Gender.Male, Gender.Female })
            foreach (int wingspan in new[] { 170, 190 })
            {
                var redRoot = new GameObject("Contact fixture red");
                var blueRoot = new GameObject("Contact fixture blue");
                try
                {
                    var model = gender == Gender.Male ? library.Male : library.Female;
                    var wrestler = new Wrestler { Id = "contact-fixture", Name = new PersonName { Given = "Contact", Family = "Fixture" }, Country = "POL", Gender = gender, Attributes = new AttributeSet(45), Body = new BodyProfile { HeightCm = 180, WingspanCm = wingspan, LegRatio = Fix64.Parse("0.48"), NaturalWeightKg = Fix64.FromInt(75) } };
                    var red = redRoot.AddComponent<WrestlerCharacter>();
                    var blue = blueRoot.AddComponent<WrestlerCharacter>();
                    red.Build(model, new WrestlerLook(wrestler, model.Skins.Length), Color.red);
                    blue.Build(model, new WrestlerLook(wrestler, model.Skins.Length), Color.blue);
                    var anchor = new ContactVector { X = .7f, Y = .5f, Z = .3f };
                    if (!RecordedContactConstraints.SurfacePoint(blue, ContactSurface.Forearm, ContactSide.Left, anchor, false, out Vector3 normal) ||
                        !RecordedContactConstraints.SurfacePoint(blue, ContactSurface.Forearm, ContactSide.Left, anchor, true, out Vector3 reflected))
                        throw new InvalidOperationException("Canonical fixture surface unavailable.");
                    float radius = blue.Physical.Radius("LeftForeArm");
                    if (Mathf.Abs(Vector3.Distance(normal, reflected) - (1.4f * radius)) > .0001f)
                        throw new InvalidOperationException("Canonical reflection must change X exactly once.");
                    Pose pose = blue.Rig.MeasurePose();
                    Vector3 headBase = blue.Rig.Find("Head").position;
                    CheckSurface(blue, ContactSurface.Head, headBase, pose.Head + (pose.Head - headBase), pose.HeadNormal, "Head", anchor);
                    CheckSurface(blue, ContactSurface.Neck, blue.Rig.Find("Neck").position, headBase, blue.Rig.SegmentRotation(Bone.Neck) * Vector3.forward, "Head", anchor);
                    CheckSurface(blue, ContactSurface.Pelvis, (pose.HipL + pose.HipR) * .5f, blue.Rig.Find("Spine").position,
                        blue.Rig.SegmentRotation(Bone.Hips) * Vector3.forward, "Hips", anchor);

                    Vector3 wrist = red.Rig.Find("LeftHand").position;
                    var contact = new MotionContact { SourceActor = 0, SourceJoint = MotionJoint.HandL, SourceSide = ContactSide.Left, Point = ContactPoint.Wrist,
                        TargetKind = ContactTargetKind.Mat, TargetSurface = ContactSurface.Mat, TargetSide = ContactSide.Centre, Anchor = new ContactVector { X = 2, Y = 0, Z = 3 },
                        FromFrame = 0, ToFrame = 2, Origin = ContactOrigin.Authored, Evidence = "Software fixture", Confidence = 1 };
                    var set = new MotionContactSet { Contacts = new[] { contact } };
                    // An arbitrary pair-facing transform, including translation, catches an extra X reflection.
                    Vector3 Transform(ContactVector value) => wrist + new Vector3((value.Z - 3) * .01f, 0, (value.X - 2) * .01f);
                    var result = RecordedContactConstraints.Apply(set.Sample(1), red, blue, Transform, true);
                    if (result.Applied != 1 || result.Unavailable != 0 || Vector3.Distance(red.Rig.Find("LeftHand").position, wrist) > .002f)
                        throw new InvalidOperationException("Mat fixture did not preserve playback-transformed target.");
                }
                finally { UnityEngine.Object.DestroyImmediate(redRoot); UnityEngine.Object.DestroyImmediate(blueRoot); }
            }
            Debug.Log("[recorded-contact] PASS authored software fixtures for mirror, mat placement, both sexes and two morphologies; not human-motion validation.");
        }
    }
}
