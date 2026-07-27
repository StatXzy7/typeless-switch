# Typeless Switch

Typeless Switch 是一个面向 Windows 10/11 x64 的轻量桌面工具，用于快速切换 Typeless 账号并迁移自定义词典。

当前桌面版以极简操作为目标：不常驻托盘、不安装后台服务、不要求用户配置 Node.js。只有执行账号切换时才会临时打开 WebView2 登录窗口。

> 本项目不是 Typeless 官方产品。请只操作你本人拥有或获准使用的账号，并遵守 Typeless 的服务条款。本工具不会绕过服务端的订阅、额度、设备或账号限制。

## 功能

- 在 GUI 中读取当前 Typeless 账号。
- 使用邮箱验证码切换账号。
- 切换前自动备份本地状态；取消或失败时自动恢复。
- 一次导出完整词典为 JSON、TXT 和 CSV。
- 批量导入时按每批最多 200 个词条拆分，并行提交多个批次。
- 完整导入模式使用可控并发，保留语言、分类、自动替换和替换目标等元数据。
- 自动跳过目标账号中已经存在的词条。
- 显示进度、成功/失败数量，并支持取消长时间操作。

## 安装

从仓库的 [Releases](../../releases) 页面下载名称类似下面的安装程序：

```text
TypelessSwitch-0.1.0-win-x64-setup.exe
```

运行安装程序后，从开始菜单打开 Typeless Switch。安装包自带 .NET 8 运行时；Windows 10 若没有 WebView2 Runtime，需要先通过 Microsoft Edge 更新安装它。Windows 11 通常已经包含 WebView2。

安装默认写入当前用户目录，不需要管理员权限，也不会配置开机启动。

当前 `v0.1.0` 安装包尚未使用商业代码签名证书，Windows SmartScreen 可能显示“未知发布者”。请只从本仓库 Releases 下载，并核对 Release 页面公布的 SHA-256；如果不信任来源，请不要继续运行。

## 使用

### 切换账号

1. 打开 Typeless Switch。
2. 在“切换账号”中输入目标邮箱。
3. 点击“切换账号”。程序会先关闭 Typeless 并备份当前本地状态。
4. 在登录窗口中继续邮箱登录并填写六位验证码。
5. 登录成功后，程序写入新会话并重新打开 Typeless。

关闭登录窗口或发生错误时，程序会恢复切换前的本地状态。备份保存在系统临时目录中，名称格式为 `%TEMP%\typeless-switch-backup-*`。

### 导出词典

1. 确认窗口顶部显示的是需要导出的账号。
2. 点击“导出当前词典”。
3. 选择一个文件夹。

程序会同时创建：

```text
typeless-dictionary-export.json
typeless-dictionary-export.txt
typeless-dictionary-export.csv
```

JSON 是重新导入时使用的标准文件。导出内容可能包含个人用词，请按私密数据保存。

### 导入词典

1. 点击“选择文件”，选择之前导出的 JSON。
2. 选择导入模式。
3. 点击“开始导入”。

两种模式都不是逐条串行导入：

- “快速批量”每批最多 200 个词条，多批并行提交，速度最快，只迁移词条文本。
- “完整元数据”并发提交单个词条，默认并发数为 12，保留完整字段。

## 本地数据与隐私

程序根据当前 Windows 用户动态解析路径，不包含开发者电脑的盘符、用户名或绝对目录。

| 数据 | 通用位置 |
|---|---|
| Typeless 加密会话 | `%APPDATA%\Typeless.exe\user-data.json` |
| Typeless 本地状态 | `%APPDATA%\Typeless.exe\app-storage.json` |
| Typeless 设备缓存 | `%APPDATA%\Typeless\Cache\device.cache` |
| Typeless Switch 账号摘要 | `%LOCALAPPDATA%\TypelessSwitch\accounts.json` |
| 登录 WebView2 数据 | `%LOCALAPPDATA%\TypelessSwitch\WebView2` |
| 切换前备份 | `%TEMP%\typeless-switch-backup-*` |

`accounts.json` 只记录邮箱、Typeless 用户 ID 和最后使用时间，不保存 access token 或 refresh token。令牌只从本地登录页读取，并按 Typeless 使用的加密格式写入其本地会话文件。

如果 Typeless 安装在非默认目录，可以为当前用户设置 `TYPELESS_APP_PATH`，值为实际的 `Typeless.exe` 路径。不要把该值写入仓库文档或提交记录。

## 轻量化约束

- 单进程 WPF 应用，没有 Electron、常驻服务或托盘守护进程。
- 登录浏览器只在切换账号时创建。
- 主窗口关闭后进程退出。
- Windows x64 自包含发布，不要求目标电脑预装 .NET。

开发机 Release 基线测试中，未打开登录页时工作集约 115–135 MB；具体数值会随 Windows 版本、字体缓存和安全软件变化。安装程序约 46 MB，安装后自包含文件约 146 MB（安装器自身约增加 4 MB）。

## 从源码构建

要求：

- Windows 10/11 x64
- .NET 8 SDK
- Inno Setup 6（仅构建安装程序时需要）

在仓库根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

相对输出位置：

```text
artifacts\publish\win-x64\
installer\output\TypelessSwitch-0.1.0-win-x64-setup.exe
```

只构建自包含应用、不构建安装程序：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-release.ps1 -SkipInstaller
```

运行测试：

```powershell
dotnet test .\TypelessSwitch.sln --configuration Release
```

## 项目结构

```text
.
├── src\TypelessSwitch.App\       WPF GUI 与 WebView2 登录
├── src\TypelessSwitch.Core\      会话、本地状态与词典服务
├── tests\TypelessSwitch.Tests\   兼容性与并发回归测试
├── installer\                    Inno Setup 配置
├── scripts\build-release.ps1     Release 构建入口
├── scripts\*.mjs                 旧命令行兼容工具
└── references\extract-dictionary.md
```

旧 Node.js/PowerShell/Bash 脚本仍保留，便于排障和兼容原工作流；Windows 普通用户应优先使用 GUI。

## 已知限制

- 当前自动切换只支持邮箱验证码登录。Google 或 Apple 登录的账号可以先在 Typeless 桌面端登录，再使用导出功能。
- Typeless 登录页面或 API 若发生变化，页面自动填写或词典操作可能需要随之更新。
- 未持有有效 Typeless 账号时，可以验证启动、安装、加密兼容和本地回滚，但无法完成真实服务端登录的端到端测试。

技术细节见 [references/extract-dictionary.md](./references/extract-dictionary.md)。

## 许可证

本项目使用 [Apache License 2.0](./LICENSE)。
