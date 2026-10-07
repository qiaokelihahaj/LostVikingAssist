using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LostVikingAssist;

internal sealed record AssistOptions(bool InfiniteBombs = true, bool Invulnerable = true);
internal sealed record ScriptStatus(bool InfiniteBombs, bool Invulnerable);
internal sealed record BuildResult(string Map, string Report, string SourceVersion, int Files, int SkippedLocales);
internal sealed record Receipt(int Schema, string Root, string InstalledHash, string? PendingHash,
    bool HadOriginal, string? OriginalHash);

internal static class ScriptPatch
{
    // Mask comments and string literals while preserving offsets. Never match code in comments.
    static string Code(string source) => Regex.Replace(source,
        "\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|//[^\\r\\n]*|/\\*[\\s\\S]*?\\*/",
        m => new string(' ', m.Length));

    static (int Start, int Length) Span(string source, string function)
    {
        var code = Code(source);
        var matches = Regex.Matches(code, @"\b(?:bool|void|int|fixed)\s+" + Regex.Escape(function) + @"\s*\([^)]*\)\s*\{");
        if (matches.Count != 1) throw new InvalidDataException($"目标函数 {function} 不唯一或不存在，拒绝修改此版本。");
        int start = matches[0].Index, brace = code.IndexOf('{', start), depth = 1, end = brace + 1;
        for (; end < code.Length && depth > 0; end++)
        {
            if (code[end] == '{') depth++;
            if (code[end] == '}') depth--;
        }
        if (depth != 0) throw new InvalidDataException("地图脚本括号不完整。");
        return (start, end - start);
    }

    static string Set(string source, string function, string pattern, string value, string label)
    {
        var span = Span(source, function);
        string body = source.Substring(span.Start, span.Length);
        var matches = Regex.Matches(Code(body), pattern);
        if (matches.Count != 1) throw new InvalidDataException($"{label}动作不唯一或不匹配，拒绝修改此版本。");
        var group = matches[0].Groups["value"];
        string changed = body[..group.Index] + value + body[(group.Index + group.Length)..];
        return source[..span.Start] + changed + source[(span.Start + span.Length)..];
    }
    const string BombPattern = @"\bgv_bombCount\s*-=\s*(?<value>[01])\s*;";
    const string InvulnerablePattern = @"\blibNtve_gf_MakeUnitInvulnerable\s*\(\s*gv_viking\s*,\s*(?<value>true|false)\s*\)\s*;";
    public static string Apply(string source, AssistOptions options)
    {
        // Identify this map before making either edit, and protect the original score function.
        var score = Span(source, "gf_AddScore");
        string scoreBefore = source.Substring(score.Start, score.Length);
        foreach (var id in new[] { "LostVikingBronze", "LostVikingSilver", "LostVikingGold" })
            if (!scoreBefore.Contains('"' + id + '"')) throw new InvalidDataException("原版维京成就检查未找到，拒绝修改。");
        string result = Set(source, "gt_FighterBombKeyDown_Func", BombPattern, options.InfiniteBombs ? "0" : "1", "炸弹消耗");
        result = Set(result, "gt_SpawnViking_Func", InvulnerablePattern, options.Invulnerable ? "true" : "false", "飞船无敌");
        var after = Span(result, "gf_AddScore");
        if (scoreBefore != result.Substring(after.Start, after.Length)) throw new InvalidDataException("成就检查发生意外变化。");
        return result;
    }
    public static ScriptStatus Inspect(string source)
    {
        // Applying both configurations validates the supported structure and score checks.
        Apply(source, new());
        var bomb = Span(source, "gt_FighterBombKeyDown_Func");
        var inv = Span(source, "gt_SpawnViking_Func");
        return new(Regex.Match(Code(source.Substring(bomb.Start, bomb.Length)), BombPattern).Groups["value"].Value == "0",
            Regex.Match(Code(source.Substring(inv.Start, inv.Length)), InvulnerablePattern).Groups["value"].Value == "true");
    }
}

