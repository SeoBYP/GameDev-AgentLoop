using System;
using NUnit.Framework;
using Roguelike.Core;

public class DungeonGridTests
{
    [Test]
    public void Constructor_Throws_When_Width_Below_Three()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DungeonGrid(2, 5, 1));
    }

    [Test]
    public void Constructor_Throws_When_Height_Below_Three()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DungeonGrid(5, 2, 1));
    }

    [Test]
    public void Same_Seed_Produces_Identical_Tiles()
    {
        var gridA = new DungeonGrid(20, 15, 42);
        var gridB = new DungeonGrid(20, 15, 42);

        for (int y = 0; y < gridA.Height; y++)
        {
            for (int x = 0; x < gridA.Width; x++)
            {
                Assert.AreEqual(gridA.TileAt(x, y), gridB.TileAt(x, y), $"Mismatch at ({x},{y})");
            }
        }
    }

    [Test]
    public void Different_Seeds_Produce_Different_Tiles()
    {
        var gridA = new DungeonGrid(20, 15, 1);
        var gridB = new DungeonGrid(20, 15, 2);

        bool foundDifference = false;
        for (int y = 0; y < gridA.Height && !foundDifference; y++)
        {
            for (int x = 0; x < gridA.Width && !foundDifference; x++)
            {
                if (gridA.TileAt(x, y) != gridB.TileAt(x, y))
                    foundDifference = true;
            }
        }

        Assert.IsTrue(foundDifference, "Expected different seeds to produce different tiles.");
    }

    [Test]
    public void Outer_Ring_Is_Always_Wall()
    {
        var grid = new DungeonGrid(10, 8, 7);

        for (int x = 0; x < grid.Width; x++)
        {
            Assert.AreEqual(TileType.Wall, grid.TileAt(x, 0));
            Assert.AreEqual(TileType.Wall, grid.TileAt(x, grid.Height - 1));
        }

        for (int y = 0; y < grid.Height; y++)
        {
            Assert.AreEqual(TileType.Wall, grid.TileAt(0, y));
            Assert.AreEqual(TileType.Wall, grid.TileAt(grid.Width - 1, y));
        }
    }

    [Test]
    public void At_Least_One_Floor_Tile_Exists_Even_On_Minimal_Grid()
    {
        var grid = new DungeonGrid(3, 3, 999);

        bool hasFloor = false;
        for (int y = 0; y < grid.Height && !hasFloor; y++)
        {
            for (int x = 0; x < grid.Width && !hasFloor; x++)
            {
                if (grid.TileAt(x, y) == TileType.Floor)
                    hasFloor = true;
            }
        }

        Assert.IsTrue(hasFloor, "Expected at least one floor tile.");
    }

    [Test]
    public void IsWalkable_Returns_False_For_OutOfBounds()
    {
        var grid = new DungeonGrid(5, 5, 3);

        Assert.IsFalse(grid.IsWalkable(-1, 0));
        Assert.IsFalse(grid.IsWalkable(0, -1));
        Assert.IsFalse(grid.IsWalkable(grid.Width, 0));
        Assert.IsFalse(grid.IsWalkable(0, grid.Height));
    }

    [Test]
    public void TileAt_Throws_For_OutOfBounds()
    {
        var grid = new DungeonGrid(5, 5, 3);

        Assert.Throws<ArgumentOutOfRangeException>(() => grid.TileAt(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.TileAt(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.TileAt(grid.Width, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.TileAt(0, grid.Height));
    }
}