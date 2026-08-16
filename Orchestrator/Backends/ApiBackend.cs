using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Orchestrator.Contracts;
using Orchestrator.Util;

namespace Orchestrator.Backends;

/// <summary>
/// Anthropic Messages API 직통 백엔드 (Phase 1 기본 두뇌).
///
/// 왜 SDK가 아니라 HttpClient 직통인가:
///   - DESIGN.md D1/D3 — 백엔드는 "텍스트 생성기"일 뿐. 의존성 0 · 와이어 포맷 투명 ·
///     버전 드리프트 없음 → "버그가 루프에 있음이 명확"하고 포폴 리뷰어가 전부 읽을 수 있다.
///   - 나중에 ClaudeCodeBackend/CodexBackend 는 이 인터페이스만 맞추면 갈아끼워진다(D3).
///
/// 키는 레포에 두지 않는다 — 환경변수 ANTHROPIC_API_KEY 에서만 읽는다(CLAUDE.md).
/// </summary>
public sealed class ApiBackend : IAgentBackend, IDisposable
{
    private const string Endpoint = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    private readonly HttpClient _http;
    private readonly string _model;
    private readonly int _maxTokens;

    public string Name => $"api:{_model}";

    public ApiBackend(string apiKey, string model, int maxTokens = 16000)
    {
        _model = model;
        _maxTokens = maxTokens;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", AnthropicVersion);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<AgentReply> CompleteAsync(AgentContext context, CancellationToken ct)
    {
        var payload = new
        {
            model = _model,
            max_tokens = _maxTokens,
            system = context.System,
            messages = context.History.Select(t => new
            {
                role = t.Role == Role.User ? "user" : "assistant",
                content = t.Content,
            }).ToArray(),
        };

        using var resp = await _http.PostAsJsonAsync(Endpoint, payload, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Anthropic API {(int)resp.StatusCode}: {body}");

        var text = ExtractText(body);
        var edits = EditParser.Parse(text);
        return new AgentReply(text, edits);
    }

    // 응답 content 배열에서 text 블록만 이어붙인다.
    // (thinking 블록 등은 무시 — 우리에게 필요한 건 파일 편집을 담은 텍스트뿐)
    private static string ExtractText(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("stop_reason", out var stop) &&
            stop.GetString() == "refusal")
        {
            throw new InvalidOperationException("The Anthropic API refused the request.");
        }

        var sb = new StringBuilder();
        if (root.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type) &&
                    type.GetString() == "text" &&
                    block.TryGetProperty("text", out var t))
                {
                    sb.Append(t.GetString());
                }
            }
        }
        return sb.ToString();
    }

    public void Dispose() => _http.Dispose();
}
