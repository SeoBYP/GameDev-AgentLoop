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

    [UnityTest]
    public IEnumerator VirtualMouse_MoveAndClick_IsSeen()
    {
        var mouse = InputSystem.AddDevice<Mouse>();

        Set(mouse.position, new Vector2(640f, 360f));
        Press(mouse.leftButton);
        yield return null;

        Assert.IsTrue(mouse.leftButton.isPressed, "가상 마우스 클릭이 반영되지 않았습니다.");
        Assert.AreEqual(640f, mouse.position.x.ReadValue(), 0.5f, "마우스 위치가 반영되지 않았습니다.");

        Release(mouse.leftButton);
        yield return null;
    }

    [UnityTest]
    public IEnumerator VirtualGamepad_StickAndButton_AreSeen()
    {
        var pad = InputSystem.AddDevice<Gamepad>();

        Set(pad.leftStick, new Vector2(1f, 0f));
        Press(pad.buttonSouth);
        yield return null;

        Assert.Greater(pad.leftStick.x.ReadValue(), 0.5f, "스틱 입력이 반영되지 않았습니다.");
        Assert.IsTrue(pad.buttonSouth.isPressed, "패드 버튼 입력이 반영되지 않았습니다.");

        Release(pad.buttonSouth);
        yield return null;
    }

    // TouchPhase 는 UnityEngine(구 Input) 과 UnityEngine.InputSystem 양쪽에 있어 **정규화가 필수**다
    // (그냥 TouchPhase 라고 쓰면 CS0104 모호 참조).
    // 그리고 SetTouch 는 이벤트를 큐에 넣기만 하므로 `yield return null` 로 한 프레임 넘겨야 반영된다.
    //   실측: 프레임을 안 넘기고 읽으면 Expected: Began / But was: None 으로 실패.
    [UnityTest]
    public IEnumerator VirtualTouch_BeganAndEnded_AreSeen()
    {
        var screen = InputSystem.AddDevice<Touchscreen>();

        SetTouch(0, UnityEngine.InputSystem.TouchPhase.Began, new Vector2(120f, 240f));
        yield return null;

        Assert.AreEqual(UnityEngine.InputSystem.TouchPhase.Began, screen.primaryTouch.phase.ReadValue(),
            "터치 시작이 반영되지 않았습니다.");
        Assert.AreEqual(120f, screen.primaryTouch.position.x.ReadValue(), 0.5f,
            "터치 좌표가 반영되지 않았습니다.");

        SetTouch(0, UnityEngine.InputSystem.TouchPhase.Ended, new Vector2(120f, 240f));
        yield return null;

        Assert.AreEqual(UnityEngine.InputSystem.TouchPhase.Ended, screen.primaryTouch.phase.ReadValue(),
            "터치 종료가 반영되지 않았습니다.");
    }
}