internal static class AssistService
{
    static readonly UTF8Encoding Utf8 = new(false, true);
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static string Decode(byte[] bytes) => Utf8.GetString(bytes);
    public static string Hash(string path) { using var f = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(f)); }
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static string DefaultOutput => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LostVikingAssist", "Builds");

    public static string? DetectGame()
    {
        var candidates = new List<string>();
        foreach (var basePath in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"D:\Games", @"D:\", @"E:\Games" })
            candidates.Add(Path.Combine(basePath, "StarCraft II"));
        foreach (var hive in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
        foreach (var prefix in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\StarCraft II",
                     @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\StarCraft II" })
        {
            using var key = hive.OpenSubKey(prefix);
            if (key?.GetValue("InstallLocation") is string location) candidates.Insert(0, location);
        }
        return candidates.FirstOrDefault(IsGame);
    }
    public static bool IsGame(string path) => File.Exists(Path.Combine(path, "StarCraft II.exe")) && File.Exists(Path.Combine(path, ".build.info"));
    public static string Version(string root)
    {
        var lines = File.ReadAllLines(Path.Combine(root, ".build.info"));
        int version = Array.FindIndex(lines[0].Split('|'), s => s.StartsWith("Version!"));
        int active = Array.FindIndex(lines[0].Split('|'), s => s.StartsWith("Active!"));
        return lines.Skip(1).Select(s => s.Split('|')).Where(s => s.Length > Math.Max(version, active) && active >= 0 && s[active] == "1")
            .Select(s => version >= 0 ? s[version] : "未知").FirstOrDefault() ?? "未知";
    }
    static void Required(IReadOnlyDictionary<string, byte[]> files)
    {
        foreach (string name in new[] { "mapscript.galaxy", "documentinfo", "documentheader", "mapinfo", "componentlist.sc2components", "t3terrain.xml" })
            if (!files.ContainsKey(name)) throw new InvalidDataException($"地图不完整：缺少 {name}。");
    }
    public static BuildResult BuildFromClient(string root, string output, AssistOptions options, Action<string>? log = null)
    {
        if (!IsGame(root)) throw new DirectoryNotFoundException("请选择包含 StarCraft II.exe 和 .build.info 的游戏安装目录。");
        root = Path.GetFullPath(root);
        log?.Invoke("正在只读打开客户端数据…");
        using var storage = new CascStorage(root);
        var names = storage.MapFiles();
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var skipped = new List<string>();
        foreach (var name in names)
        {
            try { files.Add(name, storage.Read(name)); }
            catch (IOException) when (Regex.IsMatch(name, @"^[a-z]{4}\.sc2data\\localizeddata\\", RegexOptions.IgnoreCase))
            { skipped.Add(name); }
        }
        Required(files);
        // Localized data may be absent for languages not installed. At least one UI locale is required.
        if (!files.Keys.Any(s => s.EndsWith(@"\localizeddata\gamestrings.txt", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("没有可读取的小游戏界面语言文件，请在战网完成下载。");
        return Build(files, null, output, options, Version(root), root, skipped, log);
    }
    public static BuildResult BuildFromMap(string source, string output, AssistOptions options, Action<string>? log = null)
    {
        source = Path.GetFullPath(source);
        if (!File.Exists(source) || !source.EndsWith(".SC2Map", StringComparison.OrdinalIgnoreCase))
            throw new IOException("请选择编辑器另存的完整 .SC2Map 文件。");
        using var map = new MpqArchive(source);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        // Import preserves the entire archive rather than relying on a potentially incomplete listfile.
        files["mapscript.galaxy"] = map.Read("MapScript.galaxy");
        foreach (var name in new[] { "DocumentInfo", "DocumentHeader", "MapInfo", "ComponentList.SC2Components", "t3Terrain.xml" }) map.Read(name);
        return Build(files, source, output, options, "导入地图", source, [], log);
    }
    static BuildResult Build(Dictionary<string, byte[]> files, string? originalMap, string output,
        AssistOptions options, string version, string source, List<string> skipped, Action<string>? log)
    {
        Directory.CreateDirectory(output);
        string folder = Path.Combine(Path.GetFullPath(output), DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(folder);
        byte[] original = files["mapscript.galaxy"];
        byte[] patched = Utf8.GetBytes(ScriptPatch.Apply(Decode(original), options));
        files["mapscript.galaxy"] = patched;
        log?.Invoke("两处补丁已定位，原版成就检查保持一致。");
        string path = Path.Combine(folder, "LostViking_Assist.SC2Map");
        try
        {
            if (originalMap is null)
            {
                using var map = new MpqArchive(path, create: true, count: files.Count);
                foreach (var entry in files) map.Write(entry.Key, entry.Value);
            }
            else
            {
                File.Copy(originalMap, path);
                using var map = new MpqArchive(path, writable: true);
                map.Write("MapScript.galaxy", patched);
            }
            using (var check = new MpqArchive(path))
            {
                if (!check.Read("MapScript.galaxy").SequenceEqual(patched)) throw new IOException("补丁回读校验失败。");
                foreach (var entry in files)
                    if (!check.Read(entry.Key).SequenceEqual(entry.Value)) throw new IOException($"地图回读不一致：{entry.Key}");
            }
            File.WriteAllBytes(Path.Combine(folder, "Original.MapScript.galaxy"), original);
            File.WriteAllBytes(Path.Combine(folder, "Patched.MapScript.galaxy"), patched);
            string report = Path.Combine(folder, "build-report.json");
            File.WriteAllText(report, JsonSerializer.Serialize(new {
                schema = 1, source, sourceVersion = version, createdUtc = DateTimeOffset.UtcNow,
                options, mapSha256 = Hash(path), originalScriptSha256 = Hash(original), patchedScriptSha256 = Hash(patched),
                mapFiles = originalMap is null ? files.Keys.ToArray() : null,
                skippedUnavailableLocaleFiles = skipped, scoreAndAchievementFunctionUnchanged = true,
                runtimeScriptOnly = true, editorResaveWillRegenerateOriginalScript = true,
                officialAchievementVerified = false
            }, Json), Utf8);
            log?.Invoke($"完整地图已生成并通过回读校验：{path}");
            return new(path, report, version, originalMap is null ? files.Count : -1, skipped.Count);
        }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
    }
    public static ScriptStatus InspectMap(string path)
    { using var map = new MpqArchive(path); return ScriptPatch.Inspect(Decode(map.Read("MapScript.galaxy"))); }

    static void EnsureStopped()
    {
        var processes = Process.GetProcesses();
        try
        {
            if (processes.Any(p => p.ProcessName is "SC2" or "SC2_x64" or "SC2Editor" or "SC2Editor_x64" || p.ProcessName.StartsWith("StarCraft II", StringComparison.OrdinalIgnoreCase)))
                throw new IOException("请先完全退出《星际争霸 II》和地图编辑器，再安装或恢复。无需关闭战网。");
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    static string Campaign(string root)
    {
        if (!IsGame(root)) throw new IOException("游戏目录无效。");
        var directory = Path.Combine(Path.GetFullPath(root), "Maps", "Campaign");
        // Keep writes within the selected install directory, including when existing folders are junctions.
        SafeDirectory(directory);
        return directory;
    }
    static void SafeDirectory(string directory)
    {
        for (var cursor = new DirectoryInfo(directory); cursor is not null; cursor = cursor.Parent)
            if (cursor.Exists && cursor.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("安装路径含符号链接或目录联接，请选择真实安装目录。");
    }
    static Receipt LoadReceipt(string state, string root)
    {
        string path = Path.Combine(state, "receipt.json");
        var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllText(path)) ?? throw new IOException("备份记录损坏。");
        if (receipt.Schema != 1 || !string.Equals(receipt.Root, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
            throw new IOException("备份记录与所选游戏目录不一致。");
        return receipt;
    }
    static void AtomicText(string path, string text)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, text, Utf8); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    static void AtomicCopy(string source, string target)
    {
        string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(source, temp);
            if (Hash(source) != Hash(temp)) throw new IOException("复制校验失败。");
            File.Move(temp, target, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    static void VerifyCurrent(string target, Receipt receipt)
    {
        if (!File.Exists(target)) throw new IOException("覆盖地图已被移走，停止自动恢复。请检查备份记录。");
        string hash = Hash(target);
        bool initial = receipt.HadOriginal && hash == receipt.OriginalHash;
        if (hash != receipt.InstalledHash && hash != receipt.PendingHash && !initial)
            throw new IOException("覆盖地图已被其他程序修改。为保留这份修改，工具不会覆盖；请先手动移走并保存该文件。");
    }
    public static string Install(string root, string source)
    {
        EnsureStopped();
        return InstallFiles(root, source);
    }
    internal static string InstallFiles(string root, string source)
    {
        var status = InspectMap(source);
        if (!status.InfiniteBombs && !status.Invulnerable) throw new IOException("此地图没有启用任何辅助选项。");
        string campaign = Campaign(root), state = Path.Combine(campaign, ".LostVikingAssist");
        SafeDirectory(state);
        string target = Path.Combine(campaign, "TArcade.SC2Map");
        if (Path.GetFullPath(source).Equals(target, StringComparison.OrdinalIgnoreCase)) throw new IOException("源地图不能是安装目标。");
        Directory.CreateDirectory(state);
        using var guard = new FileStream(Path.Combine(state, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (Directory.Exists(target)) throw new IOException("目标已是地图组件目录，请先另存现有地图；不会自动覆盖目录。");
        if (File.Exists(target) && File.GetAttributes(target).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("目标地图是链接，停止安装。");
        Receipt receipt;
        if (File.Exists(Path.Combine(state, "receipt.json")))
        {
            receipt = LoadReceipt(state, root);
            if (File.Exists(target)) VerifyCurrent(target, receipt);
            else if (receipt.HadOriginal) throw new IOException("现有地图已被移走，停止安装。请先恢复备份。");
        }
        else
        {
            bool had = File.Exists(target);
            string backup = Path.Combine(state, "Original.SC2Map");
            if (File.Exists(backup)) throw new IOException("发现没有对应记录的备份，停止安装。请保留并检查备份。");
            if (had) AtomicCopy(target, backup);
            receipt = new(1, Path.GetFullPath(root), had ? Hash(target) : "", null, had, had ? Hash(backup) : null);
        }
        // Write a pending journal first so interrupted installations remain recoverable.
        string newHash = Hash(source);
        receipt = receipt with { PendingHash = newHash };
        AtomicText(Path.Combine(state, "receipt.json"), JsonSerializer.Serialize(receipt, Json));
        AtomicCopy(source, target);
        if (Hash(target) != newHash) throw new IOException("安装回读校验失败。");
        receipt = receipt with { InstalledHash = newHash, PendingHash = null };
        AtomicText(Path.Combine(state, "receipt.json"), JsonSerializer.Serialize(receipt, Json));
        return target;
    }
    public static string Restore(string root)
    {
        EnsureStopped();
        return RestoreFiles(root);
    }
    internal static string RestoreFiles(string root)
    {
        string campaign = Campaign(root), state = Path.Combine(campaign, ".LostVikingAssist");
        SafeDirectory(state);
        string target = Path.Combine(campaign, "TArcade.SC2Map");
        if (!File.Exists(Path.Combine(state, "receipt.json"))) throw new IOException("此目录没有本工具的安装记录，无需恢复。");
        using var guard = new FileStream(Path.Combine(state, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var receipt = LoadReceipt(state, root);
        string backup = Path.Combine(state, "Original.SC2Map");
        if (receipt.HadOriginal && (!File.Exists(backup) || Hash(backup) != receipt.OriginalHash))
            throw new IOException("原文件备份不存在或校验失败，停止恢复。");
        if (File.Exists(target)) VerifyCurrent(target, receipt);
        else if (receipt.HadOriginal == false && receipt.PendingHash is null)
        { /* already manually removed, finish retiring the journal */ }
        var archive = Path.Combine(state, "History", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(archive);
        // Preserve the current map in History, even on machines where no original override existed.
        if (File.Exists(target)) AtomicCopy(target, Path.Combine(archive, "Removed_Assist.SC2Map"));
        if (receipt.HadOriginal) AtomicCopy(backup, target);
        else if (File.Exists(target)) File.Delete(target);
        File.Move(Path.Combine(state, "receipt.json"), Path.Combine(archive, "receipt.json"));
        if (receipt.HadOriginal) File.Move(backup, Path.Combine(archive, "Original.SC2Map"));
        return receipt.HadOriginal ? "已恢复安装前的同名地图。" : "已移除本次覆盖地图，恢复客户端原版加载。";
    }
}
