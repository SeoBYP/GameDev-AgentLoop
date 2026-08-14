namespace Orchestrator.Util;

/// <summary>
/// `.env` 파일을 읽어 **현재 프로세스의 환경변수**로 올린다.
///
/// 왜 필요한가: 자격 증명(UGS 서비스 계정 키 등)을 OS 전역 환경변수로 심지 않고
/// 레포 루트 파일 하나로 관리하기 위해서. `.gitignore` 가 `.env` 를 막고 있어 커밋 사고도 없다.
///
/// 왜 이걸로 `ugs` CLI 까지 커버되나: 오케스트레이터가 `ugs` 를 **자식 프로세스**로 띄우는데,
/// 자식은 부모의 환경을 그대로 물려받는다. 즉 여기서 올려두면 REST 호출(우리)과 CLI 호출(자식) 모두 같은 자격을 쓴다.
///
/// 규칙:
///   - `KEY=VALUE`, `#` 주석, 빈 줄. 값의 양끝 따옴표는 벗긴다. `export ` 접두사 허용.
///   - **이미 설정된 환경변수는 덮어쓰지 않는다** — 명시적으로 export 한 값이 항상 이긴다.
/// 값은 로그로 남기지 않는다(비밀이 섞이므로 키 이름만 보고한다).
/// </summary>
public static class DotEnv
{
    /// <summary>지정한 파일을 로드하고, 올린 키 이름 목록을 돌려준다(값은 반환하지 않는다).</summary>
    public static IReadOnlyList<string> Load(string path)
    {
        if (!File.Exists(path))
            return Array.Empty<string>();

        var applied = new List<string>();
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (line.StartsWith("export ", StringComparison.Ordinal))
                line = line["export ".Length..].TrimStart();

            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();

            // 값 양끝의 따옴표 제거 (KEY="v" / KEY='v')
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
                value = value[1..^1];

            // 이미 있는 값이 우선(명시적 export > .env)
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                continue;

            Environment.SetEnvironmentVariable(key, value);
            applied.Add(key);
        }
        return applied;
    }
}
