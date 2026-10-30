using UnityEngine;

namespace Wrestling.Presentation
{
    /// <summary>
    /// How a wrestler's body gives under the loads their physical body measured last frame: the legs
    /// sink under weight pressed down on them (a lift, a carry, a body on their back), the trunk leans
    /// into the opponent's push as far as the mat bears them, hands are drawn along a hold's
    /// pull and the head along a pull on the neck (a snap-down). The give changes the pose the
    /// muscles aim for, through critically damped springs, so it builds and settles with inertia and
    /// never oscillates. Small and capped: the engine's state stays what is shown. Display only.
    /// </summary>
    public sealed class LoadCompliance
    {
        /// <summary>Most the hips sink (m) and the share of body weight pressing down that sinks them all the way.</summary>
        private const float MaxSink = 0.06f;
        private const float FullSinkLoad = 0.9f;

        /// <summary>Downward load (body weights) the legs take without visible give.</summary>
        private const float SinkDeadZone = 0.04f;

        /// <summary>Most the trunk leans into a push (radians), and the share of the push's angle it takes.</summary>
        private const float MaxLean = 0.12f;
        private const float LeanShare = 0.3f;

        /// <summary>Push (body weights) below which the trunk does not lean.</summary>
        private const float LeanDeadZone = 0.15f;

        /// <summary>Hand give (m per N of the hold's pull), the most a hand gives (m), and the pull it ignores (N).</summary>
        private const float HandGivePerNewton = 6e-5f;
        private const float MaxHandGive = 0.04f;
        private const float HandDeadZone = 40f;

        /// <summary>Share of a load on the forearm that reaches the hand (an underhook or overhook carries through the forearm).</summary>
        private const float ForearmShare = 0.5f;

        /// <summary>Head give (m per N of pull on the head), its most (m) and the pull it ignores (N); the neck follows by <see cref="NeckShare"/>.</summary>
        private const float HeadGivePerNewton = 7e-5f;
        private const float MaxHeadGive = 0.045f;
        private const float HeadDeadZone = 40f;
        private const float NeckShare = 0.4f;

        /// <summary>Natural frequencies (Hz) of the give: legs and arms respond faster than the whole trunk.</summary>
        private const float LimbFrequency = 2.4f;
        private const float TrunkFrequency = 1.6f;

        /// <summary>Share of the give outside a technique (hand fighting, ties), and how quickly (s) a technique's start or end changes it.</summary>
        private const float IdleEngagement = 0.65f;
        private const float EngagementSeconds = 0.2f;

        /// <summary>An animated ankle lower than this (m) is a foot planted on the mat; it stays where it is as the hips sink.</summary>
        private const float PlantedAnkle = 0.16f;

        private Spring _sink;
        private Spring3 _lean;
        private Spring3 _handL;
        private Spring3 _handR;
        private Spring3 _head;
        private float _engagement = IdleEngagement;

        /// <summary>How far the hips sink now (m).</summary>
        public float Sink => Mathf.Max(0f, _sink.Value);

        /// <summary>The trunk's lean now (degrees).</summary>
        public float LeanDegrees => _lean.Value.magnitude * Mathf.Rad2Deg;

        /// <summary>The largest displacement the give makes to any joint now (m), for diagnostics.</summary>
        public float Displacement => Mathf.Max(Mathf.Max(Sink, _lean.Value.magnitude * 0.5f), Mathf.Max(Mathf.Max(_handL.Value.magnitude, _handR.Value.magnitude), _head.Value.magnitude));

        public void Reset()
        {
            _sink = default;
            _lean = default;
            _handL = default;
            _handR = default;
            _head = default;
            _engagement = IdleEngagement;
        }

