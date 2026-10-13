using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using XIVToolHub.Core;
using XIVToolHub.Core.GitHub;

namespace XIVToolHub.App.Services;

/// <summary>
/// GitHub トークンを Windows の資格情報マネージャーに保存する。ファイルに平文で残さないため。
/// </summary>
public sealed partial class CredentialTokenStore : ITokenStore
{
    private const string TargetName = "XIVToolHub:github-token";
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int MaxTokenLength = 1024;

    public string? Get()
    {
        if (!CredReadW(TargetName, CredTypeGeneric, 0, out var handle))
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == ErrorNotFound)
            {
                return null;
            }
            throw new HubException("資格情報マネージャーからトークンを読み込めませんでした。", new Win32Exception(error));
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(handle);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return null;
            }
            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            CredFree(handle);
        }
    }

    public void Set(string token)
    {
        var trimmed = token?.Trim() ?? "";
        if (trimmed.Length == 0 || trimmed.Length > MaxTokenLength || trimmed.Any(char.IsWhiteSpace))
        {
            throw new HubException("トークンの形式が不正です。");
        }

        var bytes = Encoding.Unicode.GetBytes(trimmed);
        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = TargetName,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                UserName = "github",
            };
            if (!CredWriteW(ref credential, 0))
            {
                throw new HubException("資格情報マネージャーにトークンを保存できませんでした。", new Win32Exception(Marshal.GetLastPInvokeError()));
            }
        }
        finally
        {
            // メモリ上にトークンを残さない
            Marshal.Copy(new byte[bytes.Length], 0, blob, bytes.Length);
            Marshal.FreeHGlobal(blob);
        }
    }

    public void Clear()
    {
        if (!CredDeleteW(TargetName, CredTypeGeneric, 0))
        {
            var error = Marshal.GetLastPInvokeError();
            if (error != ErrorNotFound)
            {
                throw new HubException("資格情報マネージャーからトークンを削除できませんでした。", new Win32Exception(error));
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWriteW(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDeleteW(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void CredFree(IntPtr buffer);
}
