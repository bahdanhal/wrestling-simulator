using UnityEngine;

namespace Wrestling.Presentation
{
    /// <summary>How a hand shapes its fingers and thumb on what it holds.</summary>
    public enum HandGripStyle
    {
        /// <summary>Free hand: athletic, slightly curved, ready to fight hands (flat when posted on the mat).</summary>
        OpenGuard,

        /// <summary>Cupping the back of the neck or head: fingers splayed and curved, thumb tucked to the index.</summary>
        CollarCup,

        /// <summary>Round a wrist or ankle: fingers wrap the limb, thumb opposed round the other side.</summary>
        WristCClamp,

        /// <summary>On the upper arm above the elbow: fingers hooked round the triceps, thumb partly opposed.</summary>
        TricepsHook,

        /// <summary>Hooked over a shoulder or round the neck: fingers bent as a hook, thumb alongside.</summary>
        HookWrap,

        /// <summary>Hands locked palm to palm (body lock, gut wrench, waist, lace): fingers hooked over the other hand.</summary>
        GableClasped,

        /// <summary>Round a thigh or behind a knee: long wrap of the fingers, thumb kept with them.</summary>
        LegWrap,
    }

    /// <summary>
    /// A grip's aperture: the radius of what the hand wraps, as a share of <see cref="MaxRadius"/>
    /// (0 a closed fist, 1 a thigh or wider).
    /// </summary>
    public static class GripAperture
    {
        /// <summary>Radius (m) of the widest thing a hand wraps (a thigh).</summary>
        public const float MaxRadius = 0.09f;

        public static float FromRadius(float radius) => Mathf.Clamp01(radius / MaxRadius);

        public static float ToRadius(float aperture) => Mathf.Clamp01(aperture) * MaxRadius;

        /// <summary>The aperture a style takes when none is given.</summary>
        public static float Default(HandGripStyle style)
        {
            switch (style)
            {
                case HandGripStyle.WristCClamp:
                    return 0.33f;
                case HandGripStyle.TricepsHook:
                    return 0.5f;
                case HandGripStyle.CollarCup:
                    return 0.75f;
                case HandGripStyle.HookWrap:
                    return 0.65f;
                case HandGripStyle.GableClasped:
                    return 0.3f;
                case HandGripStyle.LegWrap:
                    return 0.9f;
                default:
                    return 0.45f;
            }
        }
    }

    /// <summary>
    /// Where and how one hand holds the opponent: the wrist joint's position, the hand's direction
    /// (wrist to knuckles), the palm's normal (towards the held segment's centre), the hand shape
    /// and its aperture.
    /// </summary>
    public readonly struct GripSocket
    {
        public GripSocket(Vector3 wrist, Vector3 contact, Vector3 direction, Vector3 palm, HandGripStyle style, float aperture)
        {
            Wrist = wrist;
            Contact = contact;
            Direction = direction;
            Palm = palm;
            Style = style;
            Aperture = aperture;
        }

        /// <summary>Where the wrist joint goes (the arm's IK target).</summary>
        public Vector3 Wrist { get; }

        /// <summary>The point of the held body under the middle of the palm.</summary>
        public Vector3 Contact { get; }

        public Vector3 Direction { get; }

        public Vector3 Palm { get; }

        public HandGripStyle Style { get; }

        public float Aperture { get; }
    }
}
