using System.Windows;
using XIVToolHub.Core.Models;

namespace XIVToolHub.App.Views;

public partial class LanguageWindow : Window
{
    public LanguageWindow(IReadOnlyList<ToolDefinition> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        InitializeComponent();
        ToolsText.Text = string.Join("\n", tools.Select(t => "・" + t.Name));
        // 全ツールが対応している言語だけを候補にする
        LanguageBox.ItemsSource = ToolLanguages.All.Where(l => tools.All(t => t.Languages.Contains(l.Code))).ToList();
        LanguageBox.SelectedIndex = 0;
    }

    public string? Result { get; private set; }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        Result = LanguageBox.SelectedValue as string;
        if (Result is not null)
        {
            DialogResult = true;
        }
    }
}
