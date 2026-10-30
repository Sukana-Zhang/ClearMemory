# ClearMemory

[简体中文](README.md) | [English](README.en.md)

**轻量、独立、可分享的 Windows 内存清理工具。**

[![Windows build](https://github.com/Sukana-Zhang/ClearMemory/actions/workflows/build.yml/badge.svg)](https://github.com/Sukana-Zhang/ClearMemory/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

[下载最新版](https://github.com/Sukana-Zhang/ClearMemory/releases/latest) · [报告问题](https://github.com/Sukana-Zhang/ClearMemory/issues) · [更新记录](CHANGELOG.md)

![ClearMemory 中文界面](docs/screenshot.png)

截图为运行示例，内存数据和阈值会随电脑变化。

独立的 Windows 内存清理工具。可直接将发行 ZIP 发给其他人；解压到固定位置，运行 `ClearMemory.exe`，允许 Windows 管理员权限提示即可。适用 Windows 10/11 x64，使用系统自带的 .NET Framework 4.x。

程序没有网络请求，不上传数据；发行包不含作者的配置、账户或日志。

## 系统要求与下载

支持目标为 **Windows 10 / Windows 11 x64**，使用系统自带的 .NET Framework 4.x。32 位 Windows 不支持；ARM64 未验证，不作为本版支持平台。

1. 从 [Releases](https://github.com/Sukana-Zhang/ClearMemory/releases/latest) 下载 `ClearMemory-版本-win-x64.zip`，不要下载 GitHub 自动生成的 Source code ZIP 来当成程序运行。
2. 解压全部文件到固定位置，运行 `ClearMemory.exe`，允许管理员权限提示。
3. 按自己的需要调整定时和阈值，也可以关闭自动触发，仅手动清理。

发行 ZIP 严格只包含 EXE、README、MIT 许可证及 EXE 的 SHA256 校验文件。压缩包的 SHA256 另附于 Release。

## 使用

- 点击“立即清理”执行一次系统级清理，暂停自动清理时仍可使用。
- 默认每 30 分钟清理一次，支持 1–1440 分钟，可以取消勾选“按时间自动清理”。
- 已用物理内存持续 10 秒达到阈值时也会触发。首次启动阈值按本机总内存的 87.5% 计算，可自行调整；已有设置不会被自动覆盖。
- 定时与阈值分别开关。手动或阈值清理不会推迟定时任务；同时满足条件时只执行一轮。持续超阈值时，每轮完成后重新观察 10 秒，无额外冷却。
- “暂停自动清理”会暂停两类触发，暂停状态和设置在下次启动时保留。保存设置或恢复监测后重新计时。
- 设置文件损坏或无法读取时，程序使用默认值并暂停自动清理；确认设置后可手动恢复。首次启动没有设置文件时使用正常默认配置。
- 关闭窗口后继续在托盘监测；单击托盘图标立即打开窗口，右键菜单可完全退出。清理期间等待当前操作完成后退出。
- “登录 Windows 后启动”默认关闭，按需勾选。启用后使用当前用户的计划任务，以管理员权限在托盘启动。程序移动位置后手动运行一次可更新自启动路径。
- 同一用户会话内重复打开只唤回已有窗口，不创建第二个实例。

## 清理方式与限制

调用 Windows 系统接口执行内存页合并、系统文件缓存回收、系统工作集清理、低优先级备用页回收四个步骤，不注入或终止其他应用。系统不支持某一步时显示具体状态，其他步骤继续尝试；所需进程权限在操作后恢复。

这是全系统清理，没有游戏或浏览器白名单。程序占用可能明显下降，但清理不是修复内存泄漏，应用再次使用数据时可能重新装入内存，造成切换网页、软件或游戏时短暂卡顿。希望减少频繁打扰时，可关闭定时清理，仅保留阈值或手动清理。内存变化包含其他软件同时活动，不代表清理器独占释放的容量；不承诺提高游戏帧率。

请避免同时启用多个自动内存清理工具，以免重复清理。

## 设置、日志与移除

个人设置和轮转日志保存在 `%LOCALAPPDATA%\ClearMemory`。自启动任务为 `ClearMemory-<当前用户 SID>`。

点击窗口中的“导出日志”，或托盘菜单中的“导出诊断日志”，可以保存一个 TXT 文件发送给维护者。文件包含程序与系统版本、管理员状态、近期清理记录、自启动操作步骤、异常类型和 HRESULT；默认替换当前用户目录与用户 SID，不包含设置文件内容，也不会自动上传。

也可从托盘“打开日志目录”，取得 `activity.log` 和存在的话 `activity.previous.log`。自启动报错时，建议使用新版导出日志，并说明错误发生在首次勾选、移动程序之后还是登录时。

移除时先取消“登录 Windows 后启动”，从托盘退出，再删除解压目录即可。若不再需要历史设置，可自行删除上述个人设置目录。发行程序未作数字签名，管理员提示中的发布者会显示为未知；请只使用可信来源的包，并核对随附 SHA256。

## 源码与验证

克隆或下载本仓库后，在 Windows PowerShell 中执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\package.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\verify-package.ps1
```

构建离线完成，使用系统的 C# 编译器；输出文件均位于仓库自己的 `build` 和 `dist`，不依赖作者电脑的盘符或其他项目。`package.ps1 -OutputDirectory <目录>` 可指定输出位置。

常规测试覆盖调度、配置、界面、原生请求 ABI、失败处理、不同内存容量、区域格式、模拟缩放、并发、自启动错误分类与日志导出；具体检查数量见 CI 日志。另外验证发行包白名单、SHA256 及中文空格路径。常规测试不实际清理系统内存，也不注册自启动。

可选的 `test-startup.ps1` 需在管理员 PowerShell 中运行：它验证没有任务时的读取、首次启用、状态确认、路径更新和取消删除的完整流程。测试只使用唯一命名的临时任务；验证启用时指向空操作测试程序，不启动清理程序，完成后删除任务。GitHub CI 同时执行这项验证。

GitHub Actions 在 Windows Server 2022 和 2025 环境构建和测试，版本标签通过后自动发布。CI 验证构建和基本运行兼容性，不代表已在所有 Windows 10/11 版本及硬件上实测；尚未验证真实游戏帧时间。

仓库不包含运行日志、个人配置、账号资料或 API Key。软件本身不提供任何 API Key 配置项。

## 项目结构与维护

| 目录 | 内容 |
| --- | --- |
| `src` | 中文界面、调度、配置、自启动及 Windows 系统接口 |
| `tests` | 调度、原生请求及发行包测试源码 |
| `tools` / `assets` | 图标生成源码及程序图标 |
| `.github` | Windows 自动构建、测试、版本发布和问题模板 |

使用 MIT 许可证。参与开发、问题反馈及版本发布方式见 [CONTRIBUTING.md](CONTRIBUTING.md)。自动检查用于发现回归；后续修复通过版本提交维护。

参考：Windows 系统组件由微软提供；本发行包不附带微软运行库或第三方软件。
