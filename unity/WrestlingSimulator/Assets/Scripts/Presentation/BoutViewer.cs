using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Wrestling.Core.Match;
using Wrestling.Core.Rules;
using Wrestling.Data;
using Wrestling.Data.Animation;

namespace Wrestling.Presentation
{
    /// <summary>
    /// Watch mode: two AI wrestlers wrestle a full bout on the engine, shown on the mat with the
    /// broadcast camera and scoreboard. Runs the next bout after each result.
    /// </summary>
    public sealed class BoutViewer : MonoBehaviour
    {
        [SerializeField] private WrestlingStyle _style = WrestlingStyle.Freestyle;
        [SerializeField, Range(1, 95)] private int _level = 60;
        [SerializeField] private int _firstSeed = 4;
        [SerializeField, Range(0.25f, 8f)] private float _playbackSpeed = 1f;
        [SerializeField] private float _pauseAfterResult = 5f;

        [Header("Scene")]
        [SerializeField] private MatView _mat;
        [SerializeField] private BroadcastCamera _camera;
        [SerializeField] private Scoreboard _scoreboard;
        [SerializeField] private Font _font;

        [Header("Wrestlers")]
        [SerializeField] private CharacterLibrary _characters;
        [SerializeField] private Color _redSinglet = new Color(0.80f, 0.08f, 0.10f);
        [SerializeField] private Color _blueSinglet = new Color(0.07f, 0.22f, 0.75f);
        [SerializeField] private Color _shoe = new Color(0.08f, 0.08f, 0.09f);


        private readonly WrestlerFrame[] _shown = new WrestlerFrame[2];
        private WrestlerCharacter _redFigure;
        private WrestlerCharacter _blueFigure;
        private Choreographer _choreographer;
        private GameContent _content;
        private BoutSession _session;
        private ulong _seed;
        private float _finishedFor;
        private bool _snapNextFrame;
        private readonly float[] _previousAnchor = new float[2];
        private TechniqueExecution _throwTechnique;
        private int _throwPhase = -1;
        private Corner _thrown;
        private bool _turning;
        private readonly GroundState[] _previousGround = new GroundState[2];

        /// <summary>Physics substep for the ragdolls (display only).</summary>
        private const float PhysicsStep = 1f / 240f;

        /// <summary>Ticks of real-time presentation run before a fast-forwarded frame, so falls are in motion.</summary>
        private const int FastForwardWarmUp = 45;

        public BoutSession Session => _session;

        public WrestlingStyle Style
        {
            get => _style;
            set => _style = value;
        }

        private IEnumerator Start()
        {
            if (ContentLocator.NeedsExtraction)
            {
                string error = null;
                yield return ContentLocator.LoadExtracted(content => _content = content, message => error = message);
                if (error != null)
                {
                    Debug.LogError(error);
                    yield break;
                }
            }
            else
            {
                _content = ContentLocator.LoadDirect();
            }

            Initialise(_content);
            StartBout((ulong)_firstSeed);
        }

        /// <summary>Builds the mat, figures and scoreboard for loaded content.</summary>
        public void Initialise(GameContent content)
        {
            _content = content;
            _choreographer = new Choreographer(content.Animations);
            Physics.simulationMode = SimulationMode.Script;
            _mat.Build(content.Rules.Mat);
            _scoreboard.Build(_font);
        }

        public void StartBout(ulong seed)
        {
            _seed = seed;
            _session = new BoutSession(_content, _style, _level, seed);
            _redFigure = Figure(_redFigure, "Red", Corner.Red, _redSinglet);
            _blueFigure = Figure(_blueFigure, "Blue", Corner.Blue, _blueSinglet);
            _scoreboard.ResetFor(_session);
            _finishedFor = 0f;
            _snapNextFrame = true;
            Present(1f, 0f);
        }

