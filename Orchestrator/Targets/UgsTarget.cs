using System.Text;
using System.Text.Json;
using Orchestrator.Contracts;
using Orchestrator.Util;

namespace Orchestrator.Targets;

/// <summary>
/// UGS(Unity Gaming Services) Cloud Code 를 손으로 쓰는 타깃 — 두 번째 <see cref="IExecTarget"/> 구현.
///
/// 왜 이게 있나(DESIGN D5): 손을 바꾸면 같은 루프가 **클라이언트(Unity)** 대신 **백엔드(UGS)** 를 만들고
/// 검증한다. 두뇌(백엔드 4종)에 이어 손도 둘이 되면서 "두 축이 pluggable"이 코드로 증명된다.
///
/// 검증 방식이 Unity 와 다르다:
///   Unity  — 컴파일(recompile_status) + 플레이모드 런타임 assert(eval)
///   UGS    — **배포**(`ugs deploy`)가 곧 1차 검증. 서버가 스크립트를 파싱·검증하므로
///            문법/구조 오류는 배포 실패로 되돌아온다.
///
/// 런타임 호출 검증은 **지원하지 않는다**(<see cref="Supports"/> 가 false).
/// `ugs cloud-code scripts` 에 invoke/run 계열 명령이 없어서다(create/publish/get/list/update/delete 뿐).
/// 호출까지 검증하려면 Cloud Code REST 엔드포인트를 플레이어 토큰으로 직접 부르는 경로가 필요한데,
/// 그건 이 타깃의 다음 단계로 남겨 뒀다. 없는 기능을 있는 척하지 않는 게 이 프로젝트의 규칙이다.
///
/// 전제: `ugs` CLI + 서비스 계정 인증(`ugs login` 또는 UGS_CLI_SERVICE_KEY_ID/SECRET) + project-id.
/// </summary>
public sealed class UgsTarget : IExecTarget
{
    private readonly string _projectRoot;
    private readonly string _deployDir;      // Cloud Code 스크립트를 모아 두는 폴더(배포 단위)
    private readonly string? _projectId;
    private readonly string? _environment;     // 환경 "이름" (예: production)
    private string? _environmentId;            // 환경 "ID"(UUID) — 토큰 교환에 필요. 필요 시 해석해 캐시한다.
    private readonly int _timeoutSec;

    public string Name => _projectId is null ? "ugs:cloud-code" : $"ugs:{_projectId[..8]}…";

    public string ConnectionHint =>
        "UGS 인증 또는 프로젝트 설정이 되어 있지 않습니다.\n" +
        "  1) 서비스 계정 키 저장:  ugs login        (또는 환경변수 UGS_CLI_SERVICE_KEY_ID / UGS_CLI_SERVICE_SECRET_KEY)\n" +
        "     키 발급은 Unity Cloud 대시보드 → Administration → Service Accounts (Cloud Code 권한 필요)\n" +
        "  2) 프로젝트 지정:        --ugs-project-id <id>  (또는 ugs config set project-id <id>)\n" +
        "  3) 환경 지정(선택):      --ugs-env production";

    // 배포는 항상, 호출 검증은 **자격 증명이 있을 때만** 지원한다.
    // (`ugs` CLI 에는 호출 명령이 없어 REST 로 직접 부른다 — UgsInvoker)
    public bool Supports(VerifyKind kind) => kind switch
    {
        VerifyKind.Compile => true,
        VerifyKind.RuntimeAssert => HasInvokeCredentials,
        _ => false,
    };

    private static bool HasInvokeCredentials
    {
        get
        {
            var (id, secret) = UgsInvoker.ReadCredentials();
            return !string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(secret);
        }
    }

    public string LabelFor(VerifyKind kind) => kind switch
    {
        VerifyKind.Compile => "배포",
        VerifyKind.RuntimeAssert => "스크립트 호출",
        _ => kind.ToString(),
    };

    public UgsTarget(
        string projectRoot,
        string deployDir,
        string? projectId,
        string? environment,
        string? environmentId = null,
        int timeoutSec = 180)
    {
        _projectRoot = projectRoot;
        _deployDir = deployDir;
        _projectId = projectId;
        _environment = environment;
        _environmentId = environmentId;
        _timeoutSec = timeoutSec;
    }

