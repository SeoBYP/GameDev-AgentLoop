using System.Text.Json;

namespace Orchestrator.Targets;

/// <summary>
/// 성능 예산 명세(PERF 블록). 백엔드는 **무엇을 얼마나 호출할지와 예산**만 선언하고,
/// 실제 측정 코드는 <see cref="PerfHarness"/> 가 만든다 — 생성자가 자기 벤치마크를
/// 느슨하게 써서 통과시키는 걸 막기 위해서다.
/// </summary>
public sealed record PerfSpec(
    string Component,     // 측정 대상 MonoBehaviour 타입 이름
    string Call,          // 반복 호출할 식. 대상 인스턴스는 `target` 이라는 이름으로 제공된다
    string? Setup,        // (선택) 측정 전 1회 실행할 준비 코드
    int Iterations,
    double MaxTotalMs,
    SceneBudget? Scene);  // (선택) 씬에 남는 렌더 비용 예산

/// <summary>
/// 컴포넌트가 **씬에 남기는 렌더 비용** 예산.
///
/// 시간 측정(핫패스 호출 비용)과 다른 축이다 — 오브젝트를 스폰하는 컴포넌트는 호출이 빨라도
/// 드로우콜을 폭증시킬 수 있다. 그래서 `Setup` 을 실행해 씬에 상태를 만든 뒤,
/// 프레임이 렌더될 시간을 주고 **베이스라인 대비 증가분**을 본다.
///
/// 메모리는 넣지 않았다(실측): Boehm GC 요동 때문에 `monoUsedBytes` 델타가 음수로도 나온다.
/// 판정 기준으로 쓸 만큼 안정적이지 않아 **진단 기록으로만** 남긴다.
/// </summary>
public sealed record SceneBudget(
    string Setup,                 // 씬 상태를 만드는 코드(예: target.SpawnWave())
    int? MaxDrawCallIncrease,
    int? MaxTriangleIncrease);

/// <summary>
/// 플레이모드에서 실행할 성능 측정 스니펫을 만든다.
///
/// 왜 이런 방식인가(실측으로 결정):
///   - Unity Mono 는 **Boehm GC**(세대 없음)라 `GC.CollectionCount`/`GetTotalAllocatedBytes` 가
///     쓸 수 없거나 항상 0 이다. `GetTotalMemory` 도 힙 크기라 수집이 따라잡으면 0 으로 보인다.
///   - 반면 **시간은 확실하게 드러난다.** 같은 일을 하는 두 구현을 5만 회 돌렸을 때
///     무할당 4.8ms vs 매 호출 할당 30.2ms — 6배 차이가 안정적으로 재현됐다.
///   → 그래서 "핫패스 비용"을 **경과 시간**으로 측정하고 예산과 비교한다.
///     할당은 GC 압력·할당 비용으로 시간에 반영된다.
/// </summary>
public static class PerfHarness
{
    public static PerfSpec Parse(string json)
    {
        using var doc = JsonDocument.Parse(json.Trim());
        var root = doc.RootElement;

        string Req(string name) =>
            root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s
                ? s
                : throw new FormatException($"PERF 블록에 \"{name}\" 문자열이 필요합니다.");

        var iterations = root.TryGetProperty("iterations", out var it) && it.TryGetInt32(out var n) ? n : 10000;
        var budget = root.TryGetProperty("maxTotalMs", out var b) && b.TryGetDouble(out var d) ? d : 0;

        if (iterations <= 0)
            throw new FormatException("PERF 의 iterations 는 1 이상이어야 합니다.");
        if (budget <= 0)
            throw new FormatException("PERF 에 \"maxTotalMs\"(양수)가 필요합니다.");

        var setup = root.TryGetProperty("setup", out var s2) && s2.ValueKind == JsonValueKind.String
            ? s2.GetString()
            : null;

        SceneBudget? scene = null;
        if (root.TryGetProperty("scene", out var sc) && sc.ValueKind == JsonValueKind.Object)
        {
            var sceneSetup = sc.TryGetProperty("setup", out var ss) && ss.ValueKind == JsonValueKind.String
                ? ss.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(sceneSetup))
                throw new FormatException("PERF 의 scene 에는 \"setup\" 이 필요합니다.");

            int? Opt(string name) =>
                sc.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : null;

            scene = new SceneBudget(sceneSetup!, Opt("maxDrawCallIncrease"), Opt("maxTriangleIncrease"));
            if (scene.MaxDrawCallIncrease is null && scene.MaxTriangleIncrease is null)
                throw new FormatException("PERF 의 scene 에는 maxDrawCallIncrease 또는 maxTriangleIncrease 가 필요합니다.");
        }

        return new PerfSpec(Req("component"), Req("call"), setup, iterations, budget, scene);
    }

    /// <summary>
    /// 표현식을 "문"으로 만든다.
    ///
    /// 왜: `call` 로 프로퍼티 읽기(`target.Count`)가 오면 C# 에서 그건 문이 될 수 없어
    /// "Only assignment, call, increment, decrement, await, and new object expressions can be used
    /// as a statement" 로 컴파일이 깨진다(실측). 메서드 호출이면 그대로 두고,
    /// 값 표현식이면 `_ =` 로 버려 문으로 만든다 — 최적화로 사라지지 않게 discard 를 쓴다.
    /// </summary>
    private static string AsStatement(string expr)
    {
        var e = expr.Trim().TrimEnd(';').TrimEnd();
        return e.EndsWith(')') ? e : $"_ = {e}";
    }

    /// <summary>씬에 상태를 만드는 스니펫. 정리하지 않는다 — 플레이모드를 빠져나가면 자동으로 사라진다.</summary>
    public static string BuildSceneSnippet(PerfSpec spec)
    {
        var setup = AsStatement(spec.Scene!.Setup);
        return $$"""
            var __go = new UnityEngine.GameObject();
            var target = __go.AddComponent<{{spec.Component}}>();
            {{setup}};
            return 1;
            """;
    }

    /// <summary>
    /// 측정 스니펫 생성. 반환값은 경과 밀리초(double).
    /// 워밍업 1회로 JIT·최초 할당을 측정에서 제외한다.
    /// </summary>
    public static string BuildSnippet(PerfSpec spec)
    {
        var call = AsStatement(spec.Call);
        var setup = string.IsNullOrWhiteSpace(spec.Setup) ? "" : AsStatement(spec.Setup!) + ";";

        return $$"""
            var __go = new UnityEngine.GameObject();
            var target = __go.AddComponent<{{spec.Component}}>();
            {{setup}}
            {{call}};
            var __sw = System.Diagnostics.Stopwatch.StartNew();
            for (int __i = 0; __i < {{spec.Iterations}}; __i++) { {{call}}; }
            __sw.Stop();
            UnityEngine.Object.DestroyImmediate(__go);
            return __sw.Elapsed.TotalMilliseconds;
            """;
    }
}
