using System;
using System.Collections.Generic;
using UnityEngine;
using Wrestling.Core.Match;
using Wrestling.Core.Rules;
using Wrestling.Data.Motion;
using ContactPoint = Wrestling.Data.Motion.ContactPoint;

namespace Wrestling.Presentation
{
    /// <summary>Presentation contact targets before physics and shown-bone correction afterwards;
    /// never changes the authoritative bout state.</summary>
    public static class RecordedContactConstraints
    {
        public sealed class Result
        {
            public int Applied { get; internal set; }
            public int GripPriority { get; internal set; }
            public int Unavailable { get; internal set; }
            public int ZeroWeight { get; internal set; }
            public float MaxResidual { get; internal set; }
            public float MaxTargetGap { get; internal set; }
        }

        public static Result Apply(IReadOnlyList<MotionContactSample> samples, WrestlerCharacter red, WrestlerCharacter blue,
            Func<ContactVector, Vector3> matTransform, bool mirrored, BoutEngine engine = null)
        {
            if (samples == null || red == null || blue == null || matTransform == null) throw new ArgumentNullException();
            var figures = new[] { red, blue };
            var result = new Result();
            foreach (MotionContactSample sample in samples)
            {
                if (sample.Weight <= 0f) { result.ZeroWeight++; continue; }
                if (sample.SourceActor < 0 || sample.SourceActor > 1 || sample.Contact == null || sample.Anchor == null)
                { result.Unavailable++; continue; }
                WrestlerCharacter source = figures[sample.SourceActor];
                bool left = sample.SourceSide == ContactSide.Left;
                bool hand = sample.Contact.Point != ContactPoint.Sole;
                if (hand && engine != null && HandGrips.ForHand(engine, (Corner)sample.SourceActor, left ? GripSide.Left : GripSide.Right) != null)
                { result.GripPriority++; continue; }
                Vector3 target;
                if (sample.Contact.TargetKind == ContactTargetKind.Mat) target = matTransform(sample.Anchor);
                else if (!sample.TargetActor.HasValue || sample.TargetActor.Value < 0 || sample.TargetActor.Value > 1 ||
                    !SurfacePoint(figures[sample.TargetActor.Value], sample.Contact.TargetSurface, sample.TargetSide, sample.Anchor, mirrored, out target))
                { result.Unavailable++; continue; }
                if (!Finite(target)) { result.Unavailable++; continue; }
                string side = left ? "Left" : "Right";
                Transform bone = source.Rig.Find(side + (hand ? "Hand" : "Foot"));
                if (bone == null) { result.Unavailable++; continue; }
                Vector3 offset = Vector3.zero;
                if (sample.Contact.Point == ContactPoint.Palm) offset = bone.rotation * source.Physical.PalmAnchor(side + "Hand");
                else if (!hand) offset = bone.rotation * source.Physical.SoleAnchor(side + "Foot");
                Vector3 currentPoint = bone.position + offset;
                Vector3 weightedTarget = Vector3.Lerp(currentPoint, target, Mathf.Clamp01(sample.Weight)) - offset;
                var reach = hand ? source.Rig.ReachWrist(left, weightedTarget) : source.Rig.ReachAnkle(left, weightedTarget);
                result.MaxResidual = Mathf.Max(result.MaxResidual, reach.Residual);
                result.MaxTargetGap = Mathf.Max(result.MaxTargetGap, Vector3.Distance(bone.position + offset, target));
                result.Applied++;
            }
            return result;
        }

        /// <summary>Reconstructs the canonical dimensionless surface point on current shown bones.</summary>
        public static bool SurfacePoint(WrestlerCharacter figure, ContactSurface surface, ContactSide side,
            ContactVector anchor, bool mirrored, out Vector3 point)
        {
            point = default;
            if (figure == null || anchor == null) return false;
            Pose pose = figure.Rig.MeasurePose();
            bool left = side == ContactSide.Left;
            string prefix = left ? "Left" : "Right";
            Vector3 a, b, anterior;
            string segment;
            switch (surface)
            {
                case ContactSurface.Torso: a = pose.Pelvis; b = pose.Neck; anterior = pose.ChestNormal; segment = "Spine"; break;
                case ContactSurface.Pelvis:
                    a = (pose.HipL + pose.HipR) * .5f; b = figure.Rig.Find("Spine").position;
                    anterior = figure.Rig.SegmentRotation(Bone.Hips) * Vector3.forward; segment = "Hips"; break;
                case ContactSurface.Neck:
                    a = figure.Rig.Find("Neck").position; b = figure.Rig.Find("Head").position;
                    anterior = figure.Rig.SegmentRotation(Bone.Neck) * Vector3.forward; segment = "Head"; break;
                case ContactSurface.Head:
                    a = figure.Rig.Find("Head").position; b = pose.Head + (pose.Head - a);
                    anterior = pose.HeadNormal; segment = "Head"; break;
                case ContactSurface.UpperArm: a = left ? pose.ShoulderL : pose.ShoulderR; b = left ? pose.ElbowL : pose.ElbowR; anterior = pose.ChestNormal; segment = prefix + "Arm"; break;
                case ContactSurface.Forearm: a = left ? pose.ElbowL : pose.ElbowR; b = left ? pose.HandL : pose.HandR; anterior = pose.ChestNormal; segment = prefix + "ForeArm"; break;
                case ContactSurface.Hand:
                    a = left ? pose.HandL : pose.HandR;
                    Transform hand = figure.Rig.Find(prefix + "Hand");
                    if (hand == null) return false;
                    b = hand.position + (hand.rotation * figure.Physical.PalmAnchor(prefix + "Hand"));
                    anterior = pose.ChestNormal; segment = prefix + "Hand"; break;
                case ContactSurface.Thigh: a = left ? pose.HipL : pose.HipR; b = left ? pose.KneeL : pose.KneeR; anterior = pose.PelvisNormal; segment = prefix + "UpLeg"; break;
                case ContactSurface.LowerLeg: a = left ? pose.KneeL : pose.KneeR; b = left ? pose.AnkleL : pose.AnkleR; anterior = pose.PelvisNormal; segment = prefix + "Leg"; break;
                case ContactSurface.Foot: a = left ? pose.AnkleL : pose.AnkleR; b = left ? pose.ToeL : pose.ToeR; anterior = pose.PelvisNormal; segment = prefix + "Foot"; break;
                default: return false;
            }
            bool midline = surface == ContactSurface.Torso || surface == ContactSurface.Pelvis || surface == ContactSurface.Neck || surface == ContactSurface.Head;
            if (midline ? side != ContactSide.Centre : side == ContactSide.Centre) return false;
            Vector3 axis = b - a;
            float length = axis.magnitude;
            if (!Finite(a) || !Finite(b) || !Finite(anterior) || length < 1e-5f) return false;
            Vector3 y = axis / length;
            Vector3 z = Vector3.ProjectOnPlane(anterior, y);
            if (z.sqrMagnitude < 1e-10f) return false;
            z.Normalize();
            Vector3 x = Vector3.Cross(y, z).normalized;
            z = Vector3.Cross(x, y).normalized;
            float radius = figure.Physical.Radius(segment);
            if (float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0f) return false;
            float localX = mirrored ? -anchor.X : anchor.X;
            point = a + (x * (localX * radius)) + (y * (anchor.Y * length)) + (z * (anchor.Z * radius));
            return Finite(point);
        }
        private static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
    }
}