    /// <summary>UGS 타깃의 생성 규격 — Cloud Code JavaScript(CommonJS).</summary>
    public string GenerationBrief => $$"""
        TARGET: Unity Gaming Services (UGS) Cloud Code — JavaScript, CommonJS.
        - Put every script under {{RelativeDeployDir}}/ with a .js extension (one script per file).
        - Each script MUST export exactly this shape:

          module.exports = async ({ params, context, logger }) => {
            // params : parameters passed to the script
            // context: { projectId, environmentId, environmentName, playerId, accessToken }
            // logger : debug() / info() / warning() / error()
            return { /* JSON-serializable result */ };
          };

        - **You MUST declare every parameter**, or Cloud Code strips it and `params` arrives empty
          (the script then deploys fine but behaves wrong). Assign `module.exports` FIRST, then:

          module.exports.params = {
            streak: { type: "Numeric", required: true },
            playerName: { type: "String" },
          };

          Valid types: "Numeric", "String", "Boolean", "JSON", "Any".
        - Take identifiers such as a player id as an explicit PARAM, not from `context`:
          verification calls the script as a trusted client, so `context.playerId` is not available.
        - Use only libraries Cloud Code provides (e.g. `require("lodash-4.17")`). No filesystem, no native modules.
        - Validate inputs and return a clear error result instead of throwing on bad params.

        - After the FILE blocks, emit EXACTLY ONE runtime check as a JSON array:
        ASSERT:
        ```json
        [
          { "script": "<scriptName>", "params": { ... }, "expect": { ... } },
          { "script": "<scriptName>", "params": { ... }, "expectError": true }
        ]
        ```
          Each case invokes the DEPLOYED script over the Cloud Code REST API and checks the response.
          Rules:
            * "script" is the file name without .js.
            * "expect" is matched as a SUBSET of the script's returned object — list only the keys that matter.
            * "expectError": true means the call itself must fail (use for invalid input at the platform level).
            * Cover the edge cases the goal implies (bounds, clamping, invalid input), not just the happy path.
        """;

    private string RelativeDeployDir =>
        Path.GetRelativePath(_projectRoot, _deployDir).Replace('\\', '/');

    // ── ② 적용 ───────────────────────────────────────────────────────────────
    public async Task<ApplyResult> ApplyAsync(IReadOnlyList<FileEdit> edits, CancellationToken ct)
    {
        if (edits.Count == 0)
            return new ApplyResult(false, "적용할 파일 편집이 없습니다(파싱된 FILE 블록 0개).");

        var root = Path.GetFullPath(_projectRoot);
        var written = new List<string>();
        foreach (var edit in edits)
        {
            var full = Path.GetFullPath(Path.Combine(_projectRoot, edit.RelativePath));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return new ApplyResult(false, $"프로젝트 밖 경로 거부: {edit.RelativePath}");

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(full, edit.Content, new UTF8Encoding(false), ct);
            written.Add(edit.RelativePath);
        }

        // 실제 배포는 VerifyAsync 가 한다(배포 = 검증). 여기서는 파일만 놓는다.
        return new ApplyResult(true, $"{written.Count}개 파일 적용: {string.Join(", ", written)}");
    }

    // ── ③ 검증 ────────────────────────────────────────────────────────────────
    public Task<VerifyResult> VerifyAsync(VerifySpec spec, CancellationToken ct) => spec.Kind switch
    {
        VerifyKind.Compile => VerifyDeployAsync(ct),
        VerifyKind.RuntimeAssert => VerifyInvokeAsync(
            spec.AssertCode ?? throw new ArgumentException("RuntimeAssert 는 AssertCode 가 필요합니다.", nameof(spec)), ct),
        _ => throw new NotSupportedException($"지원하지 않는 검증: {spec.Kind}"),
    };

