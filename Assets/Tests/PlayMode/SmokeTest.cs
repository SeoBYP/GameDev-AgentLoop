using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// 테스트 인프라 검증용 스모크 테스트.
// (1) 테스트가 발견·실행되는가  (2) [UnityTest] 로 여러 프레임에 걸친 검증이 되는가
public class SmokeTest
{
    [Test]
    public void SingleFrame_Works()
    {
        var go = new GameObject("smoke");
        Assert.IsNotNull(go);
        Object.DestroyImmediate(go);
    }

    // 시나리오 재생의 핵심: yield return null 로 프레임을 넘긴다.
    [UnityTest]
    public IEnumerator MultiFrame_AdvancesFrames()
    {
        int start = Time.frameCount;
        yield return null;
        yield return null;
        Assert.Greater(Time.frameCount, start, "프레임이 진행되지 않았습니다.");
    }
}
