namespace XIVToolHub.App.Infrastructure;

/// <summary>コンボボックスに表示する値と表示名の組。</summary>
public sealed record Option<T>(T Value, string Label);
