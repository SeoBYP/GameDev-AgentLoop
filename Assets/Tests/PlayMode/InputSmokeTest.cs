using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

// 입력 시뮬레이션 인프라 검증용.
// InputTestFixture 를 상속하면 **가상 입력 장치**를 만들어 실제 조작처럼 주입할 수 있다.
// [UnityTest] 와 결합하면 "누르고 → 몇 프레임 뒤 → 결과 확인" 같은 조작 시나리오가 검증된다.
public class InputSmokeTest : InputTestFixture
{
    [UnityTest]
    public IEnumerator VirtualKeyboard_Press_IsSeen()
    {
        var keyboard = InputSystem.AddDevice<Keyboard>();

        Press(keyboard.spaceKey);
        yield return null;   // 입력이 처리되도록 한 프레임 넘긴다

        Assert.IsTrue(keyboard.spaceKey.isPressed, "가상 키 입력이 반영되지 않았습니다.");

        Release(keyboard.spaceKey);
        yield return null;

        Assert.IsFalse(keyboard.spaceKey.isPressed, "키를 뗐는데도 눌린 상태입니다.");
    }
}
