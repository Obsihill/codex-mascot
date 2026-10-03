namespace CodexMascot.App;

public sealed record CodexThreadInfo(string Id, string Preview, string? Cwd, string Status)
{
    public string DisplayName => $"{Preview}  ·  {Status}  ·  {Cwd ?? Loc.T("경로 없음")}  ·  {Id[..Math.Min(Id.Length, 12)]}";
}
