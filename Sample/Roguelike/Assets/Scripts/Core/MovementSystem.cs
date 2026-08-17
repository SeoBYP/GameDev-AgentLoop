using System;

namespace Roguelike.Core
{
    public class MovementSystem
    {
        private readonly DungeonGrid _grid;

        public MovementSystem(DungeonGrid grid)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        }

        public bool TryMove(Actor actor, int dx, int dy)
        {
            if (actor == null) throw new ArgumentNullException(nameof(actor));
            if (dx < -1 || dx > 1) throw new ArgumentOutOfRangeException(nameof(dx));
            if (dy < -1 || dy > 1) throw new ArgumentOutOfRangeException(nameof(dy));

            if (!actor.IsAlive) return false;

            int newX = actor.X + dx;
            int newY = actor.Y + dy;

            if (newX < 0 || newX >= _grid.Width || newY < 0 || newY >= _grid.Height) return false;
            if (!_grid.IsWalkable(newX, newY)) return false;

            actor.MoveTo(newX, newY);
            return true;
        }
    }
}