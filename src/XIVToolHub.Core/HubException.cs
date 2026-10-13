namespace XIVToolHub.Core;

/// <summary>
/// 利用者に理由を伝えるべき失敗。メッセージはそのまま画面に表示する前提で書く。
/// </summary>
public sealed class HubException : Exception
{
    public HubException(string message) : base(message)
    {
    }

    public HubException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public HubException() : base("処理に失敗しました。")
    {
    }
}
