using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Wrestling.Core.Match;
using Wrestling.Core.Rules;
using Wrestling.Data;
using Wrestling.Data.Animation;
using Wrestling.Data.Motion;

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
        private readonly Pose[] _lastPoses = new Pose[2];
        private readonly PoseTransitionCoordinator _poseTransitions = new PoseTransitionCoordinator();
        private readonly RecordedGripRetargeter _recordedGrips = new RecordedGripRetargeter();
        public RecordedContactConstraints.Result RecordedContacts { get; private set; }
        private readonly CharacterRig.RecordedLegsResult?[] _recordedLegs = new CharacterRig.RecordedLegsResult?[2];

        /// <summary>Recorded ankle targets and any unreachable residuals before physical tracking, when a recorded phase is active.</summary>
        public CharacterRig.RecordedLegsResult? RecordedLegs(Corner corner) => _recordedLegs[(int)corner];
        public CharacterRig.RecordedArmsResult? RecordedArms(Corner corner) =>
            (corner == Corner.Red ? _redFigure : _blueFigure)?.Rig.RecordedArms;
        private List<(string Thrower, string Thrown)> _throwGrips = new List<(string, string)>();
        private Corner _thrown;
        private bool _turning;


        private readonly GroundState[] _previousGround = new GroundState[2];
        private readonly HandGrips _handGrips = new HandGrips();
        private Corner? _lastExecutor;
        private readonly Vector2[] _landingOffset = new Vector2[2];
        private readonly bool[] _followLanding = new bool[2];
        private readonly LoadCompliance[] _compliance = { new LoadCompliance(), new LoadCompliance() };
        private readonly Exertion[] _exertion = { new Exertion(), new Exertion() };

        /// <summary>
        /// Whether the bodies give under the loads they measure (on in the game; tools turn it off to
        /// compare). See <see cref="LoadCompliance"/>.
        /// </summary>
        public static bool PhysicalCompliance = true;

        /// <summary>How a corner's body is giving under load now (diagnostics).</summary>
        public LoadCompliance Compliance(Corner corner) => _compliance[(int)corner];

        /// <summary>How worked a corner's wrestler looks now, 0–1 (diagnostics).</summary>
        public float ExertionOf(Corner corner) => _exertion[(int)corner].Value;

        /// <summary>The physical holds of the engine's grips (diagnostics).</summary>
        public HandGrips HandGrips => _handGrips;
        public RecordedGripRetargeter.Result RecordedGrip(Corner corner, GripSide side) => _recordedGrips.Hand(corner, side);

        /// <summary>After a throw, the thrown wrestler's staging stays where the throw landed them, easing back to the engine's placement over about this long (s).</summary>
        private const float LandingOffsetSeconds = 3f;

        /// <summary>
        /// Shifts a thrown wrestler's staged place to where the throw physically landed them, and eases
        /// the shift away, so the body is not dragged straight back to the engine's abstract position.
        /// On the mat with one wrestler on top (par terre, danger), the top one is placed by the bottom
        /// one, so they move with them.
        /// </summary>
        private void ApplyLandingOffsets(ref WrestlerFrame red, ref WrestlerFrame blue, float deltaTime)
        {
            float keep = deltaTime > 0f ? Mathf.Exp(-deltaTime / LandingOffsetSeconds) : 1f;
            for (int i = 0; i < 2; i++)
            {
                PhysicalBody body = (i == 0 ? _redFigure : _blueFigure).Physical;
                if (_followLanding[i] && body.IsFalling && deltaTime > 0f)
                {
                    // Still flying or rolling: the staged place is where the body is.
                    Vector2 staged = i == 0 ? red.Position : blue.Position;
                    _landingOffset[i] = new Vector2(body.HipPosition.x, body.HipPosition.z) - staged;
                    continue;
                }

                _followLanding[i] = false;
                _landingOffset[i] *= keep;
            }

            Vector2 redShift = _landingOffset[0];
            Vector2 blueShift = _landingOffset[1];
            BoutEngine engine = _session.Engine;
            if (engine.Top != null && (engine.Position == BoutPosition.ParTerre || engine.Position == BoutPosition.Danger))
            {
                if (engine.Top == Corner.Red)
                {
                    redShift += blueShift;
                }
                else
                {
                    blueShift += redShift;
                }
            }

            Shift(ref red, redShift);
            Shift(ref blue, blueShift);

            static void Shift(ref WrestlerFrame frame, Vector2 offset)
            {
                frame.Position += offset;
                frame.LeftFoot += offset;
                frame.RightFoot += offset;
            }
        }

        /// <summary>
        /// Physics step for the bodies (display only). Always exactly this long, whatever the frame
        /// rate, so the bodies behave the same on every device: a frame takes as many whole steps as
        /// its time holds and carries the rest over to the next frame.
        /// </summary>
        private const float PhysicsStep = 1f / 240f;

        /// <summary>Most steps one frame takes (1/15 s); a longer stall drops the backlog instead of stretching the step.</summary>
        private const int MaxStepsPerFrame = 16;

        /// <summary>Frame time not yet stepped (less than one step), and time since the bodies last took their targets.</summary>
        private float _unstepped;
        private float _sinceTargets;

        /// <summary>Ticks of real-time presentation run before a fast-forwarded frame, so falls are in motion.</summary>
        private const int FastForwardWarmUp = 45;

        public BoutSession Session => _session;

        /// <summary>Called after every presented frame (tools: diagnostics and recording).</summary>
        public static System.Action<BoutViewer> Presented;

        /// <summary>The move clip playing (diagnostics).</summary>
        public string CurrentClip => _choreographer?.CurrentClip;
        public string RecordedTakeId => _choreographer?.RecordedTakeId;
        public float RecordedSourceFrame => _choreographer?.RecordedFrame ?? 0f;

        /// <summary>The body state shown for a corner this frame (diagnostics).</summary>
        public WrestlerFrame Shown(Corner corner) => _shown[(int)corner];

        public WrestlerCharacter RedFigure => _redFigure;

        public WrestlerCharacter BlueFigure => _blueFigure;

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
        public void Initialise(GameContent content, MotionLibrary motionOverride = null)
        {
            _content = content;
            // Tools can preview an explicitly supplied library without editing production assignments.
            _choreographer = new Choreographer(content.Animations, motionOverride ?? content.Motion);
            Physics.simulationMode = SimulationMode.Script;
            _mat.Build(content.Rules.Mat);
            _scoreboard.Build(_font);
        }

        public void StartBout(ulong seed)
        {
            _seed = seed;
            _session = new BoutSession(_content, _style, _level, seed);
            // Holds and throws belong to the bodies of the bout before; the figures are rebuilt.
            _handGrips.Clear();
            _throwTechnique = null;
            _throwPhase = -1;
            _throwGrips = new List<(string, string)>();
            _turning = false;
            _lastExecutor = null;
            _poseTransitions.Reset();
            _recordedGrips.Reset();
            _landingOffset[0] = Vector2.zero;
            _landingOffset[1] = Vector2.zero;
            _followLanding[0] = false;
            _followLanding[1] = false;
            for (int i = 0; i < 2; i++)
            {
                _compliance[i].Reset();
                _exertion[i].Reset();
            }

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

            PlayFrame(Time.deltaTime);
        }

        /// <summary>Plays one displayed frame of <paramref name="deltaTime"/> seconds, as the live view does (also the physics lab at other frame rates).</summary>
        public void PlayFrame(float deltaTime)
        {
            _session.Advance(deltaTime * _playbackSpeed);
            foreach (BoutEvent e in _session.TakeNewEvents())
            {
                _scoreboard.Push(e, _session);
                if (e.Type == BoutEventType.Restart || e.Type == BoutEventType.PeriodStarted)
                {
                    _snapNextFrame = true;
                }
            }

            Present(_session.Alpha, deltaTime);
        }

        private void Present(float alpha, float deltaTime)
        {
            BoutEngine engine = _session.Engine;
            WrestlerFrame red = _session.Frame(Corner.Red, alpha);
            WrestlerFrame blue = _session.Frame(Corner.Blue, alpha);
            Vector3 view = _camera.transform.forward;
            Staging.Arrange(ref red, ref blue, engine.Position, engine.Top, new Vector2(view.x, view.z).normalized);
            _choreographer.Apply(engine, ref red, ref blue, alpha, _shown);
            ApplyLandingOffsets(ref red, ref blue, deltaTime);
            Smooth(0, red, deltaTime);
            Smooth(1, blue, deltaTime);
            bool snapped = _snapNextFrame;
            _snapNextFrame = false;
            if (snapped) _handGrips.Clear();

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

            _choreographer.ApplyRecorded(engine, alpha, _lastPoses, new[] { _redFigure.Height, _blueFigure.Height });
            bool recorded = _choreographer.Recorded != null;
            float poseTime = (engine.Tick + alpha) / BoutClock.TicksPerSecond;
            if (recorded)
            {
                var source = _choreographer.Recorded.Value;
                redPose = source.Red;
                bluePose = source.Blue;
            }

            if (!recorded && redAnchor.Weight <= 0f && blueAnchor.Weight <= 0f)
            {
                // Held bodies are in contact by construction; only free bodies need separating.
                BodyContact.Separate(ref redPose, _redFigure.Body, _shown[0].Ground, ref bluePose, _blueFigure.Body, _shown[1].Ground);
            }
            Pose redBody = redPose;
            Pose blueBody = bluePose;
            if (!recorded)
            {
                // Two passes: grips on the opponent's arms (wrist control, two-on-one, overhooks) aim at
                // where those arms are once placed, not where they hang before the arms are solved.
                PoseSolver.SolveArms(ref redPose, _redFigure.Body, redRole, _shown[0], in blueBody, engine.Grips.Of(Corner.Red));
                PoseSolver.SolveArms(ref bluePose, _blueFigure.Body, blueRole, _shown[1], in redBody, engine.Grips.Of(Corner.Blue));
                Pose redArms = redPose;
                Pose blueArms = bluePose;
                PoseSolver.SolveArms(ref redPose, _redFigure.Body, redRole, _shown[0], in blueArms, engine.Grips.Of(Corner.Red));
                PoseSolver.SolveArms(ref bluePose, _blueFigure.Body, blueRole, _shown[1], in redArms, engine.Grips.Of(Corner.Blue));
            }

            if (!recorded)
            {
                // A hand that holds aims where it physically holds, so the arm's muscles carry the
                // grip with the opponent instead of hauling them towards where the clip put the hand.
                ReachHeldPoints(ref redPose, _redFigure.Body, Corner.Red);
                ReachHeldPoints(ref bluePose, _blueFigure.Body, Corner.Blue);
            }

            if (!recorded && _throwTechnique != null)
            {
                // Throughout a throw the thrower's hands stay on the grip points of the opponent's actual body.
                Corner thrower = _thrown.Opponent();
                if (thrower == Corner.Red)
                {
                    TrapLimbs(ref bluePose, _blueFigure.Body, in redPose, _redFigure.Body);
                    HoldGrips(ref redPose, _redFigure.Body, _blueFigure.Physical);
                }
                else
                {
                    TrapLimbs(ref redPose, _redFigure.Body, in bluePose, _blueFigure.Body);
                    HoldGrips(ref bluePose, _blueFigure.Body, _redFigure.Physical);
                }
            }

            if (!recorded && !snapped && deltaTime > 0f && PhysicalCompliance)
            {
                // The bodies give under what they bore last frame; a body held on the other goes down with its carrier.
                bool engaged = engine.ActiveTechnique != null && !engine.IsStopped;
                Comply(Corner.Red, ref redPose, _redFigure, engaged, deltaTime);
                Comply(Corner.Blue, ref bluePose, _blueFigure, engaged, deltaTime);
                PoseSolver.Translate(ref redPose, Vector3.down * (_compliance[1].Sink * Mathf.Clamp01(redAnchor.Weight)));
                PoseSolver.Translate(ref bluePose, Vector3.down * (_compliance[0].Sink * Mathf.Clamp01(blueAnchor.Weight)));
            }
            else
            {
                _compliance[0].Reset();
                _compliance[1].Reset();
            }

            if (!recorded && !snapped && deltaTime > 0f)
            {
                PlantFeet(ref redPose, _redFigure);
                PlantFeet(ref bluePose, _blueFigure);
            }

            var future = _choreographer.RecordedNext;
            Pose nextRed = future.HasValue ? future.Value.Red : redPose;
            Pose nextBlue = future.HasValue ? future.Value.Blue : bluePose;
            var transitioned = _poseTransitions.Sample(engine.ActiveTechnique, engine.ActiveTechnique?.PhaseIndex ?? -1,
                recorded, poseTime, in redPose, in bluePose, future.HasValue, in nextRed, in nextBlue,
                future?.Seconds ?? 0f, snapped);
            redPose = transitioned.Red;
            bluePose = transitioned.Blue;
            bool fitTargets = recorded || _poseTransitions.IsTransitioning;
            _recordedLegs[0] = fitTargets ? _redFigure.Rig.RetargetRecordedLegs(ref redPose, true) : (CharacterRig.RecordedLegsResult?)null;
            _recordedLegs[1] = fitTargets ? _blueFigure.Rig.RetargetRecordedLegs(ref bluePose, true) : (CharacterRig.RecordedLegsResult?)null;

            SolveExpressions(engine, ref redPose, ref bluePose);
            _lastPoses[0] = redPose;
            _lastPoses[1] = bluePose;
            if (snapped)
            {
                _redFigure.Rig.ResetKneeRollHistory();
                _blueFigure.Rig.ResetKneeRollHistory();
            }
            _redFigure.Apply(in redPose, GripOn(engine, Corner.Red, GripSide.Left), GripOn(engine, Corner.Red, GripSide.Right), fitTargets);
            _blueFigure.Apply(in bluePose, GripOn(engine, Corner.Blue, GripSide.Left), GripOn(engine, Corner.Blue, GripSide.Right), fitTargets);
            if (fitTargets)
            {
                if (!snapped) _handGrips.Update(engine, _redFigure.Physical, _blueFigure.Physical, _throwTechnique != null);
                _recordedGrips.Apply(engine, _redFigure, _blueFigure, _handGrips, deltaTime, snapped);
            }
            else
                _recordedGrips.Reset();
            RecordedContacts = recorded ? RecordedContactConstraints.Apply(_choreographer.RecordedContacts,
                _redFigure, _blueFigure, _choreographer.PlaceContactPoint, _choreographer.RecordedMirrored, engine) : default;
            SimulateFalls(deltaTime, snapped);

            // After physics, on the shown bones only: holding hands back on what they hold, posted hands flat on the mat.
            Reproject(Corner.Red, _redFigure, in redPose, engine);
            Reproject(Corner.Blue, _blueFigure, in bluePose, engine);
            if (recorded)
                RecordedContacts = RecordedContactConstraints.Apply(_choreographer.RecordedContacts,
                    _redFigure, _blueFigure, _choreographer.PlaceContactPoint, _choreographer.RecordedMirrored, engine);
            _redFigure.ApplyExpression(in redPose);
            _blueFigure.ApplyExpression(in bluePose);
            _mat.ShowContacts(_redFigure.Physical, _blueFigure.Physical);

            // Sweat and strain follow the stamina spent and the bout time gone.
            _redFigure.SetExertion(_exertion[0].Update(engine.Wrestler(Corner.Red), engine.Clock, deltaTime), deltaTime);
            _blueFigure.SetExertion(_exertion[1].Update(engine.Wrestler(Corner.Blue), engine.Clock, deltaTime), deltaTime);

            // Twist bones and corrective shapes follow the final bones, after posing, contact and physics.
            _redFigure.UpdateDeformation();
            _blueFigure.UpdateDeformation();

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
            Presented?.Invoke(this);
        }

        /// <summary>This frame's post-physics pass for each corner (see <see cref="PostPhysicsReprojector"/>).</summary>
        public PostPhysicsReprojector.Result Reprojected(Corner corner) => _reprojected[(int)corner];

        private readonly PostPhysicsReprojector.Result[] _reprojected = new PostPhysicsReprojector.Result[2];
        private readonly GripLink[] _holdsScratch = new GripLink[2];
        private readonly bool[] _freeScratch = new bool[2];

        private void Reproject(Corner corner, WrestlerCharacter figure, in Pose pose, BoutEngine engine)
        {
            for (int side = 0; side < 2; side++)
            {
                var hand = (GripSide)side;
                _holdsScratch[side] = HoldOf(corner, hand, figure);
                HandGripStyle style = hand == GripSide.Left ? pose.LeftGripStyle : pose.RightGripStyle;
                _freeScratch[side] = _holdsScratch[side] == null && style == HandGripStyle.OpenGuard && GripOn(engine, corner, hand) <= 0f;
            }

            _reprojected[(int)corner] = PostPhysicsReprojector.Apply(figure, _holdsScratch, _freeScratch, pose.ChestNormal);
        }

        /// <summary>The physical hold a hand is on: its engine grip's, or for a thrower the throw's hold on the thrown body.</summary>
        private GripLink HoldOf(Corner corner, GripSide side, WrestlerCharacter figure)
        {
            GripLink link = _handGrips.Link(corner, side);
            if (link != null || _throwTechnique == null || _thrown == corner)
            {
                return link;
            }

            WrestlerCharacter thrown = _thrown == Corner.Red ? _redFigure : _blueFigure;
            ArticulationBody hand = figure.Physical.Link(side == GripSide.Left ? "LeftHand" : "RightHand");
            foreach (GripLink hold in thrown.Physical.ThrowLinks)
            {
                if (hold.Holding && hold.Holder == hand)
                {
                    return hold;
                }
            }

            return null;
        }

        /// <summary>Gives a wrestler's pose under the loads their physical body measured; hands that hold a grip keep their hold.</summary>
        private void Comply(Corner corner, ref Pose pose, WrestlerCharacter figure, bool engaged, float deltaTime)
        {
            LoadCompliance compliance = _compliance[(int)corner];
            compliance.Update(figure.Physical, engaged, deltaTime);
            compliance.Apply(ref pose, figure.Body, Holds(corner, GripSide.Left), Holds(corner, GripSide.Right));
        }

        /// <summary>True when a hand is on a physical hold: an engine grip or, for a thrower, the throw's grip.</summary>
        private bool Holds(Corner corner, GripSide side)
        {
            if (_handGrips.HeldPoint(corner, side).HasValue)
            {
                return true;
            }

            if (_throwTechnique == null || _thrown == corner)
            {
                return false;
            }

            string arm = side == GripSide.Left ? "LeftForeArm" : "RightForeArm";
            foreach ((string holder, string _) in _throwGrips)
            {
                if (holder == arm)
                {
                    return true;
                }
            }

            return false;
        }

        private void SolveExpressions(BoutEngine engine, ref Pose redPose, ref Pose bluePose)
        {
            if (engine.IsFinished)
            {
                redPose.Focus = 0.25f;
                bluePose.Focus = 0.25f;
                redPose.Effort = 0.1f;
                bluePose.Effort = 0.1f;
                redPose.HasGazeTarget = false;
                bluePose.HasGazeTarget = false;
                return;
            }

            float gripLoad = Mathf.Clamp01(engine.Grips.All.Count * 0.25f);
            float baseFocus = 0.45f + (0.25f * gripLoad);
            float redEffort = 0.18f + (0.22f * gripLoad);
            float blueEffort = 0.18f + (0.22f * gripLoad);
            if (engine.Position == BoutPosition.Danger)
            {
                redEffort = Mathf.Max(redEffort, 0.85f);
                blueEffort = Mathf.Max(blueEffort, 0.85f);
                baseFocus = 0.85f;
            }
            else if (engine.Position == BoutPosition.ParTerre)
            {
                redEffort = Mathf.Max(redEffort, 0.55f);
                blueEffort = Mathf.Max(blueEffort, 0.55f);
                baseFocus = 0.72f;
            }

            TechniqueExecution active = engine.ActiveTechnique;
            if (active != null && !engine.IsStopped)
            {
                float progress = active.PhaseLength > 0 ? Mathf.Clamp01((float)active.PhaseTick / active.PhaseLength) : 1f;
                float burst = Mathf.Sin(progress * Mathf.PI);
                float executorEffort = 0.65f + (0.35f * burst);
                float defenderEffort = 0.55f + (0.35f * burst);
                if (active.Executor == Corner.Red)
                {
                    redEffort = Mathf.Max(redEffort, executorEffort);
                    blueEffort = Mathf.Max(blueEffort, defenderEffort);
                }
                else
                {
                    blueEffort = Mathf.Max(blueEffort, executorEffort);
                    redEffort = Mathf.Max(redEffort, defenderEffort);
                }

                baseFocus = Mathf.Max(baseFocus, 0.82f);
            }

            redPose.Focus = baseFocus;
            bluePose.Focus = baseFocus;
            redPose.Effort = redEffort;
            bluePose.Effort = blueEffort;
            redPose.GazeTarget = Vector3.Lerp(bluePose.Neck, bluePose.Head, 0.65f);
            bluePose.GazeTarget = Vector3.Lerp(redPose.Neck, redPose.Head, 0.65f);
            redPose.HasGazeTarget = true;
            bluePose.HasGazeTarget = true;
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
                _handGrips.Clear();
                for (int i = 0; i < 2; i++)
                {
                    _landingOffset[i] = Vector2.zero;
                    _followLanding[i] = false;
                    figures[i].Physical.Reset();
                    _previousAnchor[i] = _choreographer.Anchors[i].Weight;
                    _previousGround[i] = _shown[i].Ground;
                }

                PhysicalBody.Settle();
                _unstepped = 0f;
                _sinceTargets = 0f;
                return;
            }

            TechniqueExecution technique = _session.Engine.ActiveTechnique;
            if (technique != null)
            {
                _lastExecutor = technique.Executor;
            }

            for (int i = 0; i < 2; i++)
            {
                float anchor = _choreographer.Anchors[i].Weight;
                GroundState ground = _shown[i].Ground;
                bool released = _previousAnchor[i] >= 0.95f && anchor < 0.95f;
                bool wasUp = _previousGround[i] == GroundState.Standing || _previousGround[i] == GroundState.OneKnee;
                bool landedLying = ground == GroundState.Prone || ground == GroundState.Supine || ground == GroundState.Bridge;
                // Only the wrestler a move is done to loses control; the one doing it (a sprawl, a
                // dive, a lift set down) goes down on purpose, under their own muscles and balance.
                if (_lastExecutor == (Corner)i)
                {
                    released = false;
                    landedLying = false;
                }

                if (released && !figures[i].Physical.IsThrown)
                {
                    figures[i].Physical.Fall(0.9f);
                }
                else if (wasUp && landedLying)
                {
                    figures[i].Physical.Fall(0.6f);
                }

                _previousAnchor[i] = anchor;
                _previousGround[i] = ground;
            }

            // Recorded motion already carries the real throw; the physics drive is only for keyed clips.
            var throwing = _choreographer.Recorded != null ? null : _choreographer.Throw;
            UpdateThrow(throwing, figures);

            // Recorded poses retain the engine's physical grips; only keyed throw holds replace them.
            _handGrips.Update(_session.Engine, _redFigure.Physical, _blueFigure.Physical, _throwTechnique != null);
            for (int i = 0; i < 2; i++)
            {
                // A wrestler the clip holds on the other's body (a lift, a carry) is held up by them.
                float held = _choreographer.Recorded == null ? _choreographer.Anchors[i].Weight : 0f;
                figures[i].Physical.SetCarrier(held > 0f ? figures[1 - i].Physical : null, held);
            }

            _redFigure.Physical.SetBalance(_shown[0].Balance);
            _blueFigure.Physical.SetBalance(_shown[1].Balance);
            _unstepped += deltaTime;
            _sinceTargets += deltaTime;
            // The small allowance keeps a 60 fps frame at exactly four steps despite rounding.
            int steps = Mathf.Min(Mathf.FloorToInt((_unstepped / PhysicsStep) + 1e-3f), MaxStepsPerFrame);
            _unstepped = Mathf.Max(0f, _unstepped - (steps * PhysicsStep));
            if (steps == MaxStepsPerFrame)
            {
                _unstepped = Mathf.Min(_unstepped, PhysicsStep);
            }

            if (steps == 0)
            {
                // Too short a frame for a step: the bodies stay as they are until the next one.
                _redFigure.Physical.ShowPose();
                _blueFigure.Physical.ShowPose();
                return;
            }

            _redFigure.Physical.BeforeStep(_sinceTargets);
            _blueFigure.Physical.BeforeStep(_sinceTargets);
            _sinceTargets = 0f;
            const float substep = PhysicsStep;
            float stepped = steps * PhysicsStep;
            for (int s = 0; s < steps; s++)
            {
                _redFigure.Physical.Drive(substep);
                _blueFigure.Physical.Drive(substep);
                _handGrips.Apply(substep);
                if (throwing != null && _throwTechnique != null && !_turning)
                {
                    figures[(int)_thrown].Physical.DriveLoad();
                }

                if (throwing != null && _throwTechnique != null && _turning)
                {
                    var t = throwing.Value;
                    float progress = t.Progress + ((s + 0.5f) / steps * stepped / Mathf.Max(t.PhaseSeconds, 1e-3f));
                    (float angle, float rate) = ThrowPlan(t.Spec, progress, t.PhaseSeconds);
                    figures[(int)_thrown].Physical.DriveThrow(angle, rate, substep);
                }

                Physics.Simulate(substep);
            }

            _redFigure.Physical.AfterStep();
            _blueFigure.Physical.AfterStep();
        }

        /// <summary>
        /// Braces the thrower while a load phase is in its window, starts the thrown wrestler's physical
        /// flight when the throw phase reaches its window, and lets go when the throw phase ends or the
        /// technique stops (completed, failed or countered).
        /// </summary>
        private void UpdateThrow((TechniqueExecution Technique, Corner Thrower, ThrowSpec Spec, float Progress, float PhaseSeconds, bool Mirrored)? throwing, WrestlerCharacter[] figures)
        {
            // A throw keeps hold to the end of its phase: the turn is planned to finish at To, and the
            // thrower follows the opponent down to the mat before letting go.
            bool active = throwing != null && throwing.Value.Progress >= throwing.Value.Spec.From
                && throwing.Value.Progress < (throwing.Value.Spec.Kind == ThrowKind.Throw ? 1f : throwing.Value.Spec.To);
            if (_throwTechnique != null)
            {
                // A load carries on into its throw within the same technique; anything else lets go.
                bool same = throwing != null && ReferenceEquals(throwing.Value.Technique, _throwTechnique)
                    && (active
                        || (throwing.Value.Spec.Kind == ThrowKind.Load && throwing.Value.Progress >= throwing.Value.Spec.To)
                        || (throwing.Value.Spec.Kind == ThrowKind.Throw && throwing.Value.Progress < throwing.Value.Spec.From));
                if (!same)
                {
                    // Countered: the thrown wrestler is acting now, so they are loose only for a
                    // moment as they twist out, then carry out their counter. Otherwise they land.
                    TechniqueExecution current = _session.Engine.ActiveTechnique;
                    bool countered = (throwing != null && throwing.Value.Thrower == _thrown) || (current != null && current.Executor == _thrown);

                    figures[(int)_thrown].Physical.EndThrow(countered ? CounterReleaseSeconds : LandingSeconds);
                    // Where the throw actually puts the body is where it lands: the staging follows it down.
                    _followLanding[(int)_thrown] = !countered;

                    figures[1 - (int)_thrown].Physical.SetCarrying(false);
                    _throwGrips = new List<(string, string)>();
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
            var grips = new List<(string, string)>();
            foreach (ThrowGrip grip in t.Spec.Grips)
            {
                grips.Add((Mirror(grip.Thrower, t.Mirrored), Mirror(grip.Thrown, t.Mirrored)));
            }

            if (_throwTechnique == null)
            {
                _thrown = t.Thrower.Opponent();
                figures[(int)t.Thrower].Physical.SetCarrying(true);
                _throwGrips = grips;
                _throwTechnique = t.Technique;
                _turning = false;
                if (t.Spec.Pivot != HipFulcrum || t.Spec.Kind == ThrowKind.Throw)
                {
                    // Facing the opponent (chest or shoulders): the grips and squeeze draw them in
                    // physically from the start of the load.
                    figures[(int)_thrown].Physical.BeginThrow(figures[(int)t.Thrower].Physical, Mirror(t.Spec.Pivot, t.Mirrored), grips, ThrowStrength(_session.WrestlerOf(t.Thrower)), t.Spec.SeatUp);
                }

                // Turning the back in under them (hip fulcrum): the opponent keeps following their
                // animation, which holds them on the thrower's lower back with real contact. Released
                // as a free body now, they would be bulldozed aside by the thrower's hips sweeping in;
                // they become one when the turn starts, from where the load put them.
            }

            if (t.Spec.Kind == ThrowKind.Throw && !_turning)
            {
                if (!figures[(int)_thrown].Physical.IsThrown)
                {
                    figures[(int)_thrown].Physical.BeginThrow(figures[(int)t.Thrower].Physical, Mirror(t.Spec.Pivot, t.Mirrored), grips, ThrowStrength(_session.WrestlerOf(t.Thrower)), t.Spec.SeatUp);
                }

                figures[(int)_thrown].Physical.StartTurn(ThrowAxisFor(t, figures));
                PhysicalBody.Trace?.Invoke(System.FormattableString.Invariant($"turn start: keyed thrower {_shown[(int)t.Thrower].Position} heading {_shown[(int)t.Thrower].Facing}, keyed thrown {_shown[(int)_thrown].Position}; bodies: thrower hips {figures[(int)t.Thrower].Physical.HipPosition}, thrown hips {figures[(int)_thrown].Physical.HipPosition}; animated pelvis thrower {_lastPoses[(int)t.Thrower].Pelvis} thrown {_lastPoses[(int)_thrown].Pelvis}, thrower chest normal {_lastPoses[(int)t.Thrower].ChestNormal}, anchor weight {_choreographer.Anchors[(int)_thrown].Weight:F2}"));
                _throwGrips = grips;
                _turning = true;
            }

            _throwPhase = t.Technique.PhaseIndex;
        }

        /// <summary>Horizontal offset (m) from the fulcrum below which a throw over it takes its axis from the thrower's facing.</summary>
        private const float FulcrumOffsetForAxis = 0.12f;

        /// <summary>After a throw lets go, how long (s) the body falls freely and settles on the mat before recovering.</summary>
        private const float LandingSeconds = 1.2f;

        /// <summary>How long (s) a thrown wrestler who counters stays loose before following their counter.</summary>
        private const float CounterReleaseSeconds = 0.05f;

        /// <summary>The fulcrum of throws where the thrower turns their back in under the opponent.</summary>
        private const string HipFulcrum = "Hips";

        private Vector3 ThrowAxisFor((TechniqueExecution Technique, Corner Thrower, ThrowSpec Spec, float Progress, float PhaseSeconds, bool Mirrored) t, WrestlerCharacter[] figures)
        {
            Vector3 offset = figures[(int)t.Thrower.Opponent()].Physical.OffsetFromFulcrum;
            if (t.Spec.Axis == ThrowAxis.Over && offset.magnitude > FulcrumOffsetForAxis)
            {
                // Over the top of the fulcrum, away from the side the body is loaded on, whichever
                // way the thrower is facing: the axis is square to the line from the fulcrum to the
                // body's centre of mass.
                return Vector3.Cross(Vector3.up, -offset.normalized);
            }

            Vector3 forward = WrestlerFrame.OnMat(_shown[(int)t.Thrower].Facing).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            // ω = up × direction makes the body's top move along the direction (v = ω × r).
            // Over a fulcrum the body sits right on top of: the thrower's left-right axis, towards their front.
            return t.Spec.Axis != ThrowAxis.Forward
                ? Vector3.Cross(Vector3.up, forward)
                : Vector3.Cross(Vector3.up, right) * (t.Mirrored ? -1f : 1f);
        }

        /// <summary>
        /// Most force (newtons) a wrestler can put into a throw through the body: about 2 kN for the
        /// weakest to 5 kN for the strongest (3.5 kN at 50 upper- and lower-body strength).
        /// </summary>
        private static float ThrowStrength(Wrestling.Core.Wrestlers.Wrestler wrestler)
        {
            float strength = (wrestler.Attributes[Wrestling.Core.Wrestlers.WrestlerAttribute.UpperBodyStrength]
                + wrestler.Attributes[Wrestling.Core.Wrestlers.WrestlerAttribute.LowerBodyStrength]) * 0.5f;
            return 2000f + (30f * strength);
        }

        /// <summary>An animated ankle lower than this (m) is a foot meant to be planted on the mat.</summary>
        private const float PlantedAnkle = 0.16f;

        /// <summary>Hips further than this (m) from their animated place are not corrected for (the body has lost the motion).</summary>
        private const float PlantReach = 1.5f;

        /// <summary>
        /// Foot planting: where the physical hips are not where the animation has them (they cannot drop
        /// faster than gravity, or are held up or pushed), each planted leg is re-aimed so that, from
        /// the hips as they physically are, the foot still lands where the animation puts it. The legs
        /// fold or reach to keep the base instead of lifting the feet off the mat.
        /// </summary>
        private static void PlantFeet(ref Pose pose, WrestlerCharacter figure)
        {
            PhysicalBody body = figure.Physical;
            if (body.Weight <= 0f || body.IsThrown || body.IsFalling)
            {
                return;
            }

            BodyProportions proportions = figure.Body;
            bool hipsHigh = body.HipHeight > body.PelvisTarget.y + HighHips;
            PlantLeg(ref pose.HipL, ref pose.KneeL, ref pose.AnkleL, ref pose.ToeL, body.Link("LeftUpLeg").transform.position, body.Link("LeftFoot").transform.position, proportions, hipsHigh);
            PlantLeg(ref pose.HipR, ref pose.KneeR, ref pose.AnkleR, ref pose.ToeR, body.Link("RightUpLeg").transform.position, body.Link("RightFoot").transform.position, proportions, hipsHigh);
        }

        /// <summary>Hips this much (m) above their animated height are still coming down.</summary>
        private const float HighHips = 0.025f;

        private static void PlantLeg(ref Vector3 hip, ref Vector3 knee, ref Vector3 ankle, ref Vector3 toe, Vector3 physicalHip, Vector3 physicalAnkle, BodyProportions proportions, bool hipsHigh)
        {
            Vector3 offset = hip - physicalHip;
            if (ankle.y >= PlantedAnkle || offset.magnitude >= PlantReach)
            {
                return;
            }

            // From the hip as it physically is to where the foot should be: full vertical reach to the
            // mat, but clamped horizontally so a displaced pelvis plants its feet under itself instead
            // of pole-vaulting or doing the splits towards a distant target.
            Vector3 flatOffset = Vector3.ClampMagnitude(new Vector3(offset.x, 0f, offset.z), 0.22f);
            Vector3 reach = (ankle - hip) + new Vector3(flatOffset.x, offset.y, flatOffset.z);
            if (hipsHigh)
            {
                // Coming down: the leg may fold or swing, but not lengthen, or it would push the body
                // back up (a jump the wrestler is not making).
                float length = (physicalAnkle - physicalHip).magnitude;
                if (reach.magnitude > length)
                {
                    reach = reach.normalized * length;
                }
            }

            Vector3 pole = knee - Vector3.Lerp(hip, ankle, 0.5f);
            knee = PoseSolver.TwoBone(hip, hip + reach, proportions.Thigh, proportions.Shin, pole, out Vector3 reached);
            toe += reached - ankle;
            ankle = reached;
        }

        /// <summary>Re-solves each holding arm so its hand reaches the point it holds on the opponent's physical body (within the shoulder's reach cone).</summary>
        private void ReachHeldPoints(ref Pose pose, BodyProportions body, Corner corner)
        {
            Vector3? left = _handGrips.HeldPoint(corner, GripSide.Left);
            Vector3? right = _handGrips.HeldPoint(corner, GripSide.Right);
            PoseSolver.TwistForReach(ref pose, left, right);
            if (left.HasValue)
            {
                PoseSolver.ReachArm(ref pose, body, true, left.Value, pose.ElbowL - Vector3.Lerp(pose.ShoulderL, left.Value, 0.5f));
            }

            if (right.HasValue)
            {
                PoseSolver.ReachArm(ref pose, body, false, right.Value, pose.ElbowR - Vector3.Lerp(pose.ShoulderR, right.Value, 0.5f));
            }
        }

        /// <summary>
        /// Puts the thrower's hands on the grip points of the opponent's actual (physics) body. A
        /// thrower turned away from what they hold (a headlock thrown over the hip) turns the chest
        /// back towards it and draws the shoulder blade back; the arm itself stays inside the
        /// shoulder's reach cone.
        /// </summary>
        private void HoldGrips(ref Pose pose, BodyProportions body, PhysicalBody held)
        {
            Vector3? targetL = null;
            Vector3? targetR = null;
            Vector3? poleL = null;
            Vector3? poleR = null;
            foreach ((string holder, string point) in _throwGrips)
            {
                if (!holder.EndsWith("ForeArm", System.StringComparison.Ordinal))
                {
                    // Trapped against the body, not held in a hand.
                    continue;
                }

                bool left = holder.StartsWith("Left");
                Vector3 shoulder = left ? pose.ShoulderL : pose.ShoulderR;
                Vector3 outward = left ? pose.Left : -pose.Left;
                Vector3 target = held.GripPoint(point);
                Vector3? pole = null;
                if (point == "Head")
                {
                    // A headlock: the arm goes round the neck, the elbow up over the head and the hand
                    // coming out on the far side, so the forearm is across the back of the neck.
                    Vector3 neck = Vector3.Lerp(target, held.GripPoint("Spine2"), HeadlockNeckDrop);
                    Vector3 through = Vector3.ProjectOnPlane(neck - shoulder, Vector3.up);
                    target = neck + ((through.sqrMagnitude > 1e-6f ? through.normalized : outward) * HeadlockReach);
                    pole = (Vector3.up * 0.8f) + (outward * 0.4f);
                }

                if (left)
                {
                    targetL = target;
                    poleL = pole;
                }
                else
                {
                    targetR = target;
                    poleR = pole;
                }
            }

            PoseSolver.TwistForReach(ref pose, targetL, targetR);
            if (targetL.HasValue)
            {
                PoseSolver.ReachArm(ref pose, body, true, targetL.Value, poleL ?? PoseSolver.ElbowPole(in pose, true, targetL.Value));
            }

            if (targetR.HasValue)
            {
                PoseSolver.ReachArm(ref pose, body, false, targetR.Value, poleR ?? PoseSolver.ElbowPole(in pose, false, targetR.Value));
            }
        }

        /// <summary>
        /// Lays a trapped arm where the trap holds it: over the thrower's shoulder and across their
        /// chest, so the clamp is visible (and the physical body's muscles reach for the same place).
        /// </summary>
        private void TrapLimbs(ref Pose pose, BodyProportions body, in Pose thrower, BodyProportions throwerBody)
        {
            foreach ((string holder, string point) in _throwGrips)
            {
                if (holder.EndsWith("ForeArm", System.StringComparison.Ordinal) || !(point.EndsWith("Arm", System.StringComparison.Ordinal)))
                {
                    continue;
                }

                Vector3 chest = PoseSolver.AnchorOn(in thrower, throwerBody, AnchorPoint.Chest);
                PoseSolver.ReachArm(ref pose, body, point.StartsWith("Left"), chest, Vector3.up);
            }
        }

        /// <summary>How far down from the head's centre towards the chest a headlock wraps (share), and how far past the neck the hand comes out (m).</summary>
        private const float HeadlockNeckDrop = 0.35f;
        private const float HeadlockReach = 0.1f;

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
        /// its waist sits on the carrier's anatomical seat for the anchor (see
        /// <see cref="ClipKinematics.Seat"/>): the contact thickness out from the carrier's surface,
        /// along the surface's normal there, so the target never lies inside the carrier's body.
        /// The keyed lift is replaced by the hold in proportion to the anchor weight, so the bodies
        /// cannot separate.
        /// </summary>
        private static void Hold(ref Pose held, WrestlerFrame heldFrame, in Pose carrier, BodyProportions carrierBody, (AnchorPoint Point, float Weight, float Contact) anchor)
        {
            Vector3 point = PoseSolver.AnchorOn(in carrier, carrierBody, anchor.Point);
            Vector3 target = point + (PoseSolver.AnchorNormal(in carrier, anchor.Point) * anchor.Contact);
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
            figure.Physical.SetStrength(
                wrestler.Attributes[Wrestling.Core.Wrestlers.WrestlerAttribute.UpperBodyStrength],
                wrestler.Attributes[Wrestling.Core.Wrestlers.WrestlerAttribute.LowerBodyStrength]);
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
