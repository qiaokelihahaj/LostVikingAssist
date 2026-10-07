# 失落的维京 · 成就辅助器

Windows 64 位桌面工具，为《星际争霸 II》自由之翼的《失落的维京》生成“无限炸弹＋飞船永久无敌”完整地图，再通过原版玩法积累分数。

**资源读取、补丁生成与地图回读已在国服 5.0.16.97579 验证。尚未验证正常战役入口采用覆盖地图，也未验证官方成就入账。**

## 使用

1. 解压发布包并打开 `LostVikingAssist.exe`。发布版自带运行环境，无需另装 .NET。
2. 确认游戏目录，选择“无限炸弹”和／或“永久无敌”。默认直接从当前客户端只读提取地图；展开“更多选项与日志”可以导入编辑器另存的完整 `TArcade.SC2Map`、检测目录、检查补丁或打开输出目录。
3. 点击“生成地图”。工具输出完整 `.SC2Map`、修改前后脚本和带 SHA-256 的生成报告，并回读校验地图文件。输出在 `%LOCALAPPDATA%\LostVikingAssist\Builds`。
4. 完全退出游戏与编辑器后，点击“安装补丁”。如果本次启动尚未生成地图，会提示选择已生成的文件。写入 Program Files 可能弹出 Windows 管理员权限提示。
5. 从战网正常启动、登录目标账号，读取**尚未使用作弊码、位于海伯利昂**的战役存档，从酒吧街机进入小游戏。
6. 先验证辅助：超过 10 秒后碰普通敌弹，飞船应不死；间隔 1 秒以上连续释放 5 次炸弹，库存应不减少。继续测试跨关、Boss 攻击、拾取与计分。
7. 自己打到最低尚未获得的成就分数：铜牌 125,000，银牌 250,000，金牌 500,000。返回账号成就页面，重新登录确认记录持久保存，才能判定官方解锁成功。
8. 恢复时退出游戏与编辑器，点击“恢复原版”。恢复只处理本工具安装的 `TArcade.SC2Map`；安装前已有覆盖地图的，会恢复那份地图。

## 实际修改

仅在目标函数内部定位唯一语句，跳过注释和字符串；目标缺失、重复或结构不支持时拒绝生成。

```diff
// gt_FighterBombKeyDown_Func
-gv_bombCount -= 1;
+gv_bombCount -= 0;

// gt_SpawnViking_Func
-libNtve_gf_MakeUnitInvulnerable(gv_viking, false);
+libNtve_gf_MakeUnitInvulnerable(gv_viking, true);
```

扣零等价于不消耗库存，保留初始炸弹、有炸弹条件、技能、UI 刷新及原版冷却。无敌修改只作用于出生触发器里的玩家飞船，不修改 Boss。原来的 `gf_AddScore` 和三个成就检查逐字保持一致。关闭某个选项会将该动作恢复原值，便于分别验证。

工具修改完整地图内的**运行脚本**，不修改编辑器的 `Triggers` 源文件。不要在编辑器重新保存生成的地图：编辑器会重新生成脚本，覆盖辅助补丁。如需编辑其他内容，先在编辑器保存，然后重新导入本工具生成。编辑器测试地图本身不代表官方成就有效。

## 安装与恢复

目标路径为：

```text
<游戏安装目录>\Maps\Campaign\TArcade.SC2Map
```

这是本地覆盖加载实验，客户端实际加载仍需游戏内检查。

同目录 `.LostVikingAssist` 保存安装记录和原有同名文件备份。重复安装不会把第一次备份换成辅助版。操作先写入恢复日志，再原子替换文件；复制后校验哈希。外部程序改变地图或备份损坏时拒绝覆盖。恢复时将辅助地图和安装记录留在 `History`，有原文件则恢复原文件，没有则移除本次覆盖。不会清空其他战役地图、修改客户端 CASC 数据包、修改存档或直接调用成就解锁。

请保留 `.LostVikingAssist`，它是恢复所需的备份记录。关闭工具不会自动卸载辅助地图。

## 命令行

命令行（GUI 程序通过 `--report` 写入操作结果）：

```powershell
LostVikingAssist.exe --build --game "C:\Program Files (x86)\StarCraft II" --output "D:\Viking" --report "D:\Viking-result.json"
LostVikingAssist.exe --install --game "C:\Program Files (x86)\StarCraft II" --map "D:\Viking\LostViking_Assist.SC2Map" --report "D:\install-result.json"
LostVikingAssist.exe --restore --game "C:\Program Files (x86)\StarCraft II" --report "D:\restore-result.json"
```

`--build --map <完整地图>` 使用导入来源；`--no-bombs` / `--no-invulnerability` 分别关闭选项。`--inspect --map <地图> --report <结果>` 检查补丁。CLI 的报告路径必须存在可写的父目录。

## 依据与第三方库

- [SC2Mapster 原版资源仓库](https://github.com/SC2Mapster/SC2GameData)：历史脚本交叉核对；实际输出使用本机客户端。
- [StormLib v9.40](https://github.com/ladislav-zezula/StormLib/releases/tag/v9.40)：读取／写入 MPQ，使用官方 x64 Unicode DLL。
- [CascLib 3.0](https://github.com/ladislav-zezula/CascLib/tree/3.0)：只读客户端 CASC 数据，使用官方源码本地编译的 x64 Unicode DLL；资源头 `afxres.h` 改为 `windows.h` 以避免 MFC 依赖。

两库的 MIT 许可保留在 `vendor` 和发布包中。程序运行时不下载旧地图、不联网查询账号成就。
