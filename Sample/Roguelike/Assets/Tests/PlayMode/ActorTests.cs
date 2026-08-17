using System;
using System.Collections;
using NUnit.Framework;
using Roguelike.Core;
using UnityEngine.TestTools;

public class ActorTests
{
    [Test]
    public void Constructor_Sets_Initial_State()
    {
        var actor = new Actor("Goblin", 3, 4, 10);

        Assert.AreEqual("Goblin", actor.Name);
        Assert.AreEqual(3, actor.X);
        Assert.AreEqual(4, actor.Y);
        Assert.AreEqual(10, actor.MaxHealth);
        Assert.AreEqual(10, actor.CurrentHealth);
        Assert.IsTrue(actor.IsAlive);
    }

    [Test]
    public void TakeDamage_Reduces_Health()
    {
        var actor = new Actor("Goblin", 0, 0, 10);

        actor.TakeDamage(4);

        Assert.AreEqual(6, actor.CurrentHealth);
        Assert.IsTrue(actor.IsAlive);
    }

    [Test]
    public void TakeDamage_Clamps_At_Zero()
    {
        var actor = new Actor("Goblin", 0, 0, 10);

        actor.TakeDamage(999);

        Assert.AreEqual(0, actor.CurrentHealth);
        Assert.IsFalse(actor.IsAlive);
    }

    [Test]
    public void TakeDamage_Negative_Throws()
    {
        var actor = new Actor("Goblin", 0, 0, 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => actor.TakeDamage(-1));
    }

    [Test]
    public void Heal_Increases_Health()
    {
        var actor = new Actor("Goblin", 0, 0, 10);
        actor.TakeDamage(6);

        actor.Heal(3);

        Assert.AreEqual(7, actor.CurrentHealth);
    }

    [Test]
    public void Heal_Clamps_At_Max()
    {
        var actor = new Actor("Goblin", 0, 0, 10);
        actor.TakeDamage(2);

        actor.Heal(999);

        Assert.AreEqual(10, actor.CurrentHealth);
    }

    [Test]
    public void Heal_Negative_Throws()
    {
        var actor = new Actor("Goblin", 0, 0, 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => actor.Heal(-1));
    }

    [Test]
    public void Died_Event_Raised_Exactly_Once_On_Repeated_Lethal_Damage()
    {
        var actor = new Actor("Goblin", 0, 0, 10);
        int deathCount = 0;
        actor.Died += _ => deathCount++;

        actor.TakeDamage(10);
        actor.TakeDamage(5);
        actor.TakeDamage(1);

        Assert.AreEqual(1, deathCount);
        Assert.IsFalse(actor.IsAlive);
        Assert.AreEqual(0, actor.CurrentHealth);
    }

    [Test]
    public void MoveTo_Updates_Position()
    {
        var actor = new Actor("Goblin", 0, 0, 10);

        actor.MoveTo(5, 7);

        Assert.AreEqual(5, actor.X);
        Assert.AreEqual(7, actor.Y);
    }

    [UnityTest]
    public IEnumerator MoveTo_Persists_Across_Frames()
    {
        var actor = new Actor("Goblin", 0, 0, 10);

        actor.MoveTo(2, 2);
        yield return null;

        Assert.AreEqual(2, actor.X);
        Assert.AreEqual(2, actor.Y);
    }
}