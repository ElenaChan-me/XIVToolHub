using System.Text;

namespace XIVToolHub.Core.Storage;

/// <summary>
/// 書き込み途中で失敗しても元のファイルが壊れないよう、一時ファイルに書いてから置き換える。
/// </summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string content)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("保存先のフォルダを特定できません。", nameof(path));
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $"{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
