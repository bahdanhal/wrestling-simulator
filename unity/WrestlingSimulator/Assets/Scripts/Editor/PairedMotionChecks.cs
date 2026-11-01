using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Wrestling.Data.Motion;
using Wrestling.Presentation;
using Pose = Wrestling.Presentation.Pose;

namespace Wrestling.Editor
{
    public static class PairedMotionChecks
    {
        public static void RunFromCommandLine()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        private static void Run()
        {
            var content = ContentLocator.LoadDirect();
            MotionTake take = content.Motion.Takes.First(t => t.Id == "harmony4d.test-grappling2-043");
            var range = new PairedMotionRange { Take = take, FromFrame = 176, ToFrame = 204, FirstActor = 1 };
            Pose origin = new Pose { Pelvis = new Vector3(.3f, 0, -.2f) };
            Pose opponent = new Pose { Pelvis = new Vector3(1.3f, 0, .3f) };
            PairedMotionPlayer Player()
            {
                var player = new PairedMotionPlayer();
                player.Begin(range, 176, false, origin, opponent, 1.8f, 1.8f);
                return player;
            }
            var reference = new MotionPlayback();
            var phase = new MotionPhase { Clip = "check only", Take = take.Id, FromFrame = 176, ToFrame = 204, Executor = 1 };
            reference.Begin(take, phase, origin, opponent, 1.8f, 1.8f, false);
            var a = Player(); var b = Player();
            for (int i = 0; i < 10; i++) a.Advance(.1f);
            for (int i = 0; i < 60; i++) b.Advance(1f / 60f);
            var expected = reference.Sample(20f / 28f);
            Require(Mathf.Abs(a.SourceFrame - 196) < 1e-4f && Mathf.Abs(b.SourceFrame - 196) < 1e-4f, "Playback did not advance at native source speed.");
            Same(a.Current.First, expected.Executor, 2e-5f);
            Same(a.Current.Second, expected.Opponent, 2e-5f);
            Same(a.Current.First, b.Current.First, 2e-5f);
            a.Advance(10f);
            Require(a.IsFinished && a.SourceFrame == 204, "Playback crossed the annotated range end.");
            var emptyContacts = new PairedMotionSearch(new[] { range });
            Require(b.Find(emptyContacts, contact: "verified collar tie") == null, "Unannotated contacts were treated as verified.");

            // Force a real-source handoff across a skipped portion of the same recording. This is
            // a continuity test, not evidence that the skipped contacts form a valid technique.
            var candidateRange = new PairedMotionRange { Take = take, FromFrame = 200, ToFrame = 204, FirstActor = 1 };
            var search = new PairedMotionSearch(new[] { candidateRange });
            var match = b.Find(search, minimumRemainingSeconds: .1f);
            Require(match != null, "No eligible paired source candidate.");
            var outgoing = b.Current;
            b.Switch(match, 1.8f, 1.8f);
            var zero = b.Advance(0f);
            Same(outgoing.First, zero.First, 2e-6f);
            Same(outgoing.Second, zero.Second, 2e-6f);
            Require(Vector3.Angle(outgoing.First.PelvisAxis, zero.First.PelvisAxis) < .01f, "Switch lost the outgoing pelvis frame.");

            Pose current = reference.Sample(14f / 28f).Executor;
            Pose previous = reference.Sample(13f / 28f).Executor;
            Pose incoming = reference.Sample(24f / 28f).Executor;
            Pose next = reference.Sample(25f / 28f).Executor;
            current.PelvisUp = Quaternion.AngleAxis(35f, Vector3.forward) * current.PelvisAxis;
            var transition = new RecordedPoseTransition();
            transition.Begin(in current, previous, .05f, incoming, next, .05f);
            Pose first = transition.Apply(in incoming, 0f);
            Same(current, first, 2e-6f);
            Require(Vector3.Angle(current.PelvisAxis, first.PelvisAxis) < .01f, "Frozen pelvis frame changed at transition start.");
            const float dt = .0002f;
            Pose shortly = reference.Sample((24f + (dt * take.FramesPerSecond)) / 28f).Executor;
            Pose after = transition.Apply(in shortly, dt);
            Vector3 expectedVelocity = (current.Pelvis - previous.Pelvis) / .05f;
            Require(Vector3.Distance((after.Pelvis - first.Pelvis) / dt, expectedVelocity) < .06f, "Positional velocity was not carried across the switch.");
            Pose settled = transition.Apply(in incoming, 2f);
            Same(incoming, settled, 2e-6f);
            transition.Reset();
            Same(incoming, transition.Apply(in incoming, 0), 0);
            CheckCoordinator(reference);
            Debug.Log("[paired-motion] PASS native timing, frame partition, annotated range end, unknown-contact rejection, coherent pair handoff, frozen pelvis frame and positional-velocity continuity. No production assignment or contact acceptance.");
        }

