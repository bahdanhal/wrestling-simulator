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
        private static readonly Color RedColour = new Color(0.82f, 0.12f, 0.15f);
        private static readonly Color BlueColour = new Color(0.10f, 0.30f, 0.82f);
        private static readonly Color Panel = new Color(0.05f, 0.065f, 0.095f, 0.92f);
        private static readonly Color HeaderBar = new Color(0.09f, 0.12f, 0.18f, 0.96f);
        private static readonly Color GoldAccent = new Color(0.96f, 0.76f, 0.20f, 1f);

        private readonly Queue<string> _feed = new Queue<string>();
        private Font _font;
        private Text _styleHeader;
        private Text _redCountry;
        private Text _blueCountry;
        private Text _redName;
        private Text _blueName;
        private Text _redPoints;
        private Text _bluePoints;
        private Text _redCautions;
        private Text _blueCautions;
        private Text _clock;
        private Text _period;
        private RectTransform _stateRect;
        private Text _position;
        private Text _action;
        private RectTransform _feedRect;
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

            RectTransform header = PanelAt(root, "Header", new Vector2(0f, 1f), new Vector2(36f, -24f), new Vector2(868f, 30f), HeaderBar);
            Image(header, "GoldTrim", new Vector2(0f, 0f), new Vector2(868f, 3f), GoldAccent);
            _styleHeader = Label(header, "Style", new Vector2(6f, -2f), new Vector2(856f, 26f), 18, TextAnchor.MiddleLeft, new Color(0.88f, 0.91f, 0.96f), FontStyle.Bold);

            RectTransform board = PanelAt(root, "Board", new Vector2(0f, 1f), new Vector2(36f, -56f), new Vector2(648f, 144f), Panel);
            Row(board, Corner.Red, 0f, out _redCountry, out _redName, out _redPoints, out _redCautions);
            Image(board, "Divider", new Vector2(14f, -71f), new Vector2(634f, 2f), new Color(1f, 1f, 1f, 0.08f));
            Row(board, Corner.Blue, -72f, out _blueCountry, out _blueName, out _bluePoints, out _blueCautions);

            RectTransform clock = PanelAt(root, "Clock", new Vector2(0f, 1f), new Vector2(692f, -56f), new Vector2(212f, 144f), Panel);
            Image(clock, "ClockAccent", new Vector2(0f, 0f), new Vector2(4f, 144f), GoldAccent);
            _clock = Label(clock, "Time", new Vector2(0f, -14f), new Vector2(212f, 76f), 58, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            _period = Label(clock, "Period", new Vector2(0f, -92f), new Vector2(212f, 38f), 22, TextAnchor.MiddleCenter, new Color(0.95f, 0.78f, 0.25f, 0.95f), FontStyle.Bold);

            _stateRect = PanelAt(root, "State", new Vector2(1f, 1f), new Vector2(-36f, -24f), new Vector2(460f, 96f), Panel);
            Image(_stateRect, "StateTopTrim", new Vector2(0f, 0f), new Vector2(460f, 3f), GoldAccent);
            _position = Label(_stateRect, "Position", new Vector2(0f, -8f), new Vector2(444f, 40f), 26, TextAnchor.MiddleRight, GoldAccent, FontStyle.Bold);
            _action = Label(_stateRect, "Action", new Vector2(0f, -48f), new Vector2(444f, 40f), 22, TextAnchor.MiddleRight, Color.white, FontStyle.Normal);

            _feedRect = PanelAt(root, "Feed", new Vector2(0f, 0f), new Vector2(36f, 28f + 144f), new Vector2(760f, 144f), new Color(0.04f, 0.055f, 0.085f, 0.84f));
            Image(_feedRect, "FeedTrim", new Vector2(0f, 0f), new Vector2(4f, 144f), new Color(0.35f, 0.65f, 1f, 0.85f));
            _feedText = Label(_feedRect, "Lines", new Vector2(6f, -10f), new Vector2(742f, 126f), 21, TextAnchor.UpperLeft, new Color(0.92f, 0.95f, 0.98f), FontStyle.Normal);

            // Lower-third championship result banner on the bottom-right, clear of both the wrestlers and the bottom-left commentary feed.
            RectTransform banner = PanelAt(root, "Result", new Vector2(1f, 0f), new Vector2(-36f, 28f + 144f), new Vector2(880f, 144f), new Color(0.04f, 0.055f, 0.085f, 0.96f));
            Image(banner, "ResultGoldTop", new Vector2(0f, 0f), new Vector2(880f, 4f), GoldAccent);
            _banner = Label(banner, "Text", new Vector2(0f, -10f), new Vector2(860f, 124f), 40, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            _bannerPanel = banner.gameObject;
            _bannerPanel.SetActive(false);
        }

        public void ResetFor(BoutSession session)
        {
            _feed.Clear();
            _feedText.text = string.Empty;
            _styleHeader.text = $"UWW OLYMPIC WRESTLING   ·   {StyleLabel(session.Style)}   ·   {Mathf.RoundToInt(session.Red.Body.NaturalWeightKg.ToFloat())} KG";
            _redCountry.text = session.Red.Country.ToUpperInvariant();
            _blueCountry.text = session.Blue.Country.ToUpperInvariant();
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
                _action.color = active.Executor == Corner.Red ? new Color(1f, 0.65f, 0.65f) : new Color(0.65f, 0.80f, 1f);
                _stateRect.sizeDelta = new Vector2(460f, 96f);
            }
            else
            {
                bool stopped = engine.IsStopped && !engine.IsFinished;
                _action.text = stopped ? "STOPPED" : string.Empty;
                _action.color = Color.white;
                _stateRect.sizeDelta = new Vector2(460f, stopped ? 96f : 56f);
            }

            if (engine.Result != null && !_bannerPanel.activeSelf)
            {
                BoutResult result = engine.Result;
                string winner = result.Winner == null ? "No winner" : Display(session, result.Winner.Value);
                _banner.text = $"{winner} wins\n<size=28>{result.RedPoints}–{result.BluePoints}  ·  {BoutCommentary.ResultLine(result, session.Content)}  ·  {result.Code}</size>";
                _banner.color = result.Winner == Corner.Red ? new Color(1f, 0.78f, 0.78f) : new Color(0.78f, 0.88f, 1f);
                _bannerPanel.SetActive(true);
            }
        }

        private static string StyleLabel(WrestlingStyle style) => style switch
        {
            WrestlingStyle.GrecoRoman => "GRECO-ROMAN",
            WrestlingStyle.Womens => "WOMEN'S WRESTLING",
            _ => "FREESTYLE",
        };

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

        private void Row(RectTransform board, Corner corner, float y, out Text country, out Text name, out Text points, out Text cautions)
        {
            Color colour = corner == Corner.Red ? RedColour : BlueColour;
            Image(board, "Bar", new Vector2(0f, y), new Vector2(14f, 72f), colour);
            Image(board, "CountryBadge", new Vector2(24f, y - 16f), new Vector2(68f, 40f), new Color(1f, 1f, 1f, 0.10f));
            country = Label(board, "Country", new Vector2(24f, y - 16f), new Vector2(68f, 40f), 20, TextAnchor.MiddleCenter, new Color(0.88f, 0.92f, 0.98f), FontStyle.Bold);
            name = Label(board, "Name", new Vector2(96f, y), new Vector2(316f, 72f), 34, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            cautions = Label(board, "Penalties", new Vector2(400f, y), new Vector2(130f, 72f), 22, TextAnchor.MiddleRight, new Color(1f, 0.85f, 0.3f), FontStyle.Bold);
            Image(board, "PointsBox", new Vector2(542f, y - 6f), new Vector2(98f, 60f), colour);
            points = Label(board, "Points", new Vector2(542f, y - 6f), new Vector2(98f, 60f), 46, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
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