        /// <summary>Runs the bout forward without real time, then shows the result (used for stills and skipping).</summary>
        public void FastForward(int ticks)
        {
            int warmUp = Mathf.Min(FastForwardWarmUp, ticks);
            if (ticks > warmUp)
            {
                // A real jump in time: start from the pose at rest, then play the last ticks live.
                _session.StepTicks(ticks - warmUp);
                foreach (BoutEvent e in _session.TakeNewEvents())
                {
                    _scoreboard.Push(e, _session);
                }

                _snapNextFrame = true;
                Present(1f, 0f);
            }

            // The last ticks play as they would live, so ragdoll falls have their motion.
            const float tickSeconds = 1f / BoutClock.TicksPerSecond;
            for (int i = 0; i < warmUp; i++)
            {
                _session.StepTicks(1);
                foreach (BoutEvent e in _session.TakeNewEvents())
                {
                    _scoreboard.Push(e, _session);
                }

                Present(1f, tickSeconds);
            }
        }

        private void Update()
        {
            if (_session == null)
            {
                return;
            }

            if (_session.Engine.IsFinished)
            {
                _finishedFor += Time.deltaTime;
                if (_finishedFor >= _pauseAfterResult)
                {
                    StartBout(_seed + 1);
                    return;
                }
            }

            _session.Advance(Time.deltaTime * _playbackSpeed);
            foreach (BoutEvent e in _session.TakeNewEvents())
            {
                _scoreboard.Push(e, _session);
                if (e.Type == BoutEventType.Restart || e.Type == BoutEventType.PeriodStarted)
                {
                    _snapNextFrame = true;
                }
            }

            Present(_session.Alpha, Time.deltaTime);
        }

        private void Present(float alpha, float deltaTime)
        {
            BoutEngine engine = _session.Engine;
            WrestlerFrame red = _session.Frame(Corner.Red, alpha);
            WrestlerFrame blue = _session.Frame(Corner.Blue, alpha);
            Vector3 view = _camera.transform.forward;
            Staging.Arrange(ref red, ref blue, engine.Position, engine.Top, new Vector2(view.x, view.z).normalized);
            _choreographer.Apply(engine, ref red, ref blue, alpha, _shown);
            Smooth(0, red, deltaTime);
            Smooth(1, blue, deltaTime);
            bool snapped = _snapNextFrame;
            _snapNextFrame = false;

            PoseRole redRole = Staging.RoleOf(Corner.Red, engine.Position, engine.Top);
            PoseRole blueRole = Staging.RoleOf(Corner.Blue, engine.Position, engine.Top);
            Pose redPose = PoseSolver.SolveBody(_shown[0], _redFigure.Body, redRole);
            Pose bluePose = PoseSolver.SolveBody(_shown[1], _blueFigure.Body, blueRole);
            (AnchorPoint Point, float Weight, float Contact) redAnchor = _choreographer.Anchors[(int)Corner.Red];
            (AnchorPoint Point, float Weight, float Contact) blueAnchor = _choreographer.Anchors[(int)Corner.Blue];
            if (redAnchor.Weight > 0f)
            {
                // Blue carries red: pose blue first, then hold red on blue's contact point.
                PoseSolver.ApplyWholeBody(ref bluePose, _shown[1], _blueFigure.Body);
                Hold(ref redPose, _shown[0], in bluePose, _blueFigure.Body, redAnchor);
                PoseSolver.ApplyWholeBody(ref redPose, _shown[0], _redFigure.Body);
            }
            else if (blueAnchor.Weight > 0f)
            {
                PoseSolver.ApplyWholeBody(ref redPose, _shown[0], _redFigure.Body);
                Hold(ref bluePose, _shown[1], in redPose, _redFigure.Body, blueAnchor);
                PoseSolver.ApplyWholeBody(ref bluePose, _shown[1], _blueFigure.Body);
            }
            else
            {
                PoseSolver.ApplyWholeBody(ref redPose, _shown[0], _redFigure.Body);
                PoseSolver.ApplyWholeBody(ref bluePose, _shown[1], _blueFigure.Body);
            }

            if (redAnchor.Weight <= 0f && blueAnchor.Weight <= 0f)
            {
                // Held bodies are in contact by construction; only free bodies need separating.
                BodyContact.Separate(ref redPose, _redFigure.Body, _shown[0].Ground, ref bluePose, _blueFigure.Body, _shown[1].Ground);
            }
            Pose redBody = redPose;
            Pose blueBody = bluePose;
            PoseSolver.SolveArms(ref redPose, _redFigure.Body, redRole, _shown[0], in blueBody, engine.Grips.Of(Corner.Red));
            PoseSolver.SolveArms(ref bluePose, _blueFigure.Body, blueRole, _shown[1], in redBody, engine.Grips.Of(Corner.Blue));
            _redFigure.Apply(in redPose, GripOn(engine, Corner.Red, GripSide.Left), GripOn(engine, Corner.Red, GripSide.Right));
            _blueFigure.Apply(in bluePose, GripOn(engine, Corner.Blue, GripSide.Left), GripOn(engine, Corner.Blue, GripSide.Right));
            SimulateFalls(deltaTime, snapped);

            Vector3 focus = WrestlerFrame.OnMat((_shown[0].Position + _shown[1].Position) * 0.5f);
            bool grounded = engine.Position != BoutPosition.Neutral;
            if (deltaTime <= 0f)
            {
                _camera.Snap(focus, grounded);
            }
            else
            {
                _camera.Follow(focus, grounded, deltaTime);
            }

            _scoreboard.Refresh(_session);
        }

