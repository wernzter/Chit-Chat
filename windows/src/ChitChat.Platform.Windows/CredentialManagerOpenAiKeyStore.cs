using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;
using ChitChat.Abstractions;

namespace ChitChat.Platform.Windows;

/// <summary>Stores the OpenAI API key in Windows Credential Manager for the current Windows account.</summary>
public sealed class CredentialManagerOpenAiKeyStore : IOpenAiKeyStore
{
    private const string Target = "ChitChat/OpenAI";
    private const uint Generic = 1;
    private const uint LocalMachine = 2;
    private const int NotFound = 1168;
    private const int MaxCredentialBytes = 2560;

    /// <inheritdoc />
    public string? Read()
    {
        if (!CredRead(Target, Generic, 0, out var handle))
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == NotFound) return null;
            throw new Win32Exception(error);
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(handle);
            if (credential.BlobSize == 0 || credential.Blob == 0) return null;
            if (credential.BlobSize > MaxCredentialBytes) throw new InvalidOperationException("Invalid credential size.");

            var bytes = new byte[credential.BlobSize];
            try
            {
                Marshal.Copy(credential.Blob, bytes, 0, bytes.Length);
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
                Marshal.Copy(bytes, 0, credential.Blob, bytes.Length);
            }
        }
        finally
        {
            CredFree(handle);
        }
    }

    /// <inheritdoc />
    public void Save(string key)
    {
        var count = Encoding.UTF8.GetByteCount(key);
        if (count is 0 or > MaxCredentialBytes) throw new ArgumentOutOfRangeException(nameof(key));

        var bytes = Encoding.UTF8.GetBytes(key);
        var pin = default(GCHandle);
        try
        {
            pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            var credential = new Credential
            {
                Type = Generic,
                TargetName = Target,
                BlobSize = (uint)bytes.Length,
                Blob = pin.AddrOfPinnedObject(),
                Persist = LocalMachine,
                UserName = "Chit-Chat",
            };
            if (!CredWrite(ref credential, 0)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            if (pin.IsAllocated) pin.Free();
        }
    }

    /// <inheritdoc />
    public void Remove()
    {
        if (CredDelete(Target, Generic, 0)) return;
        var error = Marshal.GetLastPInvokeError();
        if (error != NotFound) throw new Win32Exception(error);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        internal uint Flags;
        internal uint Type;
        internal string? TargetName;
        internal string? Comment;
        internal FILETIME LastWritten;
        internal uint BlobSize;
        internal nint Blob;
        internal uint Persist;
        internal uint AttributeCount;
        internal nint Attributes;
        internal string? TargetAlias;
        internal string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string targetName, uint type, uint flags, out nint credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string targetName, uint type, uint flags);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void CredFree(nint credential);
}
