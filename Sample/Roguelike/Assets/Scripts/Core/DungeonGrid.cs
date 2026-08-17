using System;

namespace Roguelike.Core
{
    public enum TileType
    {
        Wall,
        Floor
    }

    public class DungeonGrid
    {
        private const double FloorChance = 0.55;

        private readonly TileType[] _tiles;

        public int Width { get; }
        public int Height { get; }

        public DungeonGrid(int width, int height, int seed)
        {
            if (width < 3)
                throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be at least 3.");
            if (height < 3)
                throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be at least 3.");

            Width = width;
            Height = height;
            _tiles = new TileType[width * height];

            Generate(seed);
        }

        public TileType TileAt(int x, int y)
        {
            if (x < 0 || x >= Width)
                throw new ArgumentOutOfRangeException(nameof(x), x, $"x must be between 0 and {Width - 1}.");
            if (y < 0 || y >= Height)
                throw new ArgumentOutOfRangeException(nameof(y), y, $"y must be between 0 and {Height - 1}.");

            return _tiles[y * Width + x];
        }

        public bool IsWalkable(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
                return false;

            return _tiles[y * Width + x] == TileType.Floor;
        }

        private void Generate(int seed)
        {
            var random = new Random(seed);

            for (int y = 0; y < Height; y++)
            {
                bool isRowBorder = y == 0 || y == Height - 1;

                for (int x = 0; x < Width; x++)
                {
                    bool isBorder = isRowBorder || x == 0 || x == Width - 1;
                    TileType tile = isBorder
                        ? TileType.Wall
                        : (random.NextDouble() < FloorChance ? TileType.Floor : TileType.Wall);

                    _tiles[y * Width + x] = tile;
                }
            }

            EnsureAtLeastOneFloorTile();
        }

        private void EnsureAtLeastOneFloorTile()
        {
            for (int i = 0; i < _tiles.Length; i++)
            {
                if (_tiles[i] == TileType.Floor)
                    return;
            }

            // Width/Height >= 3 guarantees this interior cell is never part of the border ring.
            int centerX = Width / 2;
            int centerY = Height / 2;
            _tiles[centerY * Width + centerX] = TileType.Floor;
        }
    }
}