        /// <summary>
        /// Steps both physics bodies after they take this frame's animated pose as their target, so
        /// contact between the wrestlers and with the mat is resolved by the solver. Starts a fall when
        /// a thrown wrestler is let go (the clip's hold weakens) or when a wrestler goes from their
        /// feet to lying on the mat. With no elapsed time (snapshots) the bodies sit on the animation.
        /// </summary>
        private void SimulateFalls(float deltaTime, bool snapped)
        {
            WrestlerCharacter[] figures = { _redFigure, _blueFigure };
            if (snapped || deltaTime <= 0f)
            {
                // Snapshots, bout starts and restarts: the bodies sit exactly on the animated pose.
                for (int i = 0; i < 2; i++)
                {
                    figures[i].Ragdoll.Reset();
                    _previousAnchor[i] = _choreographer.Anchors[i].Weight;
                    _previousGround[i] = _shown[i].Ground;
                }

                return;
            }

            for (int i = 0; i < 2; i++)
            {
                float anchor = _choreographer.Anchors[i].Weight;
                GroundState ground = _shown[i].Ground;
                bool released = _previousAnchor[i] >= 0.95f && anchor < 0.95f;
                bool wasUp = _previousGround[i] == GroundState.Standing || _previousGround[i] == GroundState.OneKnee;
                bool landedLying = ground == GroundState.Prone || ground == GroundState.Supine || ground == GroundState.Bridge;
                if (released && !figures[i].Ragdoll.IsThrown)
                {
                    figures[i].Ragdoll.Fall(0.9f);
                }
                else if (wasUp && landedLying)
                {
                    figures[i].Ragdoll.Fall(0.6f);
                }

                _previousAnchor[i] = anchor;
                _previousGround[i] = ground;
            }

            var throwing = _choreographer.Throw;
            UpdateThrow(throwing, figures);

            _redFigure.Ragdoll.BeforeStep(deltaTime);
            _blueFigure.Ragdoll.BeforeStep(deltaTime);
            int steps = Mathf.Clamp(Mathf.CeilToInt(deltaTime / PhysicsStep), 1, 8);
            for (int s = 0; s < steps; s++)
            {
                _redFigure.Ragdoll.Drive();
                _blueFigure.Ragdoll.Drive();
                if (throwing != null && _throwTechnique != null && !_turning)
                {
                    figures[(int)_thrown].Ragdoll.HoldLoad();
                }
                else if (throwing != null && _throwTechnique != null)
                {
                    var t = throwing.Value;
                    float progress = t.Progress + ((s + 0.5f) / steps * deltaTime / Mathf.Max(t.PhaseSeconds, 1e-3f));
                    (float angle, float rate) = ThrowPlan(t.Spec, progress, t.PhaseSeconds);
                    figures[(int)_thrown].Ragdoll.DriveThrow(angle, rate, deltaTime / steps);
                    if (s == 0)
                    {
                        Debug.Log($"[throwstep] {t.Technique.Definition.Id} p={t.Progress:F2} gap={figures[(int)_thrown].Ragdoll.Gap * 100f:F0}cm loaded={figures[(int)_thrown].Ragdoll.Loaded} force={figures[(int)_thrown].Ragdoll.ThrowForce:F0}");
                    }
                }

                Physics.Simulate(deltaTime / steps);
            }

            _redFigure.Ragdoll.AfterStep();
            _blueFigure.Ragdoll.AfterStep();
        }

