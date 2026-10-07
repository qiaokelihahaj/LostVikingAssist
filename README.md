# LostVikingAssist · 失落的维京辅助器

《星际争霸 II》自由之翼小游戏《失落的维京》的 Windows 辅助工具。读取本地客户端，生成无限炸弹、玩家永久无敌的完整地图，支持备份安装和恢复。

只修改炸弹消耗与玩家出生后取消无敌的动作，保留炸弹冷却、Boss、关卡、计分及原版成就检查。不会直接调用成就解锁。

**资源提取与补丁回读已在国服 5.0.16.97579 验证；战役入口覆盖加载及官方成就入账仍待游戏内实测。**

## 使用

解压 Windows 发布包，打开 `LostVikingAssist.exe`：

1. 确认游戏目录，选择无限炸弹和／或永久无敌。
2. 点击“生成地图”。
3. 退出游戏及编辑器，点击“安装补丁”。
4. 从未使用作弊码的海伯利昂存档进入酒吧街机，验证辅助并正常积累分数。
5. 需要卸载时，退出游戏并点击“恢复原版”。

完整步骤、分数目标、地图重新保存的限制以及命令行用法见 [使用说明](docs/usage.md)。发布程序自带运行环境。

## 构建

需要 Windows x64、.NET 10 SDK、PowerShell 7，以及 Visual Studio 2022 C++ Build Tools（MSVC v143 与 Windows SDK）。

```powershell
./tools/build.ps1
```

首次构建会下载并校验固定版本依赖。脚本运行测试并生成：

- `dist/app/LostVikingAssist.exe`：自包含程序。
- `dist/LostVikingAssist-win-x64.zip`：可分发发布包。

目录结构、测试和日常开发命令见 [开发说明](docs/development.md)。GitHub Actions 执行相同构建流程。

## 许可证与依赖

项目采用 [MIT 许可证](LICENSE)。原生依赖为 [StormLib v9.40](https://github.com/ladislav-zezula/StormLib/releases/tag/v9.40) 和 [CascLib 3.0](https://github.com/ladislav-zezula/CascLib/tree/3.0)，版本、恢复方式与许可证见 [vendor](vendor/README.md)。

仓库和发布包不包含游戏地图或游戏数据；使用时从本机客户端提取。
