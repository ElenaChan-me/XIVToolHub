using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using XIVToolHub.Core.Models;

namespace XIVToolHub.App.Services;

/// <summary>
/// 配色の切り替え。App.xaml の先頭の ResourceDictionary を差し替え、タイトルバーの色も合わせる。
/// </summary>
public static class ThemeManager
{
    // Windows 10 20H1 以降でタイトルバーを黒にする DWM の属性
    private const int DwmUseImmersiveDarkMode = 20;

    private static bool _hooked;

    public static string Current { get; private set; } = HubThemes.Default;

    public static void Apply(string theme)
    {
        Current = HubThemes.Normalize(theme);
        var resources = Application.Current.Resources.MergedDictionaries;
        var source = new Uri($"pack://application:,,,/XIVToolHub;component/Themes/{(Current == HubThemes.Light ? "Light" : "Dark")}.xaml");
        resources[0] = new ResourceDictionary { Source = source };

        if (!_hooked)
        {
            // 後から開くダイアログもタイトルバーを合わせる
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) => UpdateTitleBar((Window)s)));
            _hooked = true;
        }
        foreach (Window window in Application.Current.Windows)
        {
            UpdateTitleBar(window);
        }
    }

    private static void UpdateTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }
        var dark = Current == HubThemes.Dark ? 1 : 0;
        // 古い Windows では失敗するが、標準のタイトルバーのままで支障はない
        _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
