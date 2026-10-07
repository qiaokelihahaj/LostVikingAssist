# 原生依赖

本目录只提交说明和上游许可证。DLL、下载归档与源码缓存不提交。

运行 `tools/fetch-vendor.ps1` 后，会生成 `StormLib.dll` 和 `CascLib.dll`。

| 依赖 | 固定版本 | 来源 | 构建方式 |
| --- | --- | --- | --- |
| StormLib | v9.40 | [官方发布](https://github.com/ladislav-zezula/StormLib/releases/tag/v9.40) | 官方 x64 Unicode DLL |
| CascLib | 3.0 | [官方源码](https://github.com/ladislav-zezula/CascLib/tree/3.0) | MSVC v143，x64，Unicode，静态 CRT |

下载归档按脚本中的 SHA-256 校验。CascLib 唯一源码调整是将资源头 `afxres.h` 替换为 `windows.h`，以避免 MFC 依赖。下载和编译缓存位于 `.local/dependency-cache/`。

两库均使用 MIT 许可证，原始文本保存在本目录。