        /// <summary>
        /// Moves the give towards what the loads measured over the last frame ask for.
        /// <paramref name="engaged"/> is true while a technique is under way, when the give is fullest.
        /// </summary>
        public void Update(PhysicalBody body, bool engaged, float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            _engagement = Mathf.Lerp(engaged ? 1f : IdleEngagement, _engagement, Mathf.Exp(-deltaTime / EngagementSeconds));
            float drive = body.Control * _engagement;
            float weight = body.Mass * -Physics.gravity.y;

            // Weight pressed down on the wrestler by the opponent (carried, lifted, lain on) bends the legs.
            float down = Mathf.Max(0f, (-body.OpponentForce.y / weight) - SinkDeadZone);
            float sink = drive * Saturate(MaxSink * down / FullSinkLoad, MaxSink) / body.LowerStrength;

            // Pushed by the opponent, a wrestler leans into the push so that it runs through their
            // centre of mass to their base, as far as the mat is bearing them up.
            var pushed = new Vector3(body.OpponentForce.x, 0f, body.OpponentForce.z);
            float borne = Mathf.Clamp01(body.GroundForce.y / weight);
            float share = Mathf.Max(0f, (pushed.magnitude / weight) - LeanDeadZone);
            Vector3 lean = share > 0f ? -pushed.normalized * (drive * borne * Saturate(Mathf.Atan(share) * LeanShare, MaxLean)) : Vector3.zero;

            float arms = drive / body.UpperStrength;
            Vector3 handL = Give(body.OpponentForceOn("LeftHand") + (body.OpponentForceOn("LeftForeArm") * ForearmShare), HandDeadZone, HandGivePerNewton, MaxHandGive) * arms;
            Vector3 handR = Give(body.OpponentForceOn("RightHand") + (body.OpponentForceOn("RightForeArm") * ForearmShare), HandDeadZone, HandGivePerNewton, MaxHandGive) * arms;
            Vector3 head = Give(body.OpponentForceOn("Head"), HeadDeadZone, HeadGivePerNewton, MaxHeadGive) * arms;

            float limb = 2f * Mathf.PI * LimbFrequency;
            float trunk = 2f * Mathf.PI * TrunkFrequency;
            _sink.Step(sink, limb, deltaTime);
            _lean.Step(lean, trunk, deltaTime);
            _handL.Step(handL, limb, deltaTime);
            _handR.Step(handR, limb, deltaTime);
            _head.Step(head, limb, deltaTime);
        }

        /// <summary>
        /// Bends this frame's pose by the give: the trunk leans about the pelvis, the body above the
        /// knees sinks with planted feet staying put, the head and the hands move along their loads,
        /// and the arms and legs are solved again to reach. A hand holding a grip keeps its hold point.
        /// </summary>
        public void Apply(ref Pose pose, BodyProportions body, bool leftHolds, bool rightHolds)
        {
            Vector3 handL = pose.HandL;
            Vector3 handR = pose.HandR;
            Vector3 hipL = pose.HipL;
            Vector3 hipR = pose.HipR;

            Vector3 lean = _lean.Value;
            float angle = lean.magnitude;
            if (angle > 1e-4f)
            {
                // Tilting up towards the lean direction: about the horizontal axis square to it.
                Quaternion tilt = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, Vector3.Cross(Vector3.up, lean / angle));
                Vector3 pivot = pose.Pelvis;
                pose.Neck = pivot + (tilt * (pose.Neck - pivot));
                pose.Head = pivot + (tilt * (pose.Head - pivot));
                pose.ShoulderL = pivot + (tilt * (pose.ShoulderL - pivot));
                pose.ShoulderR = pivot + (tilt * (pose.ShoulderR - pivot));
                pose.ElbowL = pivot + (tilt * (pose.ElbowL - pivot));
                pose.ElbowR = pivot + (tilt * (pose.ElbowR - pivot));
                handL = leftHolds ? handL : pivot + (tilt * (handL - pivot));
                handR = rightHolds ? handR : pivot + (tilt * (handR - pivot));
                pose.ChestNormal = tilt * pose.ChestNormal;
                pose.Left = tilt * pose.Left;
            }

