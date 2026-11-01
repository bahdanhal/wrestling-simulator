using System;
using System.Collections.Generic;

namespace Wrestling.Data.Motion
{
    public enum ContactSide { Centre, Left, Right }
    public enum ContactPoint { Wrist, Palm, Sole }
    public enum ContactTargetKind { Mat, Actor }
    public enum ContactSurface { Mat, Head, Neck, Torso, Pelvis, UpperArm, Forearm, Hand, Thigh, LowerLeg, Foot }
    public enum ContactOrigin { Manual, Inferred, Authored }

    /// <summary>Mat: take-world metres. Actor: dimensionless canonical surface coordinates; see docs/motion-contacts.md.</summary>
    public sealed class ContactVector
    {
        public float X { get; init; }
        public float Y { get; init; }
        public float Z { get; init; }
        internal bool Finite => IsFinite(X) && IsFinite(Y) && IsFinite(Z);
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>A presentation constraint, with evidence or an explicit authored origin; it does not determine match state.</summary>
    public sealed class MotionContact
    {
        public int SourceActor { get; init; }
        public MotionJoint SourceJoint { get; init; }
        public ContactSide SourceSide { get; init; }
        public ContactPoint Point { get; init; }
        public ContactTargetKind TargetKind { get; init; }
        /// <summary>Other actor index, or null for the mat.</summary>
        public int? TargetActor { get; init; }
        public ContactSurface TargetSurface { get; init; }
        public ContactSide TargetSide { get; init; }
        public ContactVector Anchor { get; init; } = new ContactVector();
        /// <summary>Inclusive interval in the source take, with fades measured in frames.</summary>
        public int FromFrame { get; init; }
        public int ToFrame { get; init; }
        public float ActivationFrames { get; init; }
        public float ReleaseFrames { get; init; }
        public ContactOrigin Origin { get; init; }
        /// <summary>Evidence and uncertainty, or the rationale for an authored constraint; not a claim of verification.</summary>
        public string Evidence { get; init; } = string.Empty;
        public float Confidence { get; init; }
    }

    public sealed class MotionContactSample
    {
        public MotionContact Contact { get; internal set; } = null!;
        public int SourceActor { get; internal set; }
        public MotionJoint SourceJoint { get; internal set; }
        public ContactSide SourceSide { get; internal set; }
        public int? TargetActor { get; internal set; }
        public ContactSide TargetSide { get; internal set; }
        public ContactVector Anchor { get; internal set; } = null!;
        public float Weight { get; internal set; }
    }

    /// <summary>Optional, separately sourced contact annotations for exactly one recorded take.</summary>
    public sealed class MotionContactSet
    {
        public int SchemaVersion { get; init; }
        public string Take { get; init; } = string.Empty;
        public int FrameCount { get; init; }
        public float FramesPerSecond { get; init; }
        public IReadOnlyList<MotionContact> Contacts { get; init; } = Array.Empty<MotionContact>();

