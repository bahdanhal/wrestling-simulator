using System;
using System.Collections.Generic;
using Wrestling.Core.AI;
using Wrestling.Core.Match;
using Wrestling.Core.Numerics;
using Wrestling.Core.Rules;
using Wrestling.Core.Wrestlers;
using Wrestling.Data;

namespace Wrestling.Presentation
{
    /// <summary>
    /// Runs one bout for display: steps the engine at its fixed 60 Hz rate from real time and keeps
    /// the previous and current tick's frames so rendering can interpolate between them. Holds no
    /// gameplay logic of its own.
    /// </summary>
    public sealed class BoutSession
    {
        private readonly WrestlerFrame[] _previous = new WrestlerFrame[2];
        private readonly WrestlerFrame[] _current = new WrestlerFrame[2];
        private double _accumulator;
        private int _eventsSeen;

        public BoutSession(GameContent content, WrestlingStyle style, int level, ulong seed)
        {
            Content = content ?? throw new ArgumentNullException(nameof(content));
            Style = style;
            Seed = seed;
            var random = new Pcg32(seed, 54);
            Red = WrestlerGenerator.Generate("red", style, level, 12, content.Techniques, content.Names, random);
            Blue = WrestlerGenerator.Generate("blue", style, level, 12, content.Techniques, content.Names, random);
            Engine = new BoutEngine(new BoutSetup
            {
                Rules = content.Rules,
                Tuning = content.Tuning,
                Catalog = content.Techniques,
                Style = style,
                Red = Red,
                Blue = Blue,
                RedController = new AiController(Tactics.Default, seed * 2),
                BlueController = new AiController(Tactics.Default, (seed * 2) + 1),
                Seed = seed,
            });
            Capture();
            Array.Copy(_current, _previous, 2);
        }

        public GameContent Content { get; }

        public WrestlingStyle Style { get; }

        public ulong Seed { get; }

        public Wrestler Red { get; }

        public Wrestler Blue { get; }

        public BoutEngine Engine { get; }

        public Wrestler WrestlerOf(Corner corner) => corner == Corner.Red ? Red : Blue;

        /// <summary>Interpolation factor between the previous and current tick (0–1).</summary>
        public float Alpha => (float)(_accumulator * BoutClock.TicksPerSecond);

        /// <summary>Advances by real time; returns the number of engine ticks run.</summary>
        public int Advance(double seconds)
        {
            if (seconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(seconds));
            }

            const double tickLength = 1.0 / BoutClock.TicksPerSecond;
            const int maximumTicksPerCall = 60 * 8;
            _accumulator += seconds;
            int ticks = 0;
            while (_accumulator >= tickLength && !Engine.IsFinished && ticks < maximumTicksPerCall)
            {
                Step();
                _accumulator -= tickLength;
                ticks++;
            }

            if (Engine.IsFinished || ticks == maximumTicksPerCall)
            {
                _accumulator = Math.Min(_accumulator, tickLength);
            }

            return ticks;
        }

        /// <summary>Runs ticks without real time (fast-forward, screenshots).</summary>
        public void StepTicks(int count)
        {
            for (int i = 0; i < count && !Engine.IsFinished; i++)
            {
                Step();
            }

            _accumulator = 0;
        }

        public WrestlerFrame Frame(Corner corner, float alpha) =>
            WrestlerFrame.Lerp(_previous[(int)corner], _current[(int)corner], alpha);

        /// <summary>Events emitted since the last call.</summary>
        public IEnumerable<BoutEvent> TakeNewEvents()
        {
            IReadOnlyList<BoutEvent> events = Engine.Events;
            while (_eventsSeen < events.Count)
            {
                yield return events[_eventsSeen++];
            }
        }

        private void Step()
        {
            Array.Copy(_current, _previous, 2);
            Engine.Step();
            Capture();
        }

        private void Capture()
        {
            _current[(int)Corner.Red] = WrestlerFrame.Capture(Engine.Wrestler(Corner.Red).Body);
            _current[(int)Corner.Blue] = WrestlerFrame.Capture(Engine.Wrestler(Corner.Blue).Body);
        }
    }
}
