using UnityEngine;
using Wrestling.Core.Match;
using Wrestling.Core.Rules;

namespace Wrestling.Presentation
{
    /// <summary>Adapts recorded arms to the engine's actual grips. These are game constraints,
    /// not claims that a palm contact was measured in a source recording.</summary>
    public sealed class RecordedGripRetargeter
    {
        private sealed class HandState
        {
            public Vector3 Correction;
            public float Weight;
            public HandGripStyle Style;
            public float Aperture;
            public Quaternion Orientation;
        }

        public readonly struct Result
        {
            public readonly bool Requested;
            public readonly bool Holding;
            public readonly Vector3 PalmTarget;
            public readonly float PalmError;
            public readonly float SourceWristDisplacement;
            public Result(bool requested, bool holding, Vector3 target, float error, float displacement)
            { Requested = requested; Holding = holding; PalmTarget = target; PalmError = error; SourceWristDisplacement = displacement; }
        }

        private readonly HandState[] _state = { new HandState(), new HandState(), new HandState(), new HandState() };
        private readonly Result[] _results = new Result[4];
        public Result Hand(Corner corner, GripSide side) => _results[((int)corner * 2) + (int)side];

        public void Reset()
        {
            for (int i = 0; i < 4; i++)
            {
                _state[i].Correction = Vector3.zero;
                _state[i].Weight = 0f;
                _results[i] = default;
            }
        }

        public void Apply(BoutEngine engine, WrestlerCharacter red, WrestlerCharacter blue, HandGrips holds, float seconds, bool reset)
        {
            if (reset) Reset();
            Pose redPose = red.Rig.MeasurePose(), bluePose = blue.Rig.MeasurePose();
            for (int corner = 0; corner < 2; corner++)
            {
                WrestlerCharacter self = corner == 0 ? red : blue;
                Pose own = corner == 0 ? redPose : bluePose;
                Pose other = corner == 0 ? bluePose : redPose;
                for (int side = 0; side < 2; side++)
                {
                    int index = (corner * 2) + side;
                    var state = _state[index];
                    bool left = side == 0;
                    var handSide = (GripSide)side;
                    Grip grip = HandGrips.ForHand(engine, (Corner)corner, handSide);
                    GripSocket? socket = null;
                    if (grip != null)
                    {
                        var sockets = HandGrips.Sockets(grip, in own, self.Body, in other);
                        socket = left ? sockets.Left : sockets.Right;
                    }
                    GripLink link = holds.Link((Corner)corner, handSide);
                    bool holding = link != null && link.Holding;
                    bool requested = holding || socket.HasValue;
                    Transform hand = self.Rig.BoneOf(left ? Bone.LeftHand : Bone.RightHand);
                    Vector3 source = hand.position;
                    Quaternion chest = self.Rig.SegmentRotation(Bone.Chest);
                    float follow = reset ? 1f : 1f - Mathf.Exp(-Mathf.Max(0f, seconds) / .05f);
                    state.Weight = Mathf.Lerp(state.Weight, requested ? 1f : 0f, follow);
                    if (socket.HasValue)
                    {
                        state.Style = socket.Value.Style;
                        state.Aperture = socket.Value.Aperture;
                        self.Rig.AimPalm(left, socket.Value.Direction, socket.Value.Palm, state.Weight);
                    }
                    if (holding)
                    {
                        state.Orientation = link.HeldOrientation;
                        hand.rotation = Quaternion.Slerp(hand.rotation, state.Orientation, state.Weight);
                    }
                    Vector3 palmOffset = hand.rotation * self.Physical.PalmAnchor(left ? "LeftHand" : "RightHand");
                    Vector3 target = holding ? link.HeldPoint : socket?.Contact ?? (hand.position + palmOffset);
                    if (!holding && socket.HasValue)
                        target = HandGrips.SurfaceFor(corner == 0 ? blue.Physical : red.Physical, grip, handSide, target);
                    Vector3 desiredCorrection = requested ? Quaternion.Inverse(chest) * (target - palmOffset - source) : Vector3.zero;
                    state.Correction = Vector3.Lerp(state.Correction, desiredCorrection, follow);
                    var reached = self.Rig.ReachWrist(left, source + (chest * state.Correction));
                    self.Rig.ApplyHandGrip(handSide, state.Style, state.Weight, state.Aperture);
                    _results[index] = new Result(requested, holding, target,
                        requested ? Vector3.Distance(hand.position + palmOffset, target) : 0f,
                        Vector3.Distance(hand.position, source));
                }
            }
        }
    }
}