        public IReadOnlyList<string> Validate(MotionTake take)
        {
            if (take == null) throw new ArgumentNullException(nameof(take));
            var errors = new List<string>();
            if (SchemaVersion != 1) errors.Add("Unsupported contact schema version.");
            if (Take != take.Id || FrameCount != take.FrameCount || FramesPerSecond != take.FramesPerSecond)
                errors.Add("Contact take id, frame count and frame rate must match the take exactly.");
            if (Contacts == null) { errors.Add("Contacts must not be null."); return errors; }
            for (int i = 0; i < Contacts.Count; i++)
            {
                MotionContact c = Contacts[i];
                if (c == null) { errors.Add($"Contact {i} is null."); continue; }
                string p = $"Contact {i}";
                if (c.SourceActor < 0 || c.SourceActor > 1 || (int)c.SourceJoint < 0 || (int)c.SourceJoint >= MotionTake.JointCount || !Enum.IsDefined(typeof(ContactSide), c.SourceSide) || !Enum.IsDefined(typeof(ContactPoint), c.Point)) errors.Add($"{p}: invalid source.");
                bool left = c.SourceJoint == MotionJoint.HandL || c.SourceJoint == MotionJoint.AnkleL;
                bool right = c.SourceJoint == MotionJoint.HandR || c.SourceJoint == MotionJoint.AnkleR;
                bool hand = c.SourceJoint == MotionJoint.HandL || c.SourceJoint == MotionJoint.HandR;
                bool foot = c.SourceJoint == MotionJoint.AnkleL || c.SourceJoint == MotionJoint.AnkleR;
                if ((!left && !right) || c.SourceSide != (left ? ContactSide.Left : ContactSide.Right) || (c.Point == ContactPoint.Sole ? !foot : !hand)) errors.Add($"{p}: source point, joint and side disagree.");
                if (!Enum.IsDefined(typeof(ContactTargetKind), c.TargetKind) || !Enum.IsDefined(typeof(ContactSurface), c.TargetSurface) || !Enum.IsDefined(typeof(ContactSide), c.TargetSide) ||
                    (c.TargetKind == ContactTargetKind.Mat ? c.TargetActor != null || c.TargetSurface != ContactSurface.Mat || c.TargetSide != ContactSide.Centre : c.TargetActor == null || c.TargetActor < 0 || c.TargetActor > 1 || c.TargetActor == c.SourceActor || c.TargetSurface == ContactSurface.Mat)) errors.Add($"{p}: invalid target.");
                bool pairedSurface = c.TargetSurface == ContactSurface.UpperArm || c.TargetSurface == ContactSurface.Forearm || c.TargetSurface == ContactSurface.Hand || c.TargetSurface == ContactSurface.Thigh || c.TargetSurface == ContactSurface.LowerLeg || c.TargetSurface == ContactSurface.Foot;
                if (c.TargetKind == ContactTargetKind.Actor && (pairedSurface ? c.TargetSide == ContactSide.Centre : c.TargetSide != ContactSide.Centre)) errors.Add($"{p}: target side does not match the anatomical surface.");
                if (c.Anchor == null || !c.Anchor.Finite) errors.Add($"{p}: anchor must be finite.");
                if (c.FromFrame < 0 || c.ToFrame >= take.FrameCount || c.FromFrame >= c.ToFrame) errors.Add($"{p}: interval is outside the take or empty.");
                float duration = c.ToFrame - c.FromFrame;
                if (!Finite(c.ActivationFrames) || !Finite(c.ReleaseFrames) || c.ActivationFrames < 0 || c.ReleaseFrames < 0 || c.ActivationFrames + c.ReleaseFrames > duration) errors.Add($"{p}: invalid activation/release durations.");
                if (!Enum.IsDefined(typeof(ContactOrigin), c.Origin) || string.IsNullOrWhiteSpace(c.Evidence) || !Finite(c.Confidence) || c.Confidence < 0 || c.Confidence > 1) errors.Add($"{p}: origin, evidence and confidence are required.");
                for (int j = 0; j < i; j++)
                {
                    MotionContact prior = Contacts[j];
                    if (prior != null && prior.SourceActor == c.SourceActor && prior.SourceJoint == c.SourceJoint && prior.FromFrame <= c.ToFrame && c.FromFrame <= prior.ToFrame && !(prior.ToFrame == c.FromFrame && (prior.ReleaseFrames > 0 || c.ActivationFrames > 0)) && !(c.ToFrame == prior.FromFrame && (c.ReleaseFrames > 0 || prior.ActivationFrames > 0)))
                        errors.Add($"{p}: overlaps contact {j} on the same source channel.");
                }
            }
            return errors;
        }

        /// <summary>Samples take-space frames. Actor reversal maps recorded actor 0 to 1 and vice versa. Mirroring swaps metadata only; anchors remain in their original coordinate space for the playback transform.</summary>
        public IReadOnlyList<MotionContactSample> Sample(float frame, bool mirrored = false, bool reverseActors = false)
        {
            if (!Finite(frame)) throw new ArgumentOutOfRangeException(nameof(frame));
            var result = new List<MotionContactSample>();
            foreach (MotionContact c in Contacts)
            {
                if (frame < c.FromFrame || frame > c.ToFrame) continue;
                float weight = 1f;
                if (c.ActivationFrames > 0) weight = Math.Min(weight, (frame - c.FromFrame) / c.ActivationFrames);
                if (c.ReleaseFrames > 0) weight = Math.Min(weight, (c.ToFrame - frame) / c.ReleaseFrames);
                result.Add(new MotionContactSample { Contact = c, SourceActor = reverseActors ? 1 - c.SourceActor : c.SourceActor,
                    SourceJoint = mirrored ? MirrorJoint(c.SourceJoint) : c.SourceJoint, SourceSide = mirrored ? MirrorSide(c.SourceSide) : c.SourceSide,
                    TargetActor = c.TargetActor.HasValue && reverseActors ? 1 - c.TargetActor.Value : c.TargetActor,
                    TargetSide = mirrored ? MirrorSide(c.TargetSide) : c.TargetSide, Anchor = c.Anchor, Weight = weight });
            }
            return result;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static ContactSide MirrorSide(ContactSide side) => side == ContactSide.Left ? ContactSide.Right : side == ContactSide.Right ? ContactSide.Left : side;
        private static MotionJoint MirrorJoint(MotionJoint joint) => joint switch { MotionJoint.HandL => MotionJoint.HandR, MotionJoint.HandR => MotionJoint.HandL, MotionJoint.AnkleL => MotionJoint.AnkleR, MotionJoint.AnkleR => MotionJoint.AnkleL, _ => joint };
    }
}
