using UnityEngine;
using Wrestling.Core.Match;

namespace Wrestling.Presentation
{
    /// <summary>
    /// How worked a wrestler looks (0 fresh, 1 spent), for sweat and strain on the skin: it builds with
    /// the stamina they have spent and the bout time gone, and fades only slowly when they recover,
    /// the way sweat stays on. Read from the engine's state; display only.
    /// </summary>
    public sealed class Exertion
    {
        /// <summary>Exertion at the first whistle (warmed up), and what the whole bout's time adds.</summary>
        private const float WarmedUp = 0.08f;
        private const float BoutShare = 0.45f;

        /// <summary>What spent stamina adds (saturating; spent is in whole stamina bars), and how fast it saturates.</summary>
        private const float SpentShare = 0.5f;
        private const float SpentRate = 1.4f;

        /// <summary>What being tired now adds (1 minus the stamina share left).</summary>
        private const float TiredShare = 0.2f;

        /// <summary>Time constants (s) for exertion rising and fading.</summary>
        private const float RiseSeconds = 6f;
        private const float FadeSeconds = 90f;

        private float _spent;
        private float _lastStamina = -1f;

        public float Value { get; private set; }

        public void Reset()
        {
            _spent = 0f;
            _lastStamina = -1f;
            Value = 0f;
        }

        /// <summary>
        /// Advances with the wrestler's stamina and the bout clock. With no elapsed time (a jump in the
        /// bout, a snapshot) it takes the value the bout so far implies at once.
        /// </summary>
        public float Update(WrestlerRuntime wrestler, BoutClock clock, float deltaTime)
        {
            float stamina = wrestler.StaminaFraction.ToFloat();
            if (_lastStamina >= 0f)
            {
                _spent += Mathf.Max(0f, _lastStamina - stamina);
            }

            // Whatever has been spent, at least what is missing now has been.
            _spent = Mathf.Max(_spent, 1f - stamina);
            _lastStamina = stamina;

            int boutTicks = Mathf.Max(1, clock.Periods * clock.PeriodTicks);
            float bout = Mathf.Clamp01((float)clock.ElapsedTicks / boutTicks);
            float target = Mathf.Clamp01(WarmedUp + (BoutShare * bout) + (SpentShare * (1f - Mathf.Exp(-SpentRate * _spent))) + (TiredShare * (1f - stamina)));
            if (deltaTime <= 0f)
            {
                Value = target;
                return Value;
            }

            float seconds = target > Value ? RiseSeconds : FadeSeconds;
            Value = Mathf.Lerp(target, Value, Mathf.Exp(-deltaTime / seconds));
            return Value;
        }
    }
}
