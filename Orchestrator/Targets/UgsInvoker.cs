using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Orchestrator.Targets;

/// <summary>
/// 배포된 Cloud Code 스크립트를 **실제로 호출해** 응답을 검증한다 — UGS 판 "런타임 assert".
///
/// 왜 필요한가: 배포 성공은 "서버가 스크립트를 받아들였다"는 뜻일 뿐이다.
/// (실측 사례) `module.exports.params` 선언을 빠뜨리면 Cloud Code 가 파라미터를 걸러내서,
/// **배포는 성공하지만** 호출하면 params 가 비어 와 로직이 틀리게 동작한다.
/// 호출해 보지 않으면 절대 못 잡는 결함이다 — Unity 쪽 "컴파일 통과 ≠ 동작 정상"과 같은 격차.
///
/// 검증된 경로(docs/UGS-INVOKE-DESIGN.md):
///   ① 토큰 교환  POST services.api.unity.com/auth/v1/token-exchange?projectId&amp;environmentId
///                Authorization: Basic base64(keyId:secret)  →  { accessToken }   (~1시간)
///   ② 호출       POST cloud-code.services.api.unity.com/v1/projects/{pid}/scripts/{name}
///                Authorization: Bearer &lt;token&gt;,  body { "params": {...} }  →  { "output": {...} }
///
/// 비밀키는 토큰 교환 요청을 만들 때만 쓰고, 로그·예외 메시지에 절대 싣지 않는다.
/// </summary>
public sealed class UgsInvoker : IDisposable
{
    private const string TokenEndpoint = "https://services.api.unity.com/auth/v1/token-exchange";
    private const string CloudCodeEndpoint = "https://cloud-code.services.api.unity.com/v1/projects";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly string _projectId;
    private readonly string _environmentId;
    private readonly string _keyId;
    private readonly string _secret;

    private string? _token;
    private DateTime _tokenAcquiredUtc;

    public UgsInvoker(string projectId, string environmentId, string keyId, string secret)
    {
        _projectId = projectId;
        _environmentId = environmentId;
        _keyId = keyId;
        _secret = secret;
    }

    /// <summary>자격 증명이 환경에 있는지(없으면 호출 검증 자체가 불가).</summary>
    public static (string? KeyId, string? Secret) ReadCredentials() =>
        (Environment.GetEnvironmentVariable("UGS_CLI_SERVICE_KEY_ID"),
         Environment.GetEnvironmentVariable("UGS_CLI_SERVICE_SECRET_KEY"));

    // ── 검증 실행 ─────────────────────────────────────────────────────────────
    /// <summary>
    /// ASSERT 블록(JSON 배열)의 각 케이스를 호출해 검증한다.
    /// 반환: 실패 사유 목록(비어 있으면 통과).
    /// </summary>
    public async Task<IReadOnlyList<string>> VerifyAsync(string assertJson, CancellationToken ct)
    {
        List<InvokeCase> cases;
        try
        {
            cases = ParseCases(assertJson);
        }
        catch (Exception ex)
        {
            return new[] { $"The ASSERT block is not a valid JSON array: {ex.Message}" };
        }

        if (cases.Count == 0)
            return new[] { "The ASSERT block contains no verification cases." };

        var failures = new List<string>();
        foreach (var c in cases)
        {
            var (ok, message) = await RunCaseAsync(c, ct);
            if (!ok)
                failures.Add(message);
        }
        return failures;
    }

    private async Task<(bool Ok, string Message)> RunCaseAsync(InvokeCase c, CancellationToken ct)
    {
        var label = $"{c.Script}({c.ParamsJson})";

        HttpResponseMessage resp;
        string body;
        try
        {
            var token = await GetTokenAsync(ct);
            using var req = new HttpRequestMessage(
                HttpMethod.Post, $"{CloudCodeEndpoint}/{_projectId}/scripts/{c.Script}")
            {
                Content = new StringContent($"{{\"params\":{c.ParamsJson}}}", Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            resp = await _http.SendAsync(req, ct);
            body = await resp.Content.ReadAsStringAsync(ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return (false, $"{label} → invocation failed: {ex.Message}");
        }

        // 실패를 기대한 케이스
        if (c.ExpectError)
            return resp.IsSuccessStatusCode
                ? (false, $"{label} → expected an error but the call succeeded: {Trim(body)}")
                : (true, "");

        if (!resp.IsSuccessStatusCode)
            return (false, $"{label} → HTTP {(int)resp.StatusCode}: {Trim(body)}");

        // 성공 응답의 output 을 기대값과 부분 일치 비교
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("output", out var output))
                return (false, $"{label} → the response has no output field: {Trim(body)}");

            if (c.Expect is null)
                return (true, "");

            using var expectDoc = JsonDocument.Parse(c.Expect);
            var diffs = new List<string>();
            JsonSubset.Match(output, expectDoc.RootElement, "output", diffs);

            return diffs.Count == 0
                ? (true, "")
                : (false, $"{label} → {string.Join("; ", diffs)}");
        }
        catch (Exception ex)
        {
            return (false, $"{label} → could not parse the response: {ex.Message} / {Trim(body)}");
        }
    }

    // ── 토큰 ──────────────────────────────────────────────────────────────────
    // 토큰은 약 1시간 유효하다. 여유를 두고 50분마다 재발급한다.
    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_token is not null && DateTime.UtcNow - _tokenAcquiredUtc < TimeSpan.FromMinutes(50))
            return _token;

