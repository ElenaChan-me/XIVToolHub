namespace XIVToolHub.Core.GitHub;

/// <summary>
/// GitHub トークンの保管先。平文のファイルには保存しない。
/// </summary>
public interface ITokenStore
{
    string? Get();

    void Set(string token);

    void Clear();
}
