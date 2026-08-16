using System.Diagnostics;

namespace Orchestrator.Trace;

/// <summary>
/// span 트리를 쌓아 <see cref="RunStore"/> 에 흘려보낸다.
///
/// 실행기가 발행한다 — **노드가 아니라.** 노드는 자기 판단만 하고,
/// 타이밍·기록·부모 관계는 여기가 소유한다(ARCHITECTURE §5.2).
///
/// 열려 있는 자식은 부모가 닫힐 때 함께 닫힌다. 루프가 `continue` 로 빠져나가도
/// span 이 유실되지 않게 하기 위해서다.
/// </summary>
public sealed class RunTrace
{
    private readonly RunStore _store;
    private readonly List<SpanScope> _open = new();
    private int _counter;

    public string RunId => _store.RunId;
    public string Root => _store.Root;

    public RunTrace(RunStore store) => _store = store;

    public SpanScope Begin(SpanKind kind, string name)
    {
        var id = $"s{++_counter:D3}";
        var parent = _open.Count > 0 ? _open[^1].Id : null;
        var scope = new SpanScope(this, id, parent, kind, name);
        _open.Add(scope);
        return scope;
    }

    /// <summary>산출물을 span 폴더에 남기고 상대 경로를 돌려준다.</summary>
    public string? WriteArtifact(string spanId, string fileName, string content)
        => _store.WriteArtifact(spanId, fileName, content);

    internal void Close(SpanScope scope)
    {
        var index = _open.IndexOf(scope);
        if (index < 0)
            return;   // 이미 닫혔다

        // 열린 채로 남은 자식들을 **기록하면서** 먼저 닫는다(안쪽부터).
        // 루프가 `continue` 로 빠져나가도 span 이 유실되지 않아야 한다.
        for (var i = _open.Count - 1; i > index; i--)
        {
            _open[i].MarkClosed();
            _store.Append(_open[i].ToSpan(RunId));
        }

        _open.RemoveRange(index, _open.Count - index);
        _store.Append(scope.ToSpan(RunId));
    }
}

/// <summary>한 span 의 수명. 결과를 정하지 않고 닫히면 <see cref="SpanOutcome.Pass"/> 로 본다.</summary>
public sealed class SpanScope : IDisposable
{
    private readonly RunTrace _trace;
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private readonly List<string> _artifacts = new();
    private bool _closed;

    public string   Id { get; }
    public string?  ParentId { get; }
    public SpanKind Kind { get; }
    public string   Name { get; }

    private SpanOutcome _outcome = SpanOutcome.Pass;
    private string? _log;
    private string? _blamedOn;
    private IReadOnlyList<string>? _errors;

    internal SpanScope(RunTrace trace, string id, string? parentId, SpanKind kind, string name)
    {
        _trace = trace;
        Id = id;
        ParentId = parentId;
        Kind = kind;
        Name = name;
    }

    public SpanScope Pass(string? log = null)                { _outcome = SpanOutcome.Pass; _log = log; return this; }
    public SpanScope Skip(string why)                        { _outcome = SpanOutcome.Skip; _log = why; return this; }
    public SpanScope Fatal(string message)                   { _outcome = SpanOutcome.Fatal; _log = message; return this; }
    public SpanScope Blocked(string by, string? log = null)  { _outcome = SpanOutcome.Blocked; _blamedOn = by; _log = log; return this; }

    public SpanScope Fail(IReadOnlyList<string>? errors = null, string? log = null)
    {
        _outcome = SpanOutcome.Fail;
        _errors = errors;
        _log = log;
        return this;
    }

    /// <summary>큰 원문을 파일로 남긴다 — 모델에는 요약만 가고, 사람은 전문을 본다.</summary>
    public SpanScope Artifact(string fileName, string content)
    {
        var rel = _trace.WriteArtifact(Id, fileName, content);
        if (rel is not null)
            _artifacts.Add(rel);
        return this;
    }

    internal Span ToSpan(string runId) => new(
        RunId: runId,
        SpanId: Id,
        ParentSpanId: ParentId,
        Kind: Kind,
        Name: Name,
        Outcome: _outcome,
        Ms: Math.Round(_sw.Elapsed.TotalMilliseconds, 1),
        Log: _log,
        BlamedOn: _blamedOn,
        Errors: _errors is { Count: > 0 } ? _errors : null,
        Artifacts: _artifacts.Count > 0 ? _artifacts : null);

    /// <summary>부모가 대신 기록해 줄 때 — 이후 Dispose 는 아무 일도 하지 않는다.</summary>
    internal void MarkClosed() => _closed = true;

    public void Dispose()
    {
        if (_closed)
            return;
        _closed = true;
        _trace.Close(this);
    }
}
