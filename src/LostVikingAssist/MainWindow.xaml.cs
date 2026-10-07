using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;

namespace LostVikingAssist;

public partial class MainWindow : Window
{
    string? importedMap, builtMap;
    bool busy;
    public MainWindow()
    {
        InitializeComponent();
        var found = AssistService.DetectGame();
        GamePath.Text = found ?? "";
        RefreshVersion();
        Log("只修改炸弹消耗与玩家无敌，计分、Boss、关卡和成就检查保留。");
        Log("安装会备份已有同名地图，恢复时会保留移出的辅助地图。");
        Closing += (_, e) => { if (busy) { e.Cancel = true; Log("正在处理文件，请等待完成后再关闭。"); } };
    }
    void Log(string text)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => Log(text)); return; }
        LogText.AppendText(text + Environment.NewLine);
        LogText.ScrollToEnd();
    }
    void RefreshVersion()
    {
        try { ClientInfo.Text = AssistService.IsGame(GamePath.Text) ? "客户端 " + AssistService.Version(GamePath.Text) : "请选择游戏安装目录"; }
        catch { ClientInfo.Text = "无法读取客户端版本"; }
    }
    void BrowseGame(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择星际争霸 II 安装目录" };
        if (dialog.ShowDialog(this) == true) { GamePath.Text = dialog.FolderName; RefreshVersion(); }
    }
    void DetectGame(object sender, RoutedEventArgs e)
    {
        string? found = AssistService.DetectGame();
        if (found is null) Log("未在常用目录或注册表找到客户端，请用“浏览目录”选择。");
        else GamePath.Text = found;
        RefreshVersion();
    }
    void ImportMap(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "选择编辑器另存的完整 TArcade 地图", Filter = "SC2 地图 (*.SC2Map)|*.SC2Map" };
        if (dialog.ShowDialog(this) != true) return;
        importedMap = dialog.FileName;
        SourceInfo.Text = "来源：" + importedMap;
    }
    void UseClient(object sender, RoutedEventArgs e) { importedMap = null; SourceInfo.Text = "来源：当前客户端"; }
    async Task Run(string name, Func<Task> operation)
    {
        if (busy) return;
        busy = true; Actions.IsEnabled = false; Settings.IsEnabled = false; ExtraActions.IsEnabled = false;
        OperationStatus.Text = name + "…";
        try { await operation(); OperationStatus.Text = name + "完成"; }
        catch (Exception error)
        {
            string message = error is UnauthorizedAccessException ? "没有写入权限。请以管理员身份运行本工具后重试安装或恢复。" : error.Message;
            Log("失败：" + message); OperationStatus.Text = "操作未完成";
            MessageBox.Show(this, message, "操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { busy = false; Actions.IsEnabled = true; Settings.IsEnabled = true; ExtraActions.IsEnabled = true; }
    }
    async void BuildMap(object sender, RoutedEventArgs e)
    {
        string root = GamePath.Text.Trim(), source = importedMap ?? "";
        var options = new AssistOptions(Bombs.IsChecked == true, Invulnerability.IsChecked == true);
        if (!options.InfiniteBombs && !options.Invulnerable) { Log("请至少选择一个辅助选项。"); return; }
        await Run("生成", async () =>
        {
            var result = await Task.Run(() => source.Length == 0
                ? AssistService.BuildFromClient(root, AssistService.DefaultOutput, options, Log)
                : AssistService.BuildFromMap(source, AssistService.DefaultOutput, options, Log));
            builtMap = result.Map;
            ResultInfo.Text = "地图已生成并校验，可以安装。";
            ResultInfo.ToolTip = builtMap;
            if (result.SkippedLocales > 0) Log($"已跳过 {result.SkippedLocales} 个未安装语言文件，其余地图文件已保留并校验。");
            Log("可点击“安装补丁”。游戏中仍需验证正常入口加载和官方成就记录。");
        });
    }
    string? ChooseGeneratedMap()
    {
        if (builtMap is not null && File.Exists(builtMap)) return builtMap;
        var dialog = new OpenFileDialog { Title = "选择本工具生成的辅助地图", Filter = "SC2 地图 (*.SC2Map)|*.SC2Map", InitialDirectory = AssistService.DefaultOutput };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }
    async void InstallMap(object sender, RoutedEventArgs e)
    {
        string? map = ChooseGeneratedMap();
        if (map is null) return;
        string root = GamePath.Text.Trim();
        await Run("安装", async () =>
        {
            string result = await Privileged("install", root, map);
            ResultInfo.Text = "补丁已安装，可进入酒吧街机验证。"; ResultInfo.ToolTip = result; Log(result);
            Log("进入海伯利昂酒吧街机：超过 10 秒碰敌弹，间隔 1 秒以上连放 5 次炸弹。");
        });
    }
    async void RestoreMap(object sender, RoutedEventArgs e)
    {
        string root = GamePath.Text.Trim();
        await Run("恢复", async () => { string result = await Privileged("restore", root, null); ResultInfo.Text = result; ResultInfo.ToolTip = null; Log(result); });
    }
    async Task<string> Privileged(string command, string root, string? map)
    {
        try { return await Task.Run(() => command == "install" ? AssistService.Install(root, map!) : AssistService.Restore(root)); }
        catch (UnauthorizedAccessException)
        {
            Log("战役目录受 Windows 保护，正在请求管理员权限，仅用于此文件操作。");
            Directory.CreateDirectory(AssistService.DefaultOutput);
            string report = Path.Combine(AssistService.DefaultOutput, "operation-" + Guid.NewGuid().ToString("N") + ".json");
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            foreach (string arg in new[] { "--" + command, "--game", root, "--report", report }) start.ArgumentList.Add(arg);
            if (map is not null) { start.ArgumentList.Add("--map"); start.ArgumentList.Add(map); }
            try
            {
                using var process = Process.Start(start) ?? throw new IOException("无法启动管理员操作。");
                await process.WaitForExitAsync();
                if (!File.Exists(report)) throw new IOException("管理员操作未返回结果。");
                var result = JsonSerializer.Deserialize<OperationResult>(File.ReadAllText(report))!;
                if (!result.Success) throw new IOException(result.Message);
                return result.Message;
            }
            catch (Win32Exception error) when (error.NativeErrorCode == 1223) { throw new IOException("管理员权限请求已取消，文件操作未完成。"); }
        }
    }
    async void CheckInstalled(object sender, RoutedEventArgs e)
    {
        string root = GamePath.Text.Trim();
        await Run("检查", async () =>
        {
            string target = Path.Combine(root, "Maps", "Campaign", "TArcade.SC2Map");
            if (!File.Exists(target)) { Log("战役目录没有覆盖地图，当前通过客户端原版资源加载。"); return; }
            var status = await Task.Run(() => AssistService.InspectMap(target));
            Log($"已安装文件：无限炸弹 {(status.InfiniteBombs ? "开启" : "关闭")}；飞船无敌 {(status.Invulnerable ? "开启" : "关闭")}。");
            Log("文件检查通过。实际战役入口是否采用覆盖地图，需要进入游戏验证。");
        });
    }
    void OpenOutput(object sender, RoutedEventArgs e)
    {
        string folder = builtMap is null ? AssistService.DefaultOutput : Path.GetDirectoryName(builtMap)!;
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder }, UseShellExecute = true });
    }
}
