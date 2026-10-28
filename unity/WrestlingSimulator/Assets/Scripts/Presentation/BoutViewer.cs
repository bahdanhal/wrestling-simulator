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
            _session.StepTicks(ticks);
            foreach (BoutEvent e in _session.TakeNewEvents())
            {
                _scoreboard.Push(e, _session);
            }

            _snapNextFrame = true;
            Present(1f, 0f);
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
            _choreographer.Apply(engine, ref red, ref blue, alpha);
            Smooth(0, red, deltaTime);
            Smooth(1, blue, deltaTime);
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
