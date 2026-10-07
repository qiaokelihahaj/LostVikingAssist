using System.Runtime.InteropServices;
using System.IO;

namespace LostVikingAssist;

internal static class Native
{
    const string Casc = "CascLib.dll", Storm = "StormLib.dll";
    [DllImport(Casc, CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool CascOpenStorage(string path, uint locale, out IntPtr handle);
    [DllImport(Casc)] [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool CascCloseStorage(IntPtr handle);
    [DllImport(Casc, CharSet = CharSet.Ansi)]
    internal static extern IntPtr CascFindFirstFile(IntPtr storage, string mask, IntPtr data, IntPtr listFile);
    [DllImport(Casc)] [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool CascFindNextFile(IntPtr find, IntPtr data);
    [DllImport(Casc)] [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool CascFindClose(IntPtr find);
    [DllImport(Casc, CharSet = CharSet.Ansi)] [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool CascOpenFile(IntPtr storage, string name, uint locale, uint flags, out IntPtr file);
    [DllImport(Casc)] [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool CascGetFileSize64(IntPtr file, out ulong size);
    [DllImport(Casc)] [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool CascReadFile(IntPtr file, [Out] byte[] data, uint size, out uint read);
    [DllImport(Casc)] [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool CascCloseFile(IntPtr file);
    [DllImport(Casc)] internal static extern uint GetCascError();

    [DllImport(Storm, CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SFileCreateArchive(string path, uint flags, uint maxFiles, out IntPtr handle);
    [DllImport(Storm, CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SFileOpenArchive(string path, uint priority, uint flags, out IntPtr handle);
    [DllImport(Storm)] [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SFileCloseArchive(IntPtr handle);
    [DllImport(Storm, CharSet = CharSet.Ansi)] [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SFileOpenFileEx(IntPtr archive, string name, uint scope, out IntPtr file);
    [DllImport(Storm)] internal static extern uint SFileGetFileSize(IntPtr file, out uint high);
    [DllImport(Storm)] [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SFileReadFile(IntPtr file, [Out] byte[] data, uint size, out uint read, IntPtr overlapped);
    [DllImport(Storm)] [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SFileCloseFile(IntPtr file);
    [DllImport(Storm, CharSet = CharSet.Ansi)] [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SFileCreateFile(IntPtr archive, string name, ulong time, uint size, uint locale, uint flags, out IntPtr file);
    [DllImport(Storm)] [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SFileWriteFile(IntPtr file, byte[] data, uint size, uint compression);
    [DllImport(Storm)] [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SFileFinishFile(IntPtr file);
    [DllImport(Storm)] internal static extern uint SErrGetLastError();
}

internal sealed class CascStorage : IDisposable
{
    readonly IntPtr handle;
    internal const string Prefix = "campaigns\\liberty.sc2campaign\\base.sc2maps\\maps\\campaign\\tarcade.sc2map\\";
    public CascStorage(string root)
    {
        if (!Native.CascOpenStorage(root, 0xFFFFFFFF, out handle))
            throw new IOException($"无法打开客户端数据（CASC 错误 {Native.GetCascError()}）。请确认客户端已下载完整，或用编辑器另存地图后导入。");
    }
    public List<string> MapFiles()
    {
        var names = new List<string>();
        var data = Marshal.AllocHGlobal(1024);
        var find = IntPtr.Zero;
        try
        {
            find = Native.CascFindFirstFile(handle, Prefix + "*", data, IntPtr.Zero);
            if (find == IntPtr.Zero || find == new IntPtr(-1))
                throw new IOException("客户端中未找到 TArcade 地图，请先下载自由之翼战役内容。");
            do
            {
                string name = Marshal.PtrToStringAnsi(data) ?? "";
                if (name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) names.Add(name[Prefix.Length..]);
            } while (Native.CascFindNextFile(find, data));
            // CascLib 3.0 leaves ERROR_SUCCESS on normal enumeration exhaustion.
            if (Native.GetCascError() is not (0 or 18)) throw new IOException($"地图文件枚举未完成：{Native.GetCascError()}");
        }
        finally
        {
            if (find != IntPtr.Zero && find != new IntPtr(-1)) Native.CascFindClose(find);
            Marshal.FreeHGlobal(data);
        }
        return names.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
    }
    public byte[] Read(string relative)
    {
        if (!Native.CascOpenFile(handle, Prefix + relative, 0xFFFFFFFF, 0x10, out var file))
            throw new IOException($"无法读取地图文件 {relative}（{Native.GetCascError()}）。");
        try
        {
            if (!Native.CascGetFileSize64(file, out var size) || size > 128 * 1024 * 1024)
                throw new IOException($"地图文件未完整下载或大小异常：{relative}（{Native.GetCascError()}）。");
            var bytes = new byte[(int)size];
            if (size > 0 && (!Native.CascReadFile(file, bytes, (uint)size, out var read) || read != size))
                throw new IOException($"地图文件读取不完整：{relative}（{Native.GetCascError()}）。");
            return bytes;
        }
        finally { Native.CascCloseFile(file); }
    }
    public void Dispose() => Native.CascCloseStorage(handle);
}

internal sealed class MpqArchive : IDisposable
{
    readonly IntPtr handle;
    public MpqArchive(string path, bool create = false, int count = 128, bool writable = false)
    {
        bool ok = create ? Native.SFileCreateArchive(path, 0x00100000, (uint)Math.Max(count * 2, 128), out handle)
                         : Native.SFileOpenArchive(path, 0, writable ? 0u : 0x100u, out handle);
        if (!ok) throw new IOException($"无法{(create ? "创建" : "打开")}地图 MPQ（{Native.SErrGetLastError()}）：{path}");
    }
    public byte[] Read(string name)
    {
        if (!Native.SFileOpenFileEx(handle, name, 0, out var file))
            throw new IOException($"地图缺少 {name}（{Native.SErrGetLastError()}）。");
        try
        {
            uint size = Native.SFileGetFileSize(file, out var high);
            if (high != 0 || size > 128 * 1024 * 1024) throw new IOException($"地图文件大小异常：{name}");
            var bytes = new byte[(int)size];
            if (size > 0 && (!Native.SFileReadFile(file, bytes, size, out var read, IntPtr.Zero) || read != size))
                throw new IOException($"地图文件读取不完整：{name}");
            return bytes;
        }
        finally { Native.SFileCloseFile(file); }
    }
    public void Write(string name, byte[] bytes)
    {
        if (!Native.SFileCreateFile(handle, name, 0, (uint)bytes.Length, 0, 0x80000200, out var file))
            throw new IOException($"地图文件写入失败：{name}（{Native.SErrGetLastError()}）");
        bool written = bytes.Length == 0 || Native.SFileWriteFile(file, bytes, (uint)bytes.Length, 2);
        bool finished = Native.SFileFinishFile(file);
        if (!written || !finished) throw new IOException($"地图文件写入未完成：{name}");
    }
    public void Dispose()
    {
        if (!Native.SFileCloseArchive(handle)) throw new IOException("关闭地图时写入失败，请检查磁盘空间。");
    }
}
