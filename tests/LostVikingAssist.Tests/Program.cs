using System.Text;
using System.Text.Json;
using LostVikingAssist;

string original = """
// gt_FighterBombKeyDown_Func { fake gv_bombCount -= 1; }
void gf_AddScore(int points) {
    if (points >= 125000) { Award("LostVikingBronze"); }
    if (points >= 250000) { Award("LostVikingSilver"); }
    if (points >= 500000) { Award("LostVikingGold"); }
}
bool gt_FighterBombKeyDown_Func(bool testConds, bool runActions) {
    if (testConds) { if (gv_bombCount <= 0) { return false; } }
    // decoy: gv_bombCount -= 1;
    string comment = "gv_bombCount -= 1; }";
    gv_bombCount -= 1;
    UnitIssueOrder(gv_viking, Order(AbilityCommand("SS_FighterBomb", 0)), c_orderQueueReplace);
    Wait(0.5, c_timeGame); Wait(0.5, c_timeGame);
    return true;
}
bool gt_SpawnViking_Func(bool testConds, bool runActions) {
    Wait(4.0, c_timeGame);
    UnitBehaviorRemovePlayer(gv_viking, "SS_Invulnerable", gv_p1_USER, 1);
    libNtve_gf_MakeUnitInvulnerable(gv_viking, false);
    return true;
}
void boss() { libNtve_gf_MakeUnitInvulnerable(gv_boss, false); }
""";
int passed = 0;
void Test(string name, Action run)
{
    try { run(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception error) { Console.Error.WriteLine("FAIL " + name + ": " + error); Environment.Exit(1); }
}
void Assert(bool value) { if (!value) throw new Exception("Assertion failed"); }
void Refused(Action run)
{
    bool refused = false;
    try { run(); } catch (Exception error) when (error is IOException or InvalidDataException) { refused = true; }
    Assert(refused);
}
var options = new AssistOptions();
string patched = ScriptPatch.Apply(original, options);
Test("exact two code edits; comments, score, cooldown and Boss unchanged", () => {
    string expected = original.Replace("    gv_bombCount -= 1;", "    gv_bombCount -= 0;")
        .Replace("libNtve_gf_MakeUnitInvulnerable(gv_viking, false)", "libNtve_gf_MakeUnitInvulnerable(gv_viking, true)");
    Assert(patched == expected);
});
Test("idempotent patch", () => Assert(ScriptPatch.Apply(patched, options) == patched));
Test("both toggles reverse exactly", () => Assert(ScriptPatch.Apply(patched, new(false, false)) == original));
Test("independent bomb toggle", () => Assert(ScriptPatch.Inspect(ScriptPatch.Apply(original, new(true, false))) == new ScriptStatus(true, false)));
Test("independent invulnerability toggle", () => Assert(ScriptPatch.Inspect(ScriptPatch.Apply(original, new(false, true))) == new ScriptStatus(false, true)));
Test("missing target rejected", () => Refused(() => ScriptPatch.Apply(original.Replace("gt_SpawnViking_Func", "renamed"), options)));
Test("duplicate decrement rejected", () => Refused(() => ScriptPatch.Apply(original.Replace("    gv_bombCount -= 1;", "    gv_bombCount -= 1; gv_bombCount -= 1;"), options)));
Test("unsupported consumption rejected", () => Refused(() => ScriptPatch.Apply(original.Replace("    gv_bombCount -= 1;", "    gv_bombCount -= 2;"), options)));
Test("duplicate target function rejected", () => Refused(() => ScriptPatch.Apply(original + "\nbool gt_SpawnViking_Func() {}", options)));
Test("missing original achievement rejected", () => Refused(() => ScriptPatch.Apply(original.Replace("LostVikingGold", "OtherGold"), options)));
Test("CRLF preserved", () => Assert(ScriptPatch.Apply(original.Replace("\n", "\r\n"), options) == patched.Replace("\n", "\r\n")));
Test("malformed braces rejected", () => Refused(() => ScriptPatch.Apply(original.Replace("    return true;\n}\nvoid boss", "    return true;\nvoid boss"), options)));

string sandbox = Path.Combine(Path.GetTempPath(), "LostVikingAssist-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(sandbox);
string NewGame(string name)
{
    string root = Path.Combine(sandbox, name); Directory.CreateDirectory(root);
    File.WriteAllBytes(Path.Combine(root, "StarCraft II.exe"), []);
    File.WriteAllText(Path.Combine(root, ".build.info"), "Active!DEC:1|Version!STRING:0\n1|5.0.test\n");
    return root;
}
string Map(string script, string filename)
{
    string path = Path.Combine(sandbox, filename);
    using var map = new MpqArchive(path, create: true);
    map.Write("MapScript.galaxy", Encoding.UTF8.GetBytes(script));
    foreach (var name in new[] { "DocumentInfo", "DocumentHeader", "MapInfo", "ComponentList.SC2Components", "t3Terrain.xml" })
        map.Write(name, [1, 2, 3]);
    map.Write("UnknownExtra\\KeepMe.dat", [8, 6, 4]);
    return path;
}
string assist = Map(patched, "辅助.SC2Map"), vanilla = Map(original, "原版.SC2Map");
Test("Unicode MPQ paths and script roundtrip", () => Assert(AssistService.InspectMap(assist) == new ScriptStatus(true, true)));
Test("import preserves unlisted extra archive content", () => {
    var result = AssistService.BuildFromMap(vanilla, sandbox, options);
    using var map = new MpqArchive(result.Map);
    Assert(map.Read("UnknownExtra\\KeepMe.dat").SequenceEqual(new byte[] {8, 6, 4}));
    Assert(AssistService.InspectMap(result.Map) == new ScriptStatus(true, true));
});
Test("new installation then restore removes only the override", () => {
    string root = NewGame("clean"), target = AssistService.InstallFiles(root, assist);
    string other = Path.Combine(Path.GetDirectoryName(target)!, "OtherMap.SC2Map"); File.WriteAllText(other, "keep");
    Assert(AssistService.Hash(target) == AssistService.Hash(assist));
    AssistService.RestoreFiles(root);
    Assert(!File.Exists(target) && File.ReadAllText(other) == "keep");
    Assert(Directory.GetFiles(Path.Combine(Path.GetDirectoryName(target)!, ".LostVikingAssist", "History"), "Removed_Assist.SC2Map", SearchOption.AllDirectories).Length == 1);
});
Test("preexisting map backup survives repeated install and exact restore", () => {
    string root = NewGame("existing"), campaign = Path.Combine(root, "Maps", "Campaign"); Directory.CreateDirectory(campaign);
    string target = Path.Combine(campaign, "TArcade.SC2Map"); File.WriteAllBytes(target, [9, 8, 7, 6]);
    AssistService.InstallFiles(root, assist); AssistService.InstallFiles(root, assist); AssistService.RestoreFiles(root);
    Assert(File.ReadAllBytes(target).SequenceEqual(new byte[] {9, 8, 7, 6}));
});
Test("external map modifications block install and restore", () => {
    string root = NewGame("modified"), target = AssistService.InstallFiles(root, assist); File.AppendAllText(target, "changed");
    Refused(() => AssistService.InstallFiles(root, assist)); Refused(() => AssistService.RestoreFiles(root));
    Assert(File.ReadAllBytes(target).Length > new FileInfo(assist).Length);
});
Test("corrupt original backup blocks restore", () => {
    string root = NewGame("corrupt"), campaign = Path.Combine(root, "Maps", "Campaign"); Directory.CreateDirectory(campaign);
    File.WriteAllText(Path.Combine(campaign, "TArcade.SC2Map"), "original"); string target = AssistService.InstallFiles(root, assist);
    File.WriteAllText(Path.Combine(campaign, ".LostVikingAssist", "Original.SC2Map"), "corrupted");
    Refused(() => AssistService.RestoreFiles(root)); Assert(File.Exists(target));
});
Test("interrupted first install journal can restore original", () => {
    string root = NewGame("interrupted"), campaign = Path.Combine(root, "Maps", "Campaign"), state = Path.Combine(campaign, ".LostVikingAssist");
    Directory.CreateDirectory(state); string target = Path.Combine(campaign, "TArcade.SC2Map"), backup = Path.Combine(state, "Original.SC2Map");
    File.WriteAllText(target, "original"); File.Copy(target, backup); string hash = AssistService.Hash(target);
    File.WriteAllText(Path.Combine(state, "receipt.json"), JsonSerializer.Serialize(new Receipt(1, root, hash, AssistService.Hash(assist), true, hash)));
    AssistService.RestoreFiles(root); Assert(File.ReadAllText(target) == "original");
});
Test("component directory target never overwritten", () => {
    string root = NewGame("components"), target = Path.Combine(root, "Maps", "Campaign", "TArcade.SC2Map"); Directory.CreateDirectory(target);
    Refused(() => AssistService.InstallFiles(root, assist)); Assert(Directory.Exists(target));
});
Test("unpatched map refused for install", () => Refused(() => AssistService.InstallFiles(NewGame("noassist"), vanilla)));
Test("restore without receipt refused", () => Refused(() => AssistService.RestoreFiles(NewGame("noreceipt"))));

if (args.Length > 0)
{
    string script = File.ReadAllText(args[0]);
    Test("current client script accepts only the two intended edits", () => {
        string result = ScriptPatch.Apply(script, options);
        string expected = script.Replace("    gv_bombCount -= 1;", "    gv_bombCount -= 0;")
            .Replace("libNtve_gf_MakeUnitInvulnerable(gv_viking, false)", "libNtve_gf_MakeUnitInvulnerable(gv_viking, true)");
        Assert(result == expected); Assert(ScriptPatch.Apply(result, options) == result);
    });
}
Console.WriteLine($"{passed} tests passed. Isolated test files: {sandbox}");
