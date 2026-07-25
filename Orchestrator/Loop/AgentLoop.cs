using Orchestrator.Contracts;

namespace Orchestrator.Loop;

/// <summary>
/// 루프 소유자. DESIGN.md §5 의 5단계를 그대로 구현한다:
///
///   목표(자연어)
///    → ① 생성   backend.CompleteAsync           (텍스트/파일편집 out)
///    → ② 적용   target.ApplyAsync               (파일쓰기 + 리컴파일)
///    → ③ 검증   target.VerifyAsync              (컴파일 에러?)
///    → ④ 피드백  에러를 History에 넣어 백엔드로 되돌림
///    → ⑤ 판정   통과 → 종료 / 실패 → ①로 (maxSteps 가드)
///
/// 이 클래스가 "루프는 우리 것"(D1)의 실체다. 백엔드/타깃은 인터페이스 뒤에 있고,
/// 적용·검증·재시도·판정의 흐름은 전적으로 여기서 소유한다.
/// </summary>
public sealed class AgentLoop
{
    private readonly IAgentBackend _backend;
    private readonly IExecTarget _target;
    private readonly LoopOptions _options;
    private readonly Action<string> _log;

    public AgentLoop(IAgentBackend backend, IExecTarget target, LoopOptions options, Action<string>? log = null)
    {
        _backend = backend;
        _target = target;
        _options = options;
        _log = log ?? Console.WriteLine;
    }

    public async Task<LoopResult> RunAsync(string goal, CancellationToken ct)
    {
        _log($"목표: {goal}");
        _log($"백엔드: {_backend.Name}   타깃: {_target.Name}   maxSteps: {_options.MaxSteps}");

        var history = new List<Turn> { new(Role.User, BuildInitialPrompt(goal)) };

        for (int step = 1; step <= _options.MaxSteps; step++)
        {
            _log($"\n──────── step {step}/{_options.MaxSteps} ────────");

            // ① 생성
            var reply = await _backend.CompleteAsync(new AgentContext(SystemPrompt, history), ct);
            history.Add(new Turn(Role.Assistant, reply.Text));
            _log($"① 생성  → 파일 편집 {reply.Edits.Count}개");

            if (reply.Edits.Count == 0)
            {
                _log("   (파싱된 FILE 블록 없음 — 형식 재요청)");
                history.Add(new Turn(Role.User, NoEditsFeedback));
                continue;
            }

            // ② 적용
            var apply = await _target.ApplyAsync(reply.Edits, ct);
            _log($"② 적용  → {apply.Message}");
            if (!apply.Ok)
            {
                history.Add(new Turn(Role.User, $"적용 실패: {apply.Message}. 경로를 고쳐 다시 출력하세요."));
                continue;
            }

            // ③ 검증
            var verify = await _target.VerifyAsync(new VerifySpec(VerifyKind.Compile), ct);

            // ⑤ 판정
            if (verify.Ok)
            {
                _log("③ 검증  → 컴파일 통과 ✅");
                return new LoopResult(true, step, $"{step}스텝 만에 컴파일 통과");
            }

            _log($"③ 검증  → 컴파일 에러 {verify.Errors.Count}건 ❌");
            foreach (var e in verify.Errors.Take(5))
                _log($"      · {e}");

            // ④ 피드백 → 다음 스텝 ①로
            history.Add(new Turn(Role.User, BuildErrorFeedback(verify.Errors)));
        }

        return new LoopResult(false, _options.MaxSteps, $"maxSteps({_options.MaxSteps}) 초과 — 미해결");
    }

    // ── 프롬프트/피드백 (출력 계약을 시스템 프롬프트로 강제 — DESIGN.md §4) ──────────

    private const string SystemPrompt = """
        You are a Unity C# code generator running inside an automated build → verify → repair loop.

        OUTPUT CONTRACT (STRICT):
        - Emit every file exactly as:
        FILE: <path relative to the Unity project root>
        ```csharp
        <complete file content>
        ```
        - Put runtime scripts under Assets/Scripts/.
        - Always output the FULL file content. No diffs, no "// ...", no ellipses, no omissions.
        - Keep any prose to at most one short line; the FILE blocks are what matter.

        TARGET: Unity 6 (6000.x), C#. Assume UnityEngine is available.
        When you receive compiler errors, fix the root cause and re-emit the COMPLETE corrected file(s).
        """;

    private static string BuildInitialPrompt(string goal) =>
        $"목표: {goal}\n\n위 목표를 만족하는 Unity C# 파일을 출력 계약(FILE: + ```csharp 펜스) 형식으로 생성하세요.";

    private const string NoEditsFeedback =
        "응답에서 FILE 블록을 하나도 찾지 못했습니다. 반드시 'FILE: <경로>' 다음 줄에 ```csharp 펜스로 전체 파일을 출력하세요.";

    private static string BuildErrorFeedback(IReadOnlyList<string> errors)
    {
        var list = string.Join("\n", errors.Select(e => "  - " + e));
        return $"""
            컴파일에 실패했습니다. 아래 컴파일러 에러를 고쳐서 전체 파일을 다시 출력하세요
            (부분 수정/diff 금지 — 전체 파일 재출력):

            {list}
            """;
    }
}
