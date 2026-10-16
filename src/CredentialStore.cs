using System.Runtime.InteropServices;
using System.Text;

namespace ChatGPTRevit;

/// <summary>Windows Credential Manager; secrets are never written into project/settings files.</summary>
public static class CredentialStore
{
    private static string Target(string provider) => "BIMCopiloto." + (provider == "Gemini" ? "Gemini" : "OpenAI");
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias, UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Write(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Read(string target, uint type, uint flags, out IntPtr pointer);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr pointer);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Delete(string target, uint type, uint flags);
    public static void Save(string key)
        => Save("OpenAI", key);

    public static void Save(string provider, string key)
    {
        key = key.Trim();
        if (provider == "OpenAI" && (!key.StartsWith("sk-", StringComparison.Ordinal) || key.Length < 30))
            throw new InvalidOperationException("Informe uma chave válida da OpenAI Platform.");
        if (provider == "Gemini" && (key.Length < 20 || key.Any(char.IsWhiteSpace)))
            throw new InvalidOperationException("Informe uma chave válida do Google AI Studio/Gemini API.");
        var data = Encoding.Unicode.GetBytes(key.Trim());
        var pointer = Marshal.AllocHGlobal(data.Length);
        try
        {
            Marshal.Copy(data, 0, pointer, data.Length);
            var credential = new Credential { Type = 1, TargetName = Target(provider), UserName = provider,
                CredentialBlobSize = (uint)data.Length, CredentialBlob = pointer, Persist = 2 };
            if (!Write(ref credential, 0)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            Array.Clear(data); Marshal.Copy(data, 0, pointer, data.Length); Marshal.FreeHGlobal(pointer);
        }
    }
    public static string? Get()
        => Get("OpenAI");

    public static string? Get(string provider)
    {
        if (!Read(Target(provider), 1, 0, out var p)) return null;
        try
        {
            var c = Marshal.PtrToStructure<Credential>(p);
            return Marshal.PtrToStringUni(c.CredentialBlob, (int)c.CredentialBlobSize / 2);
        }
        finally { CredFree(p); }
    }
    public static void Remove()
        => Remove("OpenAI");

    public static void Remove(string provider)
    {
        if (!Delete(Target(provider), 1, 0) && Marshal.GetLastWin32Error() != 1168)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
}