            float sink = Sink;
            if (sink > 1e-4f)
            {
                Vector3 down = Vector3.down * sink;
                pose.Pelvis += down;
                pose.HipL += down;
                pose.HipR += down;
                pose.Neck += down;
                pose.Head += down;
                pose.ShoulderL += down;
                pose.ShoulderR += down;
                pose.ElbowL += down;
                pose.ElbowR += down;
                handL += leftHolds ? Vector3.zero : down;
                handR += rightHolds ? Vector3.zero : down;
            }

            Vector3 head = _head.Value;
            pose.Head += head;
            pose.Neck += head * NeckShare;
            pose.Spine = (pose.Neck - pose.Pelvis).normalized;

            handL += _handL.Value;
            handR += _handR.Value;
            pose.ElbowL = PoseSolver.TwoBone(pose.ShoulderL, handL, body.UpperArm, body.Forearm, pose.ElbowL - pose.ShoulderL, out pose.HandL);
            pose.ElbowR = PoseSolver.TwoBone(pose.ShoulderR, handR, body.UpperArm, body.Forearm, pose.ElbowR - pose.ShoulderR, out pose.HandR);

            if (sink > 1e-4f)
            {
                Leg(ref pose.HipL, ref pose.KneeL, ref pose.AnkleL, ref pose.ToeL, hipL, body);
                Leg(ref pose.HipR, ref pose.KneeR, ref pose.AnkleR, ref pose.ToeR, hipR, body);
            }
        }

        /// <summary>A planted leg bends to keep its foot where it is under the lowered hip; a leg in the air goes down with the hip.</summary>
        private static void Leg(ref Vector3 hip, ref Vector3 knee, ref Vector3 ankle, ref Vector3 toe, Vector3 hipBefore, BodyProportions body)
        {
            if (ankle.y >= PlantedAnkle)
            {
                Vector3 moved = hip - hipBefore;
                knee += moved;
                ankle += moved;
                toe += moved;
                return;
            }

            Vector3 pole = knee - Vector3.Lerp(hipBefore, ankle, 0.5f);
            knee = PoseSolver.TwoBone(hip, ankle, body.Thigh, body.Shin, pole, out Vector3 reached);
            toe += reached - ankle;
            ankle = reached;
        }

        /// <summary>Give along a force: none below the dead zone, then in proportion, easing into its most.</summary>
        private static Vector3 Give(Vector3 force, float deadZone, float perNewton, float most)
        {
            float size = force.magnitude;
            if (size <= deadZone)
            {
                return Vector3.zero;
            }

            return force * (Saturate((size - deadZone) * perNewton, most) / size);
        }

        /// <summary>Follows <paramref name="value"/> while small and eases smoothly into <paramref name="most"/>, never past it.</summary>
        private static float Saturate(float value, float most) => most * (float)System.Math.Tanh(value / most);

        /// <summary>
        /// A critically damped spring stepped with its exact solution for a target held over the step,
        /// so it behaves the same at any frame rate, settles without oscillating and cannot blow up.
        /// </summary>
        private struct Spring
        {
            public float Value;
            public float Velocity;

            public void Step(float target, float omega, float deltaTime)
            {
                float offset = Value - target;
                float decay = Mathf.Exp(-omega * deltaTime);
                float push = (Velocity + (omega * offset)) * deltaTime;
                Value = target + ((offset + push) * decay);
                Velocity = (Velocity - (omega * push)) * decay;
            }
        }

        private struct Spring3
        {
            public Vector3 Value;
            public Vector3 Velocity;

            public void Step(Vector3 target, float omega, float deltaTime)
            {
                Vector3 offset = Value - target;
                float decay = Mathf.Exp(-omega * deltaTime);
                Vector3 push = (Velocity + (omega * offset)) * deltaTime;
                Value = target + ((offset + push) * decay);
                Velocity = (Velocity - (omega * push)) * decay;
            }
        }
    }
}
