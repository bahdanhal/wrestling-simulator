using UnityEngine;
using Wrestling.Core.Match;
using Wrestling.Core.Numerics;

namespace Wrestling.Presentation
{
    /// <summary>
    /// One wrestler's body state converted to floats for rendering. The engine's mat plane (x, y)
    /// maps to Unity's (x, z).
    /// </summary>
    public struct WrestlerFrame
    {
        public Vector2 Position;
        public Vector2 Facing;
        public Vector2 LeftFoot;
        public Vector2 RightFoot;
        public float Level;
        public float Posture;
        public float Balance;
        public float TorsoRotation;
        public GroundState Ground;
        public Stance Stance;

        /// <summary>Which side a lying wrestler rolls towards: +1 their left, -1 their right (display staging).</summary>
        public float RollSide;

        public static WrestlerFrame Capture(BodyState body)
        {
            (FixVector2 left, FixVector2 right) = body.FootPositions();
            return new WrestlerFrame
            {
                Position = ToVector(body.Position),
                Facing = ToVector(body.Facing),
                LeftFoot = ToVector(left),
                RightFoot = ToVector(right),
                Level = body.Level.ToFloat(),
                Posture = body.Posture.ToFloat(),
                Balance = body.Balance.ToFloat(),
                TorsoRotation = body.TorsoRotationDegrees.ToFloat(),
                Ground = body.Ground,
                Stance = body.Stance,
                RollSide = 1f,
            };
        }

        /// <summary>Blends continuous values; discrete states switch at the halfway point.</summary>
        public static WrestlerFrame Lerp(WrestlerFrame a, WrestlerFrame b, float t)
        {
            Vector2 facing = Vector2.Lerp(a.Facing, b.Facing, t);
            return new WrestlerFrame
            {
                Position = Vector2.Lerp(a.Position, b.Position, t),
                Facing = facing.sqrMagnitude > 1e-6f ? facing.normalized : b.Facing,
                LeftFoot = Vector2.Lerp(a.LeftFoot, b.LeftFoot, t),
                RightFoot = Vector2.Lerp(a.RightFoot, b.RightFoot, t),
                Level = Mathf.Lerp(a.Level, b.Level, t),
                Posture = Mathf.Lerp(a.Posture, b.Posture, t),
                Balance = Mathf.Lerp(a.Balance, b.Balance, t),
                TorsoRotation = Mathf.Lerp(a.TorsoRotation, b.TorsoRotation, t),
                Ground = t < 0.5f ? a.Ground : b.Ground,
                Stance = t < 0.5f ? a.Stance : b.Stance,
                RollSide = b.RollSide,
            };
        }

        public static Vector2 ToVector(FixVector2 v) => new Vector2(v.X.ToFloat(), v.Y.ToFloat());

        public static Vector3 OnMat(Vector2 v, float height = 0f) => new Vector3(v.x, height, v.y);
    }
}