        private static void CheckCoordinator(MotionPlayback source)
        {
            // Use real source samples for both outgoing and incoming motion. The phase switch
            // occurs on a render sample; position must advance by the measured outgoing speed.
            foreach (float step in new[] { 1f / 30f, 1f / 60f, 1f / 120f })
            {
                var coordinator = new PoseTransitionCoordinator();
                object firstPhase = new object(), counter = new object();
                Pose Source(float seconds) => source.Sample(seconds / 1f).Executor;
                Pose Sample(float time, bool recorded, object owner, int phase, float sourceTime)
                {
                    Pose raw = Source(sourceTime), future = Source(sourceTime + .001f);
                    return coordinator.Sample(owner, phase, recorded, time, in raw, in raw,
                        true, in future, in future, .001f).Red;
                }
                Pose a = Sample(0f, true, firstPhase, 0, .20f);
                Pose b = Sample(step, true, firstPhase, 0, .20f + step);
                // Duplicate sampling should leave the previous measured velocity intact.
                Sample(step, true, firstPhase, 0, .20f + step);
                Pose c = Sample(2f * step, false, null, -1, .65f);
                Vector3 projected = b.Pelvis + (b.Pelvis - a.Pelvis);
                Require(Vector3.Distance(c.Pelvis, projected) < 1e-5f, "Recorded exit froze or jumped at the boundary.");
                Require(Vector3.Angle(b.PelvisAxis, c.PelvisAxis) < .01f, "Recorded exit lost its frozen pelvis frame.");
                const float probe = .0002f;
                Pose cNext = Sample((2f * step) + probe, false, null, -1, .65f + probe);
                Vector3 carried = (cNext.Pelvis - c.Pelvis) / probe;
                Require(Vector3.Distance(carried, (b.Pelvis - a.Pelvis) / step) < .08f,
                    "Recorded exit did not carry positional velocity.");
                Pose d = Sample(3f * step, true, counter, 1, .75f);
                Require(Vector3.Distance(d.Pelvis, c.Pelvis) < .3f, "Counter interruption jumped.");
                Pose raw = Source(.85f);
                Pose reset = coordinator.Sample(counter, 1, true, 4f, in raw, in raw, false,
                    in raw, in raw, 0f).Red;
                Same(raw, reset, 2e-6f);
                Pose backwards = Source(.9f);
                reset = coordinator.Sample(counter, 1, true, 3f, in backwards, in backwards, false,
                    in backwards, in backwards, 0f).Red;
                Same(backwards, reset, 2e-6f);
            }
            // A continuous series of frames must let the correction settle to the raw target.
            var settling = new PoseTransitionCoordinator();
            object phaseA = new object(), phaseB = new object();
            Pose start = source.Sample(.2f).Executor, target = source.Sample(.7f).Executor;
            settling.Sample(phaseA, 0, true, 0f, in start, in start, false, in start, in start, 0f);
            Pose result = default;
            for (int i = 1; i <= 130; i++)
                result = settling.Sample(phaseB, 0, true, i / 60f, in target, in target, false,
                    in target, in target, 0f).Red;
            Same(target, result, 2e-6f);
        }

        private static void Same(Pose a, Pose b, float tolerance)
        {
            Require(Vector3.Distance(a.Pelvis, b.Pelvis) <= tolerance, "Pelvis continuity mismatch.");
            Require(Vector3.Distance(a.Neck, b.Neck) <= tolerance, "Neck continuity mismatch.");
            Require(Vector3.Distance(a.Head, b.Head) <= tolerance, "Head continuity mismatch.");
            Require(Vector3.Distance(a.ShoulderL, b.ShoulderL) <= tolerance, "ShoulderL continuity mismatch.");
            Require(Vector3.Distance(a.ShoulderR, b.ShoulderR) <= tolerance, "ShoulderR continuity mismatch.");
            Require(Vector3.Distance(a.ElbowL, b.ElbowL) <= tolerance, "ElbowL continuity mismatch.");
            Require(Vector3.Distance(a.ElbowR, b.ElbowR) <= tolerance, "ElbowR continuity mismatch.");
            Require(Vector3.Distance(a.HandL, b.HandL) <= tolerance, "HandL continuity mismatch.");
            Require(Vector3.Distance(a.HandR, b.HandR) <= tolerance, "HandR continuity mismatch.");
            Require(Vector3.Distance(a.HipL, b.HipL) <= tolerance, "HipL continuity mismatch.");
            Require(Vector3.Distance(a.HipR, b.HipR) <= tolerance, "HipR continuity mismatch.");
            Require(Vector3.Distance(a.KneeL, b.KneeL) <= tolerance, "KneeL continuity mismatch.");
            Require(Vector3.Distance(a.KneeR, b.KneeR) <= tolerance, "KneeR continuity mismatch.");
            Require(Vector3.Distance(a.AnkleL, b.AnkleL) <= tolerance, "AnkleL continuity mismatch.");
            Require(Vector3.Distance(a.AnkleR, b.AnkleR) <= tolerance, "AnkleR continuity mismatch.");
            Require(Vector3.Distance(a.ToeL, b.ToeL) <= tolerance, "ToeL continuity mismatch.");
            Require(Vector3.Distance(a.ToeR, b.ToeR) <= tolerance, "ToeR continuity mismatch.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
