using System.Text.RegularExpressions;
using Orchestrator.Contracts;

namespace Orchestrator.Util;

/// <summary>
/// 백엔드 응답 텍스트 → <see cref="FileEdit"/> 목록 파서.
/// 출력 형식은 시스템 프롬프트로 강제한다(DESIGN.md §4 "출력 정규화"):
///
///   FILE: Assets/Scripts/Health.cs
///   ```csharp
///   ...전체 파일 내용...
///   ```
///
/// 형식이 백엔드 무관하므로 파서를 여기 두어 모든 백엔드가 재사용한다.
/// </summary>
public static partial class EditParser
{
    // FILE: <경로> 다음 줄에 오는 펜스 코드블록의 본문을 캡처.
    // Singleline: 본문(.*?)이 개행을 포함하도록. 경로/펜스 언어는 개행 제외.
    [GeneratedRegex(
        @"FILE:[ \t]*(?<path>[^\r\n]+?)[ \t]*\r?\n```[^\r\n]*\r?\n(?<body>.*?)\r?\n```",
        RegexOptions.Singleline)]
    private static partial Regex FileBlockRegex();

    // ASSERT: 다음 줄에 오는 펜스 코드블록 = 플레이모드 런타임 검증 스니펫.
    [GeneratedRegex(
        @"ASSERT:[ \t]*\r?\n```[^\r\n]*\r?\n(?<body>.*?)\r?\n```",
        RegexOptions.Singleline)]
    private static partial Regex AssertBlockRegex();

    /// <summary>응답에서 ASSERT 블록(런타임 검증 스니펫)을 뽑는다. 없으면 null.</summary>
    public static string? ParseAssert(string text)
    {
        var m = AssertBlockRegex().Match(text);
        if (!m.Success)
            return null;
        var body = m.Groups["body"].Value.Trim();
        return body.Length == 0 ? null : body;
    }

    // PERF: 다음 줄에 오는 펜스 블록 = 성능 예산 명세(JSON).
    [GeneratedRegex(
        @"PERF:[ \t]*\r?\n```[^\r\n]*\r?\n(?<body>.*?)\r?\n```",
        RegexOptions.Singleline)]
    private static partial Regex PerfBlockRegex();

    /// <summary>응답에서 PERF 블록(성능 예산 명세)을 뽑는다. 없으면 null.</summary>
    public static string? ParsePerf(string text)
    {
        var m = PerfBlockRegex().Match(text);
        if (!m.Success)
            return null;
        var body = m.Groups["body"].Value.Trim();
        return body.Length == 0 ? null : body;
    }

    public static IReadOnlyList<FileEdit> Parse(string text)
    {
        var edits = new List<FileEdit>();
        foreach (Match m in FileBlockRegex().Matches(text))
        {
            var path = Normalize(m.Groups["path"].Value.Trim());
            var body = m.Groups["body"].Value;
            if (path.Length == 0)
                continue;
            edits.Add(new FileEdit(path, body));
        }
        return edits;
    }

    // 경로를 프로젝트 상대 형식으로 정리: 역슬래시 → 슬래시, 앞쪽 ./ 및 / 제거.
    private static string Normalize(string path)
    {
        path = path.Replace('\\', '/').Trim();
        while (path.StartsWith("./", StringComparison.Ordinal))
            path = path[2..];
        return path.TrimStart('/');
    }
}