    // ── ③-a 배포 검증 ─────────────────────────────────────────────────────────
    // `ugs deploy` 는 publish 까지 수행한다(실측 확인) — 별도 게시 단계가 필요 없다.
    private async Task<VerifyResult> VerifyDeployAsync(CancellationToken ct)
    {
        // 서비스 필터 값은 `cloud-code` 가 아니라 **`cloud-code-scripts`** 다(실측으로 확인).
        // 잘못된 값을 주면 CLI 가 "No content deployed" 와 함께 인식 실패를 알린다.
        var res = await RunUgsAsync(new[] { "deploy", _deployDir, "--services", "cloud-code-scripts" }, ct);
        var errors = ParseDeployErrors(res.StdOut, res.StdErr, res.ExitCode);

        return errors.Count == 0
            ? new VerifyResult(true, res.StdOut, Array.Empty<string>())
            : new VerifyResult(false, res.StdOut + res.StdErr, errors);
    }

    // ── ③-b 호출 검증 ─────────────────────────────────────────────────────────
    // 배포된 스크립트를 실제로 불러 응답을 확인한다. "배포 성공 ≠ 동작 정상"을 메우는 단계.
    private async Task<VerifyResult> VerifyInvokeAsync(string assertJson, CancellationToken ct)
    {
        var (keyId, secret) = UgsInvoker.ReadCredentials();
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(secret))
        {
            // 인프라 문제는 모델 탓이 아니므로 피드백하지 않고 중단시킨다.
            throw new InvalidOperationException(
                "호출 검증에는 UGS_CLI_SERVICE_KEY_ID / UGS_CLI_SERVICE_SECRET_KEY 가 필요합니다(.env 참고).");
        }

        var projectId = _projectId ?? throw new InvalidOperationException("UGS 프로젝트 ID 가 지정되지 않았습니다.");
        var environmentId = await ResolveEnvironmentIdAsync(ct)
            ?? throw new InvalidOperationException($"환경 ID 를 찾지 못했습니다(환경 이름: {_environment ?? "production"}).");

        using var invoker = new UgsInvoker(projectId, environmentId, keyId!, secret!);
        var failures = await invoker.VerifyAsync(assertJson, ct);

