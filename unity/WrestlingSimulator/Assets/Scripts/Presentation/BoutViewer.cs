using System.Collections;
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
        private readonly GroundState[] _previousGround = new GroundState[2];

        /// <summary>Physics substep for the ragdolls (display only).</summary>
        private const float PhysicsStep = 1f / 120f;

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
            _session.StepTicks(ticks - warmUp);
            foreach (BoutEvent e in _session.TakeNewEvents())
            {
                _scoreboard.Push(e, _session);
            }

            _snapNextFrame = true;
            Present(1f, 0f);

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
        /// Starts a ragdoll fall when a thrown wrestler is let go (the clip's hold weakens) or when a
        /// wrestler goes from their feet to lying on the mat; then steps the physics and blends the
        /// bones. With no elapsed time (snapshots) the bodies just follow the animation.
        /// </summary>
        private void SimulateFalls(float deltaTime, bool snapped)
        {
            WrestlerCharacter[] figures = { _redFigure, _blueFigure };
            for (int i = 0; i < 2; i++)
            {
                float anchor = _choreographer.Anchors[i].Weight;
                GroundState ground = _shown[i].Ground;
                bool released = _previousAnchor[i] >= 0.95f && anchor < 0.95f;
                bool wasUp = _previousGround[i] == GroundState.Standing || _previousGround[i] == GroundState.OneKnee;
                bool landedLying = ground == GroundState.Prone || ground == GroundState.Supine || ground == GroundState.Bridge;
                if (deltaTime > 0f && !snapped)
                {
                    if (released)
                    {
                        figures[i].Ragdoll.Fall(0.9f, deltaTime, figures[1 - i].Ragdoll);
                    }
                    else if (wasUp && landedLying)
                    {
                        figures[i].Ragdoll.Fall(0.6f, deltaTime);
                    }
                }

                _previousAnchor[i] = anchor;
                _previousGround[i] = ground;
            }

            _redFigure.Ragdoll.BeforeStep(deltaTime);
            _blueFigure.Ragdoll.BeforeStep(deltaTime);
            if (deltaTime > 0f && (_redFigure.Ragdoll.Weight > 0f || _blueFigure.Ragdoll.Weight > 0f))
            {
                int steps = Mathf.Clamp(Mathf.RoundToInt(deltaTime / PhysicsStep), 1, 8);
                for (int s = 0; s < steps; s++)
                {
                    Physics.Simulate(deltaTime / steps);
                }
            }

            _redFigure.Ragdoll.AfterStep();
            _blueFigure.Ragdoll.AfterStep();
        }

        /// <summary>
        /// Moves a held body (before its whole-body rotation) so that, after the rotation and lift,
        /// its waist sits the contact thickness behind the carrier's contact point. The keyed lift
        /// is replaced by the hold in proportion to the anchor weight, so the bodies cannot separate.
        /// </summary>
        private static void Hold(ref Pose held, WrestlerFrame heldFrame, in Pose carrier, BodyProportions carrierBody, (AnchorPoint Point, float Weight, float Contact) anchor)
        {
            // The held body touches the anchor with its front, so its waist sits behind its own chest
            // after this frame's rotation: as it turns over the anchor, the contact turns with it.
            Vector3 front = PoseSolver.WholeBodyRotation(heldFrame) * held.ChestNormal;
            Vector3 target = PoseSolver.AnchorOn(in carrier, carrierBody, anchor.Point) - (front.normalized * anchor.Contact);
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
