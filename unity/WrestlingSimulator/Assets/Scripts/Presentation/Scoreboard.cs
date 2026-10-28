using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Wrestling.Core.Bouts;
using Wrestling.Core.Match;
using Wrestling.Core.Rules;
using Wrestling.Data.Localization;

namespace Wrestling.Presentation
{
    /// <summary>
    /// Broadcast scoreboard and play-by-play feed, built in code on a canvas. Reads the bout
    /// through <see cref="IBoutView"/> and the event log; never changes the bout.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public sealed class Scoreboard : MonoBehaviour
    {
        private const int FeedLines = 5;
        private static readonly Color RedColour = new Color(0.80f, 0.12f, 0.14f);
        private static readonly Color BlueColour = new Color(0.10f, 0.28f, 0.78f);
        private static readonly Color Panel = new Color(0.06f, 0.07f, 0.09f, 0.97f);

        private readonly Queue<string> _feed = new Queue<string>();
        private Font _font;
        private Text _redName;
        private Text _blueName;
        private Text _redPoints;
        private Text _bluePoints;
        private Text _redCautions;
        private Text _blueCautions;
        private Text _clock;
        private Text _period;
        private Text _position;
        private Text _action;
        private Text _feedText;
        private Text _banner;
        private GameObject _bannerPanel;

        public void Build(Font font)
        {
            _font = font;
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
                if (Application.isPlaying)
                {
                    Destroy(child);
                }
                else
                {
                    DestroyImmediate(child);
                }
            }

            var root = (RectTransform)transform;

            RectTransform board = PanelAt(root, "Board", new Vector2(0f, 1f), new Vector2(36f, -32f), new Vector2(640f, 156f), Panel);
            Row(board, Corner.Red, 0f, out _redName, out _redPoints, out _redCautions);
            Row(board, Corner.Blue, -78f, out _blueName, out _bluePoints, out _blueCautions);

            RectTransform clock = PanelAt(root, "Clock", new Vector2(0f, 1f), new Vector2(688f, -32f), new Vector2(220f, 156f), Panel);
            _clock = Label(clock, "Time", new Vector2(0f, -18f), new Vector2(220f, 80f), 64, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            _period = Label(clock, "Period", new Vector2(0f, -100f), new Vector2(220f, 40f), 26, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.7f), FontStyle.Normal);

            RectTransform state = PanelAt(root, "State", new Vector2(1f, 1f), new Vector2(-36f, -32f), new Vector2(520f, 110f), Panel);
            _position = Label(state, "Position", new Vector2(0f, -10f), new Vector2(500f, 44f), 30, TextAnchor.MiddleRight, new Color(1f, 0.75f, 0.2f), FontStyle.Bold);
            _action = Label(state, "Action", new Vector2(0f, -56f), new Vector2(500f, 44f), 26, TextAnchor.MiddleRight, Color.white, FontStyle.Normal);

            RectTransform feed = PanelAt(root, "Feed", new Vector2(0f, 0f), new Vector2(36f, 36f + 176f), new Vector2(900f, 176f), Panel);
            _feedText = Label(feed, "Lines", new Vector2(0f, -12f), new Vector2(880f, 160f), 26, TextAnchor.UpperLeft, Color.white, FontStyle.Normal);

            RectTransform banner = PanelAt(root, "Result", new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(1120f, 180f), new Color(0.04f, 0.05f, 0.06f, 1f));
            _banner = Label(banner, "Text", new Vector2(0f, -10f), new Vector2(1100f, 160f), 48, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            _bannerPanel = banner.gameObject;
            _bannerPanel.SetActive(false);
        }

        public void ResetFor(BoutSession session)
        {
            _feed.Clear();
            _feedText.text = string.Empty;
            _redName.text = Display(session, Corner.Red);
            _blueName.text = Display(session, Corner.Blue);
            _bannerPanel.SetActive(false);
            Refresh(session);
        }

        public void Push(BoutEvent e, BoutSession session)
        {
            if (!BoutCommentary.IsNotable(e))
            {
                return;
            }

            _feed.Enqueue($"{BoutCommentary.Clock(e.Time, session.Engine.Clock)}   {BoutCommentary.Describe(e, session.Content, session.Red, session.Blue)}");
            while (_feed.Count > FeedLines)
            {
                _feed.Dequeue();
            }

            _feedText.text = string.Join("\n", _feed);
        }

        public void Refresh(BoutSession session)
        {
            BoutEngine engine = session.Engine;
            BoutScoreSheet sheet = engine.Sheet;
            _redPoints.text = sheet.Points(Corner.Red).ToString();
            _bluePoints.text = sheet.Points(Corner.Blue).ToString();
            _redCautions.text = Penalties(sheet, Corner.Red);
            _blueCautions.text = Penalties(sheet, Corner.Blue);

            BoutClock clock = engine.Clock;
            int seconds = clock.TickInPeriod / BoutClock.TicksPerSecond;
            _clock.text = $"{seconds / 60}:{seconds % 60:00}";
            _period.text = clock.Phase == ClockPhase.Break ? "BREAK" : $"PERIOD {clock.Period}";
            _position.text = PositionLabel(engine);

            TechniqueExecution active = engine.ActiveTechnique;
            if (active != null)
            {
                string phase = active.Definition.Phases[Mathf.Clamp(active.PhaseIndex, 0, active.Definition.Phases.Count - 1)].Id;
                string who = active.Executor == Corner.Red ? session.Red.Name.Family : session.Blue.Name.Family;
                _action.text = $"{who}: {BoutCommentary.TechniqueName(active.Definition.Id, session.Content)} ({phase})";
                _action.color = active.Executor == Corner.Red ? new Color(1f, 0.6f, 0.6f) : new Color(0.6f, 0.75f, 1f);
            }
            else
            {
                _action.text = engine.IsStopped && !engine.IsFinished ? "Stopped" : string.Empty;
                _action.color = Color.white;
            }

            if (engine.Result != null && !_bannerPanel.activeSelf)
            {
                BoutResult result = engine.Result;
                string winner = result.Winner == null ? "No winner" : Display(session, result.Winner.Value);
                _banner.text = $"{winner} wins\n<size=32>{result.RedPoints}–{result.BluePoints}  ·  {BoutCommentary.ResultLine(result, session.Content)}  ·  {result.Code}</size>";
                _banner.color = result.Winner == Corner.Red ? new Color(1f, 0.75f, 0.75f) : new Color(0.75f, 0.85f, 1f);
                _bannerPanel.SetActive(true);
            }
        }

        private static string Display(BoutSession session, Corner corner)
        {
            var wrestler = session.WrestlerOf(corner);
            return $"{wrestler.Name.Given.ToUpperInvariant()[0]}. {wrestler.Name.Family.ToUpperInvariant()}";
        }

        private static string Penalties(BoutScoreSheet sheet, Corner corner)
        {
            int cautions = sheet.Cautions(corner);
            int passivity = sheet.PassivityCallsAgainst(corner);
            var parts = new List<string>();
            if (cautions > 0)
            {
                parts.Add($"{cautions}× C");
            }

            if (passivity > 0)
            {
                parts.Add($"{passivity}× P");
            }

            return string.Join("  ", parts);
        }

        private static string PositionLabel(BoutEngine engine)
        {
            if (engine.IsFinished)
            {
                return "FINAL";
            }

            switch (engine.Position)
            {
                case BoutPosition.ParTerre:
                    return "PAR TERRE";
                case BoutPosition.Danger:
                    return "DANGER POSITION";
                case BoutPosition.Scramble:
                    return "SCRAMBLE";
                default:
                    return "STANDING";
            }
        }

        private void Row(RectTransform board, Corner corner, float y, out Text name, out Text points, out Text cautions)
        {
            Color colour = corner == Corner.Red ? RedColour : BlueColour;
            Image(board, "Bar", new Vector2(0f, y), new Vector2(14f, 78f), colour);
            name = Label(board, "Name", new Vector2(34f, y), new Vector2(380f, 78f), 38, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            cautions = Label(board, "Penalties", new Vector2(390f, y), new Vector2(120f, 78f), 24, TextAnchor.MiddleRight, new Color(1f, 0.85f, 0.3f), FontStyle.Normal);
            Image(board, "PointsBox", new Vector2(530f, y - 6f), new Vector2(100f, 66f), colour);
            points = Label(board, "Points", new Vector2(530f, y - 6f), new Vector2(100f, 66f), 50, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
        }

        private static RectTransform PanelAt(RectTransform parent, string panelName, Vector2 anchor, Vector2 position, Vector2 size, Color colour)
        {
            var panel = new GameObject(panelName, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)panel.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(anchor.x, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            panel.GetComponent<Image>().color = colour;
            return rect;
        }

        private static void Image(RectTransform parent, string imageName, Vector2 position, Vector2 size, Color colour)
        {
            var image = new GameObject(imageName, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)image.transform;
            rect.SetParent(parent, false);
            TopLeft(rect, position, size);
            image.GetComponent<Image>().color = colour;
        }

        private Text Label(RectTransform parent, string labelName, Vector2 position, Vector2 size, int fontSize, TextAnchor alignment, Color colour, FontStyle style)
        {
            var label = new GameObject(labelName, typeof(RectTransform), typeof(Text));
            var rect = (RectTransform)label.transform;
            rect.SetParent(parent, false);
            TopLeft(rect, position + new Vector2(10f, 0f), size - new Vector2(20f, 0f));
            Text text = label.GetComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = colour;
            text.fontStyle = style;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static void TopLeft(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
