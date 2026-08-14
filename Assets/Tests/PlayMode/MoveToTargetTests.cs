using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class MoveToTargetTests
{
    [Test]
    public void SetTarget_Away_From_Current_Position_Starts_Moving()
    {
        GameObject go = new GameObject();
        MoveToTarget target = go.AddComponent<MoveToTarget>();

        bool startedMoving = target.SetTarget(new Vector3(5f, 0f, 0f));

        Assert.IsTrue(startedMoving);
        Assert.IsTrue(target.IsMoving);

        Object.DestroyImmediate(go);
    }

    [Test]
    public void SetTarget_At_Current_Position_Does_Not_Start_Moving()
    {
        GameObject go = new GameObject();
        MoveToTarget target = go.AddComponent<MoveToTarget>();

        bool startedMoving = target.SetTarget(go.transform.position);

        Assert.IsFalse(startedMoving);
        Assert.IsFalse(target.IsMoving);

        Object.DestroyImmediate(go);
    }

    [Test]
    public void Tick_Does_Nothing_When_Not_Moving()
    {
        GameObject go = new GameObject();
        MoveToTarget target = go.AddComponent<MoveToTarget>();
        Vector3 startPosition = go.transform.position;

        target.Tick(0.016f);

        Assert.AreEqual(startPosition, go.transform.position);

        Object.DestroyImmediate(go);
    }

    [UnityTest]
    public IEnumerator Reaches_Target_Over_Frames()
    {
        GameObject go = new GameObject();
        MoveToTarget target = go.AddComponent<MoveToTarget>();
        Vector3 destination = new Vector3(0.1f, 0f, 0f);

        target.SetTarget(destination);

        int framesWaited = 0;
        const int maxFrames = 300;

        while (target.IsMoving && framesWaited < maxFrames)
        {
            yield return null;
            framesWaited++;
        }

        Assert.IsFalse(target.IsMoving, "Expected MoveToTarget to finish moving within " + maxFrames + " frames.");
        Assert.Less((go.transform.position - destination).sqrMagnitude, 0.001f * 0.001f);

        Object.Destroy(go);
        yield return null;
    }
}