using System;
using UnityEngine;

namespace Wrestling.Presentation
{
    /// <summary>
    /// Athletic exertion on the face, as FACS action units baked into the body mesh at import
    /// (shape names and how strain, focus and fatigue drive them are defined once here): the
    /// brows drawn down and in (corrugator, AU4), the eyes narrowed from below (orbicularis oculi,
    /// AU6/7), the nostrils flared (dilator naris, AU38), the masseter bulging over a clenched jaw
    /// (AU31), the lips pressed together (orbicularis oris, AU24) and the mouth corners pulled
    /// back and down (risorius and platysma, AU20). Under load the jaw is clenched, never slack;
    /// it opens only to breathe hard in a pause after an exchange. Display only.
    /// </summary>
    public sealed class FacialEffort
    {
        public const string BrowFurrow = "Effort_BrowFurrow";
        public const string EyeSquint = "Effort_EyeSquint";
        public const string NostrilFlare = "Effort_NostrilFlare";
        public const string JawClench = "Effort_JawClench";
        public const string LipPress = "Effort_LipPress";
        public const string Grimace = "Effort_Grimace";

        /// <summary>Every effort shape, in the order <see cref="Weights"/> returns them.</summary>
        public static readonly string[] Shapes = { BrowFurrow, EyeSquint, NostrilFlare, JawClench, LipPress, Grimace };

        /// <summary>Jaw opening (degrees at the jaw bone): teeth together when clenched, lips just apart at rest, and the range of a hard breath.</summary>
        public const float ClenchedJaw = 1.5f;
        public const float StrainOnsetJaw = 3.5f;
        public const float RestJaw = 1.2f;
        public const float BreathMinJaw = 4f;
        public const float BreathMaxJaw = 6f;

        /// <summary>Breaths per second when spent, and when only a little tired.</summary>
        private const float SpentBreathRate = 0.75f;
        private const float TiredBreathRate = 0.45f;

        private const float Epsilon = 0.25f;

        private readonly SkinnedMeshRenderer _renderer;
        private readonly int[] _indices = new int[6];
        private readonly float[] _current = { -1f, -1f, -1f, -1f, -1f, -1f };
        private float _breath;

        /// <summary>Finds the effort shapes on the body mesh (the importer bakes them).</summary>
        public FacialEffort(SkinnedMeshRenderer renderer)
        {
            _renderer = renderer;
            Mesh mesh = renderer.sharedMesh;
            for (int i = 0; i < Shapes.Length; i++)
            {
                _indices[i] = mesh.GetBlendShapeIndex(Shapes[i]);
                if (_indices[i] < 0)
                {
                    throw new InvalidOperationException($"Body mesh has no facial effort shape '{Shapes[i]}'; re-import the character models.");
                }
            }
        }

        /// <summary>Phase of the current breath, 0–1.</summary>
        public float BreathPhase => _breath;

        /// <summary>Advances the breathing rhythm (faster the more spent the wrestler is).</summary>
        public void Breathe(float deltaTime, float fatigue)
        {
            float rate = Mathf.Lerp(TiredBreathRate, SpentBreathRate, Mathf.Clamp01(fatigue));
            _breath = Mathf.Repeat(_breath + (Mathf.Max(0f, deltaTime) * rate), 1f);
        }

        /// <summary>How clenched the face is under strain (0–1): from moderate effort the jaw sets.</summary>
        public static float Clench(float strain) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.65f, strain));

        /// <summary>How far the wrestler is breathing hard through an open mouth (0–1): tired, and between exertions.</summary>
        public static float Recovery(float strain, float fatigue) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, 0.7f, fatigue)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.35f, strain)));

        /// <summary>
        /// Jaw opening (degrees): at rest the lips are just apart; under strain the jaw clenches to a
        /// gritted-teeth gap; tired and between exertions it opens and closes with each breath
        /// (<paramref name="breath"/>, 0–1, is the breath's phase).
        /// </summary>
        public static float JawOpening(float focus, float strain, float fatigue, float breath)
        {
            float clench = Clench(strain);
            float rest = RestJaw + (0.8f * Mathf.Clamp01(focus));
            float set = Mathf.Lerp(StrainOnsetJaw, ClenchedJaw, clench);
            float jaw = Mathf.Lerp(rest, set, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 0.4f, strain)));
            float inhale = 0.5f - (0.5f * Mathf.Cos(breath * 2f * Mathf.PI));
            return Mathf.Lerp(jaw, Mathf.Lerp(BreathMinJaw, BreathMaxJaw, inhale), Recovery(strain, fatigue));
        }

        /// <summary>The six shape weights (0–1, in <see cref="Shapes"/> order) for a strain, focus and fatigue at a breath's phase.</summary>
        public static void Weights(float focus, float strain, float fatigue, float breath, float[] into)
        {
            focus = Mathf.Clamp01(focus);
            strain = Mathf.Clamp01(strain);
            float clench = Clench(strain);
            float recovery = Recovery(strain, fatigue);
            float inhale = 0.5f - (0.5f * Mathf.Cos(breath * 2f * Mathf.PI));
            float grimace = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 0.95f, strain));
            into[0] = Mathf.Clamp01((0.7f * focus) + (0.5f * strain));
            into[1] = Mathf.Clamp01((0.35f * focus) + (0.75f * clench));
            into[2] = Mathf.Clamp01((0.3f * strain) + (recovery * (0.55f + (0.45f * inhale))));
            into[3] = clench;
            into[4] = clench * (1f - (0.6f * grimace)) * (1f - recovery);
            into[5] = grimace;
        }

        private readonly float[] _weights = new float[6];

        /// <summary>Sets the shapes for this frame.</summary>
        public void Apply(float focus, float strain, float fatigue)
        {
            Weights(focus, strain, fatigue, _breath, _weights);
            for (int i = 0; i < _weights.Length; i++)
            {
                float weight = _weights[i] * 100f;
                if (Mathf.Abs(weight - _current[i]) > Epsilon)
                {
                    _current[i] = weight;
                    _renderer.SetBlendShapeWeight(_indices[i], weight);
                }
            }
        }
    }
}
