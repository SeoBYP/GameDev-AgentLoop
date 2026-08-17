using System;
using NUnit.Framework;
using Roguelike.Core;

public class MovementSystemTests
{
    private static readonly int[] Deltas = { -1, 0, 1 };

    [Test]
    public void Constructor_Rejects_Null_Grid()
    {
        Assert.Throws<ArgumentNullException>(() => new MovementSystem(null));
    }

    [Test]
    public void TryMove_Rejects_Null_Actor()
    {
        var grid = new DungeonGrid(10, 10, 1);
        var system = new MovementSystem(grid);
        Assert.Throws<ArgumentNullException>(() => system.TryMove(null, 0, 1));
    }

    [Test]
    public void TryMove_Rejects_Dx_Out_Of_Range()
    {
        var grid = new DungeonGrid(10, 10, 1);
        var system = new MovementSystem(grid);
        var actor = new Actor("Hero", 0, 0, 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => system.TryMove(actor, 2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => system.TryMove(actor, -2, 0));
    }

    [Test]
    public void TryMove_Rejects_Dy_Out_Of_Range()
    {
        var grid = new DungeonGrid(10, 10, 1);
        var system = new MovementSystem(grid);
        var actor = new Actor("Hero", 0, 0, 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => system.TryMove(actor, 0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => system.TryMove(actor, 0, -2));
    }

    [Test]
    public void TryMove_Off_Left_Edge_Returns_False_And_Leaves_Actor()
    {
        var grid = new DungeonGrid(10, 10, 1);
        var system = new MovementSystem(grid);
        var actor = new Actor("Hero", 0, 0, 10);

        bool moved = system.TryMove(actor, -1, 0);

        Assert.IsFalse(moved);
        Assert.AreEqual(0, actor.X);
        Assert.AreEqual(0, actor.Y);
    }

    [Test]
    public void TryMove_Off_Right_Edge_Returns_False_And_Leaves_Actor()
    {
        var grid = new DungeonGrid(10, 10, 1);
        var system = new MovementSystem(grid);
        var actor = new Actor("Hero", grid.Width - 1, 5, 10);

        bool moved = system.TryMove(actor, 1, 0);

        Assert.IsFalse(moved);
        Assert.AreEqual(grid.Width - 1, actor.X);
        Assert.AreEqual(5, actor.Y);
    }

    [Test]
    public void TryMove_Dead_Actor_Never_Moves()
    {
        var grid = new DungeonGrid(20, 20, 7);
        Assert.IsTrue(TryFindWalkableTile(grid, out int x, out int y), "Test grid produced no walkable tile.");

        var system = new MovementSystem(grid);
        var actor = new Actor("Hero", x, y, 10);
        actor.TakeDamage(10);
        Assert.IsFalse(actor.IsAlive);

        bool moved = system.TryMove(actor, 1, 0);

        Assert.IsFalse(moved);
        Assert.AreEqual(x, actor.X);
        Assert.AreEqual(y, actor.Y);
    }

    [Test]
    public void TryMove_To_Wall_Returns_False_And_Leaves_Actor()
    {
        var grid = new DungeonGrid(30, 30, 11);
        Assert.IsTrue(TryFindFloorAdjacentToWall(grid, out int fx, out int fy, out int dx, out int dy),
            "Test grid produced no floor tile adjacent to a wall.");

        var system = new MovementSystem(grid);
        var actor = new Actor("Hero", fx, fy, 10);

        bool moved = system.TryMove(actor, dx, dy);

        Assert.IsFalse(moved);
        Assert.AreEqual(fx, actor.X);
        Assert.AreEqual(fy, actor.Y);
    }

    [Test]
    public void TryMove_To_Floor_Returns_True_And_Updates_Position()
    {
        var grid = new DungeonGrid(30, 30, 11);
        Assert.IsTrue(TryFindAdjacentFloorPair(grid, out int fx, out int fy, out int dx, out int dy),
            "Test grid produced no pair of adjacent floor tiles.");

        var system = new MovementSystem(grid);
        var actor = new Actor("Hero", fx, fy, 10);

        bool moved = system.TryMove(actor, dx, dy);

        Assert.IsTrue(moved);
        Assert.AreEqual(fx + dx, actor.X);
        Assert.AreEqual(fy + dy, actor.Y);
    }

    private static bool TryFindWalkableTile(DungeonGrid grid, out int x, out int y)
    {
        for (int gx = 0; gx < grid.Width; gx++)
        {
            for (int gy = 0; gy < grid.Height; gy++)
            {
                if (grid.IsWalkable(gx, gy))
                {
                    x = gx;
                    y = gy;
                    return true;
                }
            }
        }

        x = 0;
        y = 0;
        return false;
    }

    private static bool TryFindFloorAdjacentToWall(DungeonGrid grid, out int fx, out int fy, out int dx, out int dy)
    {
        for (int x = 0; x < grid.Width; x++)
        {
            for (int y = 0; y < grid.Height; y++)
            {
                if (!grid.IsWalkable(x, y)) continue;

                foreach (int ddx in Deltas)
                {
                    foreach (int ddy in Deltas)
                    {
                        if (ddx == 0 && ddy == 0) continue;
                        int nx = x + ddx;
                        int ny = y + ddy;
                        if (nx < 0 || nx >= grid.Width || ny < 0 || ny >= grid.Height) continue;

                        if (!grid.IsWalkable(nx, ny))
                        {
                            fx = x;
                            fy = y;
                            dx = ddx;
                            dy = ddy;
                            return true;
                        }
                    }
                }
            }
        }

        fx = fy = dx = dy = 0;
        return false;
    }

    private static bool TryFindAdjacentFloorPair(DungeonGrid grid, out int fx, out int fy, out int dx, out int dy)
    {
        for (int x = 0; x < grid.Width; x++)
        {
            for (int y = 0; y < grid.Height; y++)
            {
                if (!grid.IsWalkable(x, y)) continue;

                foreach (int ddx in Deltas)
                {
                    foreach (int ddy in Deltas)
                    {
                        if (ddx == 0 && ddy == 0) continue;
                        int nx = x + ddx;
                        int ny = y + ddy;
                        if (nx < 0 || nx >= grid.Width || ny < 0 || ny >= grid.Height) continue;

                        if (grid.IsWalkable(nx, ny))
                        {
                            fx = x;
                            fy = y;
                            dx = ddx;
                            dy = ddy;
                            return true;
                        }
                    }
                }
            }
        }

        fx = fy = dx = dy = 0;
        return false;
    }
}