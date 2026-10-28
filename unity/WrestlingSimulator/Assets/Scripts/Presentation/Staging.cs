using UnityEngine;
using Wrestling.Core.Match;
using Wrestling.Core.Rules;

namespace Wrestling.Presentation
{
    /// <summary>
    /// Arranges the two figures for ground positions. The engine tracks who is on top and each
    /// body's state but not the exact relative placement on the ground, so the display puts the top
    /// wrestler at the bottom wrestler's side (par terre) or across the chest (danger position).
    /// Display only: nothing here feeds back into the simulation.
    /// </summary>
    public static class Staging
    {
        private const float ParTerreSideOffset = 0.42f;
        private const float ParTerreBackOffset = 0.18f;
        private const float DangerSideOffset = 0.40f;

        public static PoseRole RoleOf(Corner corner, BoutPosition position, Corner? top)
        {
            if (top == null || (position != BoutPosition.ParTerre && position != BoutPosition.Danger))
            {
                return PoseRole.Neutral;
            }

            return corner == top ? PoseRole.Top : PoseRole.Bottom;
        }

        /// <summary>
        /// Places the top wrestler beside the bottom one. <paramref name="viewDirection"/> is the
        /// camera's horizontal viewing direction on the mat: the top wrestler goes on the far side so
        /// neither figure hides the other.
        /// </summary>
        public static void Arrange(ref WrestlerFrame red, ref WrestlerFrame blue, BoutPosition position, Corner? top, Vector2 viewDirection)
        {
            if (top == null || (position != BoutPosition.ParTerre && position != BoutPosition.Danger))
            {
                return;
            }

            if (top == Corner.Red)
            {
                Arrange(ref red, ref blue, position, viewDirection);
            }
            else
            {
                Arrange(ref blue, ref red, position, viewDirection);
            }
        }

        private static void Arrange(ref WrestlerFrame upper, ref WrestlerFrame bottom, BoutPosition position, Vector2 viewDirection)
        {
            Vector2 forward = bottom.Facing.sqrMagnitude > 1e-6f ? bottom.Facing.normalized : Vector2.right;
            var side = new Vector2(-forward.y, forward.x);
            float sign = Vector2.Dot(side, viewDirection) >= 0f ? 1f : -1f;
            side *= sign;
            bottom.RollSide = sign;
            if (position == BoutPosition.ParTerre)
            {
                upper.Position = bottom.Position + (side * ParTerreSideOffset) - (forward * ParTerreBackOffset);
                upper.Facing = -side;
            }
            else
            {
                // Bottom on the back, turned towards their left: the top wrestler is across the chest on that side.
                upper.Position = bottom.Position + (side * DangerSideOffset) + (forward * 0.1f);
                upper.Facing = -side;
            }

            Vector2 across = new Vector2(-upper.Facing.y, upper.Facing.x) * 0.2f;
            upper.LeftFoot = upper.Position + across;
            upper.RightFoot = upper.Position - across;
        }
    }
}