        /// <summary>
        /// Starts the thrown wrestler's physical flight when a throw phase reaches its window, and lets
        /// go when the window ends or the technique stops (completed, failed or countered).
        /// </summary>
        private void UpdateThrow((TechniqueExecution Technique, Corner Thrower, ThrowSpec Spec, float Progress, float PhaseSeconds, bool Mirrored)? throwing, WrestlerCharacter[] figures)
        {
            bool active = throwing != null && throwing.Value.Progress >= throwing.Value.Spec.From && throwing.Value.Progress < throwing.Value.Spec.To;
            if (_throwTechnique != null)
            {
                // A load carries on into its throw within the same technique; anything else lets go.
                bool same = throwing != null && ReferenceEquals(throwing.Value.Technique, _throwTechnique)
                    && (active || (throwing.Value.Spec.Kind == ThrowKind.Load && throwing.Value.Progress >= throwing.Value.Spec.To));
                if (!same)
                {
                    figures[(int)_thrown].Ragdoll.EndThrow(0.8f);
                    figures[1 - (int)_thrown].Ragdoll.SetCarrying(false);
                    _throwTechnique = null;
                    _throwPhase = -1;
                    _turning = false;
                }
            }

            if (!active)
            {
                return;
            }

            var t = throwing.Value;
            Vector3 axis = ThrowAxisFor(t);
            if (_throwTechnique == null)
            {
                _thrown = t.Thrower.Opponent();
                var grips = new List<(string, string)>();
                foreach (ThrowGrip grip in t.Spec.Grips)
                {
                    grips.Add((Mirror(grip.Thrower, t.Mirrored), Mirror(grip.Thrown, t.Mirrored)));
                }

                figures[(int)_thrown].Ragdoll.BeginThrow(figures[(int)t.Thrower].Ragdoll, t.Spec.Pivot, axis, grips, ThrowStrength(_session.WrestlerOf(t.Thrower)));
                figures[(int)t.Thrower].Ragdoll.SetCarrying(true);
                _throwTechnique = t.Technique;
                _turning = false;
            }

            if (t.Spec.Kind == ThrowKind.Throw && !_turning)
            {
                // The opponent is loaded: the turn starts about the thrower's current facing.
                figures[(int)_thrown].Ragdoll.StartTurn(axis);
                _turning = true;
            }

            _throwPhase = t.Technique.PhaseIndex;
        }

        private Vector3 ThrowAxisFor((TechniqueExecution Technique, Corner Thrower, ThrowSpec Spec, float Progress, float PhaseSeconds, bool Mirrored) t)
        {
            Vector3 forward = WrestlerFrame.OnMat(_shown[(int)t.Thrower].Facing).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            // ω = up × direction makes the body's top move along the direction (v = ω × r).
            return t.Spec.Axis == ThrowAxis.Lateral
                ? Vector3.Cross(Vector3.up, forward)
                : Vector3.Cross(Vector3.up, right) * (t.Mirrored ? -1f : 1f);
        }

        /// <summary>
        /// Most force (newtons) a wrestler can put into a throw: about 1.5 kN for the weakest to 3.5 kN
        /// for the strongest, from upper- and lower-body strength (a 50 average gives 2.5 kN).
        /// </summary>
        private static float ThrowStrength(Wrestling.Core.Wrestlers.Wrestler wrestler)
        {
            float strength = (wrestler.Attributes[Wrestling.Core.Wrestlers.WrestlerAttribute.UpperBodyStrength]
                + wrestler.Attributes[Wrestling.Core.Wrestlers.WrestlerAttribute.LowerBodyStrength]) * 0.5f;
            return 1500f + (20f * strength);
        }

