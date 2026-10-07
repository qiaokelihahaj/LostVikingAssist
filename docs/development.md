# 开发

## 环境

- Windows x64。
- .NET 10 SDK。
- Visual Studio 2022 C++ Build Tools，包含 MSVC v143 与 Windows SDK。用于从源码构建 CascLib；WPF 应用本身使用 `dotnet` 构建。
- PowerShell 7，用于运行构建脚本。

首次运行：

```powershell
./tools/build.ps1
```

脚本在缺少 DLL 时恢复固定版本原生依赖，再运行测试、发布自包含程序，并生成 `dist/LostVikingAssist-win-x64.zip`。程序无需游戏安装即可构建；实际提取地图时需要完整游戏客户端。

## 日常开发

```powershell
dotnet build LostVikingAssist.slnx -c Release
dotnet run --project src/LostVikingAssist
dotnet run --project tests/LostVikingAssist.Tests -c Release
```

直接执行 `dotnet` 前，需要已运行过 `tools/fetch-vendor.ps1` 或 `tools/build.ps1`。

测试程序是零额外测试包依赖的控制台验证器，失败时退出码非零。默认 22 项测试使用自行构造的脚本和 MPQ，不需要游戏数据。可选择本地真实脚本进行额外验证：

```powershell
dotnet run --project tests/LostVikingAssist.Tests -c Release -- "C:\local\Original.MapScript.galaxy"
```

测试创建独立临时游戏目录，不安装到真实客户端。

## 目录

```text
src/LostVikingAssist/          WPF 界面、补丁和安装逻辑
tests/LostVikingAssist.Tests/  脚本、MPQ 与备份恢复验证
tools/                        依赖恢复和发布脚本
vendor/                       原生依赖说明、上游许可证
docs/                         使用和开发文档
.github/workflows/            Windows 构建与产物上传
.local/                       本地资源、实验记录、依赖缓存（忽略）
dist/                         程序与发布包（忽略）
```

GitHub Actions 使用 Windows runner 执行相同构建脚本。成功后上传 Windows ZIP 作为 workflow artifact；不会自动创建 Release 或上传游戏地图。

发布包按固定文件清单组装，只包含程序、文档、许可证与校验文件。本地输出目录里即使有地图，也不会打入包中。仓库只保存项目自有源代码、构造的测试及依赖恢复信息；游戏地图、原版 Galaxy 脚本、存档、备份和本机生成报告均保留在本地。