        return failures.Count == 0
            ? new VerifyResult(true, "", Array.Empty<string>())
            : new VerifyResult(false, "", failures);
    }

    // 토큰 교환에는 환경 **ID**(UUID)가 필요한데 사용자는 보통 이름("production")만 안다.
    // 인자로 받았으면 그대로 쓰고, 아니면 `ugs env list --json` 으로 이름→ID 를 해석한다.
    private async Task<string?> ResolveEnvironmentIdAsync(CancellationToken ct)
    {
        if (_environmentId is not null)
            return _environmentId;

        var wanted = _environment ?? "production";
        var res = await RunUgsAsync(new[] { "env", "list" }, ct, withEnvironment: false);

        try
        {
            var json = ExtractJsonArray(res.StdOut);
            if (json is null)
                return null;

            using var doc = JsonDocument.Parse(json);
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.TryGetProperty("name", out var n) &&
                    string.Equals(n.GetString(), wanted, StringComparison.OrdinalIgnoreCase) &&
                    e.TryGetProperty("id", out var id))
                {
                    _environmentId = id.GetString();
                    return _environmentId;
                }
            }
        }
        catch { /* 아래에서 null 반환 */ }
        return null;
    }

    private static string? ExtractJsonArray(string stdout)
    {
        var start = stdout.IndexOf('[');
        var end = stdout.LastIndexOf(']');
        return start >= 0 && end > start ? stdout[start..(end + 1)] : null;
    }

    /// <summary>배포 전 사전 점검: 인증·프로젝트 설정이 됐는지. 안 되어 있으면 AI 호출 전에 빠르게 실패시킨다.</summary>
    public async Task<bool> IsConnectedAsync(CancellationToken ct)
    {
        try
        {
            var status = await RunUgsAsync(new[] { "status" }, ct);
            var text = status.StdOut + status.StdErr;
            // 자격증명이 없으면 "No Service Account key stored." 가 온다.
            if (text.Contains("No Service Account key", StringComparison.OrdinalIgnoreCase))
                return false;

            // project-id 는 인자/환경변수/설정 중 하나로 반드시 결정돼야 한다.
            if (_projectId is not null || Environment.GetEnvironmentVariable("UGS_CLI_PROJECT_ID") is { Length: > 0 })
                return true;

            var cfg = await RunUgsAsync(new[] { "config", "get", "project-id" }, ct);
            return !(cfg.StdOut + cfg.StdErr).Contains("is not set", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }

    // ── ugs CLI 호출 ──────────────────────────────────────────────────────────
    // 주의: 명령마다 받는 플래그가 다르다. `env list` 는 --environment-name 을 모르며,
    // 주면 실행 대신 도움말을 뱉는다(실측). 그래서 환경 플래그는 필요한 명령에만 붙인다.
    private Task<ProcessResult> RunUgsAsync(
        IEnumerable<string> args,
        CancellationToken ct,
        bool withEnvironment = true)
    {
        var list = new List<string>(args);
        if (_projectId is not null)
        {
            list.Add("--project-id");
            list.Add(_projectId);
        }
        if (withEnvironment && _environment is not null)
        {
            list.Add("--environment-name");
            list.Add(_environment);
        }
        list.Add("--json");

        var (file, prefix) = UgsInvocation();
        var full = new List<string>(prefix);
        full.AddRange(list);
        return ProcessRunner.RunAsync(file, full, workingDir: _projectRoot, ct);
    }

    // Windows 는 npm 전역 shim(ugs.cmd)이라 cmd.exe 로 감싸 PATH 해석을 맡긴다.
    private static (string File, string[] Prefix) UgsInvocation() =>
        OperatingSystem.IsWindows()
            ? ("cmd.exe", new[] { "/c", "ugs" })
            : ("ugs", Array.Empty<string>());

    // ── 배포 결과 판정 ─────────────────────────────────────────────────────────
    // `ugs deploy --json` 은 { Result: { Created/Updated/Failed[...] }, Messages/Errors } 형태를 낸다.
    // 스키마가 버전에 따라 흔들릴 수 있으므로, 실패 항목을 찾되 못 찾으면 종료 코드로 판정한다.
    private static IReadOnlyList<string> ParseDeployErrors(string stdout, string stderr, int exitCode)
    {
        var errors = new List<string>();
        var json = ExtractJson(stdout);

        if (json is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                CollectFailures(doc.RootElement, errors);
            }
            catch { /* 파싱 실패 → 아래 폴백 */ }
        }

        if (errors.Count == 0 && exitCode != 0)
        {
            var raw = (stderr + "\n" + stdout).Trim();
            errors.Add(raw.Length == 0 ? $"ugs deploy 실패(exit {exitCode})" : Flatten(raw));
        }
        return errors;
    }

    // Failed / Errors 배열을 재귀로 훑어 사람이 읽을 메시지를 모은다.
    private static void CollectFailures(JsonElement el, List<string> into)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in el.EnumerateObject())
            {
                if ((prop.NameEquals("Failed") || prop.NameEquals("failed") ||
                     prop.NameEquals("Errors") || prop.NameEquals("errors")) &&
                    prop.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in prop.Value.EnumerateArray())
                        into.Add(Flatten(DescribeFailure(item)));
                }
                else
                {
                    CollectFailures(prop.Value, into);
                }
            }
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
                CollectFailures(item, into);
        }
    }

    private static string DescribeFailure(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.String)
            return item.GetString() ?? "";

        string? name = null, message = null;
        foreach (var key in new[] { "Name", "name", "Path", "path" })
            if (item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
            { name = v.GetString(); break; }
        foreach (var key in new[] { "Message", "message", "Reason", "reason", "Error", "error" })
            if (item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
            { message = v.GetString(); break; }

        return (name, message) switch
        {
            (not null, not null) => $"{name}: {message}",
            (null, not null) => message!,
            (not null, null) => name!,
            _ => item.ToString(),
        };
    }

    // ugs 는 JSON 앞뒤로 로그를 섞어 내보낼 수 있어, 첫 '{'~마지막 '}' 만 잘라 쓴다.
    private static string? ExtractJson(string stdout)
    {
        var start = stdout.IndexOf('{');
        var end = stdout.LastIndexOf('}');
        return start >= 0 && end > start ? stdout[start..(end + 1)] : null;
    }

    private static string Flatten(string s) =>
        s.Replace("\r", " ").Replace("\n", " ").Trim();
}