        /// <summary>The throw's planned rotation at phase progress <paramref name="progress"/>: eased in and out over its window.</summary>
        private static (float Angle, float Rate) ThrowPlan(ThrowSpec spec, float progress, float phaseSeconds)
        {
            float span = spec.To - spec.From;
            float u = Mathf.Clamp01((progress - spec.From) / span);
            float angle = spec.Degrees * u * u * (3f - (2f * u));
            float rate = spec.Degrees * 6f * u * (1f - u) / (span * Mathf.Max(phaseSeconds, 1e-3f));
            return (angle, rate);
        }

        private static string Mirror(string segment, bool mirrored)
        {
            if (!mirrored)
            {
                return segment;
            }

            return segment.StartsWith("Left") ? "Right" + segment.Substring(4) : segment.StartsWith("Right") ? "Left" + segment.Substring(5) : segment;
        }

        /// <summary>
        /// Moves a held body (before its whole-body rotation) so that, after the rotation and lift,
        /// its waist sits the contact thickness outside the carrier's contact point. The keyed lift
        /// is replaced by the hold in proportion to the anchor weight, so the bodies cannot separate.
        /// </summary>
        private static void Hold(ref Pose held, WrestlerFrame heldFrame, in Pose carrier, BodyProportions carrierBody, (AnchorPoint Point, float Weight, float Contact) anchor)
        {
            // The held waist sits just outside the carrier's surface at the contact point, along the
            // carrier's outward direction there, so the target never lies inside the carrier's body.
            Vector3 point = PoseSolver.AnchorOn(in carrier, carrierBody, anchor.Point);
            Vector3 outward = point - Vector3.Lerp(carrier.Pelvis, carrier.Neck, 0.4f);
            outward = outward.sqrMagnitude > 1e-6f ? outward.normalized : Vector3.up;
            Vector3 target = point + (outward * anchor.Contact);
            Vector3 free = PoseSolver.WholeBodyPivot(in held) + (Vector3.up * heldFrame.Lift);
            PoseSolver.Translate(ref held, (target - free) * Mathf.Clamp01(anchor.Weight));
        }

        /// <summary>1 when the hand is holding a grip, so the fingers close around it.</summary>
        private static float GripOn(BoutEngine engine, Corner corner, GripSide hand) => engine.Grips.IsHandFree(corner, hand) ? 0f : 1f;

        /// <summary>Eases the displayed frame towards the engine's so staging changes do not jump.</summary>
        private void Smooth(int index, WrestlerFrame target, float deltaTime)
        {
            if (_snapNextFrame || deltaTime <= 0f || (target.Position - _shown[index].Position).sqrMagnitude > 4f)
            {
                _shown[index] = target;
                return;
            }

            float blend = 1f - Mathf.Exp(-14f * deltaTime);
            _shown[index] = WrestlerFrame.Lerp(_shown[index], target, blend);
            _shown[index].Ground = target.Ground;
            _shown[index].Stance = target.Stance;
            _shown[index].RollSide = target.RollSide;
        }

        private WrestlerCharacter Figure(WrestlerCharacter existing, string figureName, Corner corner, Color singlet)
        {
            WrestlerCharacter figure = existing;
            if (figure == null)
            {
                figure = new GameObject(figureName + " wrestler").AddComponent<WrestlerCharacter>();
            }

            var wrestler = _session.WrestlerOf(corner);
            CharacterLibrary.CharacterBase characterBase = _characters.For(wrestler.Gender == Wrestling.Core.Wrestlers.Gender.Female);
            figure.Build(characterBase, new WrestlerLook(wrestler, characterBase.Skins.Length), singlet);
            return figure;
        }

        public void Configure(MatView mat, BroadcastCamera broadcastCamera, Scoreboard scoreboard, Font font, CharacterLibrary characters)
        {
            _mat = mat;
            _camera = broadcastCamera;
            _scoreboard = scoreboard;
            _font = font;
            _characters = characters;
        }
    }
}
