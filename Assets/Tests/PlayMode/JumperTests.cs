using System.Collections;
using AgentLoop.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

public class JumperTests : InputTestFixture
{
    private const float JumpDuration = 0.5f;
    private const float LandingTimeoutSeconds = 5f;

    [UnityTest]
    public IEnumerator Space_Triggers_Jump_And_Lands_After_Duration()
    {
        var keyboard = InputSystem.AddDevice<Keyboard>();
        var go = new GameObject();
        var jumper = go.AddComponent<Jumper>();

        Assert.IsFalse(jumper.IsJumping);

        Press(keyboard.spaceKey);
        yield return null;
        Release(keyboard.spaceKey);
        yield return null;

        Assert.IsTrue(jumper.IsJumping, "Space press should start the jump");

        float startTime = Time.time;
        while (jumper.IsJumping && Time.time - startTime < LandingTimeoutSeconds)
        {
            yield return null;
        }

        float elapsed = Time.time - startTime;
        Assert.IsFalse(jumper.IsJumping, "Jumper should have landed automatically");
        Assert.Less(elapsed, LandingTimeoutSeconds, "Landing took too long / never happened");

        Object.Destroy(go);
    }

    [Test]
    public void TryJump_Returns_False_When_Already_Jumping()
    {
        var go = new GameObject();
        var jumper = go.AddComponent<Jumper>();

        Assert.IsTrue(jumper.TryJump());
        Assert.IsFalse(jumper.TryJump());

        Object.DestroyImmediate(go);
    }

    [Test]
    public void Tick_Lands_Exactly_At_Duration_Boundary()
    {
        var go = new GameObject();
        var jumper = go.AddComponent<Jumper>();

        jumper.TryJump();
        jumper.Tick(JumpDuration - 0.01f);
        Assert.IsTrue(jumper.IsJumping, "Should still be jumping just before duration elapses");

        jumper.Tick(0.02f);
        Assert.IsFalse(jumper.IsJumping, "Should land once accumulated time exceeds duration");

        Object.DestroyImmediate(go);
    }
}