        var url = $"{TokenEndpoint}?projectId={_projectId}&environmentId={_environmentId}";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_keyId}:{_secret}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            // 비밀키가 섞일 수 있는 요청 내용은 절대 남기지 않는다 — 상태코드만.
            throw new InvalidOperationException(
                $"UGS token exchange failed (HTTP {(int)resp.StatusCode}). Check the service account key and the project/environment ids.");
        }

        using var doc = JsonDocument.Parse(body);
        _token = doc.RootElement.TryGetProperty("accessToken", out var t) ? t.GetString() : null;
        if (string.IsNullOrEmpty(_token))
            throw new InvalidOperationException("The UGS token exchange response has no accessToken.");

        _tokenAcquiredUtc = DateTime.UtcNow;
        return _token;
    }

    // ── ASSERT 파싱 ───────────────────────────────────────────────────────────
    private sealed record InvokeCase(string Script, string ParamsJson, string? Expect, bool ExpectError);

    private static List<InvokeCase> ParseCases(string assertJson)
    {
        using var doc = JsonDocument.Parse(assertJson.Trim());
        var root = doc.RootElement;

        // 케이스 하나만 준 경우도 허용한다.
        var items = root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray().ToList()
            : new List<JsonElement> { root };

        var cases = new List<InvokeCase>();
        foreach (var item in items)
        {
            var script = item.TryGetProperty("script", out var s) ? s.GetString() : null;
            if (string.IsNullOrWhiteSpace(script))
                throw new FormatException("Every case must have a \"script\" field.");

            var prms = item.TryGetProperty("params", out var p) ? p.GetRawText() : "{}";
            var expect = item.TryGetProperty("expect", out var e) ? e.GetRawText() : null;
            var expectError = item.TryGetProperty("expectError", out var x) && x.ValueKind == JsonValueKind.True;

            cases.Add(new InvokeCase(script!, prms, expect, expectError));
        }
        return cases;
    }

    private static string Trim(string s) =>
        s.Replace("\r", " ").Replace("\n", " ").Trim() is { Length: > 200 } long_
            ? long_[..200] + "…"
            : s.Replace("\r", " ").Replace("\n", " ").Trim();

    public void Dispose() => _http.Dispose();
}

/// <summary>
/// 기대값을 **부분 일치(subset)** 로 비교한다 — 명시한 키만 본다.
/// 응답에 타임스탬프·요청ID 같은 부가 필드가 붙어도 검증이 깨지지 않게 하기 위해서.
/// </summary>
public static class JsonSubset
{
    public static void Match(JsonElement actual, JsonElement expected, string path, List<string> diffs)
    {
        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                if (actual.ValueKind != JsonValueKind.Object)
                {
                    diffs.Add($"{path}: expected an object but got {Describe(actual)}");
                    return;
                }
                foreach (var prop in expected.EnumerateObject())
                {
                    if (!actual.TryGetProperty(prop.Name, out var child))
                        diffs.Add($"{path}.{prop.Name}: missing from the response");
                    else
                        Match(child, prop.Value, $"{path}.{prop.Name}", diffs);
                }
                return;

            case JsonValueKind.Array:
                if (actual.ValueKind != JsonValueKind.Array)
                {
                    diffs.Add($"{path}: expected an array but got {Describe(actual)}");
                    return;
                }
                var exp = expected.EnumerateArray().ToList();
                var act = actual.EnumerateArray().ToList();
                if (exp.Count != act.Count)
                {
                    diffs.Add($"{path}: expected length {exp.Count}, got {act.Count}");
                    return;
                }
                for (var i = 0; i < exp.Count; i++)
                    Match(act[i], exp[i], $"{path}[{i}]", diffs);
                return;

            default:
                if (!ScalarEquals(actual, expected))
                    diffs.Add($"{path}: expected {Describe(expected)}, got {Describe(actual)}");
                return;
        }
    }

    private static bool ScalarEquals(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind)
            return false;
        return a.ValueKind switch
        {
            JsonValueKind.Number => a.GetDouble().Equals(b.GetDouble()),
            JsonValueKind.String => a.GetString() == b.GetString(),
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => true,
            _ => a.GetRawText() == b.GetRawText(),
        };
    }

    private static string Describe(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => $"\"{e.GetString()}\"",
        JsonValueKind.Undefined => "nothing",
        _ => e.GetRawText(),
    };
}
