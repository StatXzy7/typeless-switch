# Typeless Switch

Typeless Switch 是一个面向 Windows 10/11 x64 的轻量桌面工具，用于快速切换 Typeless 账号并迁移自定义词典。

当前桌面版以极简操作为目标：不常驻托盘、不安装后台服务、不要求用户配置 Node.js。只有执行账号切换时才会临时打开 WebView2 登录窗口。

> 本项目不是 Typeless 官方产品。请只操作你本人拥有或获准使用的账号，并遵守 Typeless 的服务条款。本工具不会绕过服务端的订阅、额度、设备或账号限制。

## 功能

- 在 GUI 中读取当前 Typeless 账号。
- 显示本机保存的账号，并支持一键切换和移除本地记录。
- 使用 Windows DPAPI 为当前 Windows 用户加密保存每个账号的登录状态。
- 使用邮箱验证码切换账号。
- 切换后重新读取邮箱和用户 ID，连续确认身份一致后才提交；失败时自动恢复原账号。
- 在账号列表显示健康状态和最近验证时间。
- 一键检查 Typeless、WebView2、当前会话、DPAPI 账号库、本地存储和临时备份，并可复制脱敏诊断报告。
- 一次导出完整词典为 JSON、TXT 和 CSV。
- 每次导入或导出前重新读取最新会话，并使用 Typeless 桌面端相同的长期凭据；短期 access token 过期不会再误报退出登录。
- 词典接口返回 401/403 时自动重新同步会话并安全重试一次。
- 可一键导出到当前用户的默认“文档”目录，也可自行选择文件夹。
- 导出完成后自动选中生成的 JSON，下次启动也会自动发现默认文件。
- 启动时自动检查 GitHub 最新版本，也可手动点击“检查更新”；下载前会先征得确认并校验 SHA-256。
- 批量导入时按每批最多 200 个词条拆分，并行提交多个批次。
- 完整导入模式使用可控并发，保留语言、分类、自动替换和替换目标等元数据。
- 自动跳过目标账号中已经存在的词条。
- 显示进度、成功/失败数量，并支持取消长时间操作。

## 安装

从仓库的 [Releases](../../releases) 页面下载名称类似下面的安装程序：

```text
TypelessSwitch-0.3.1-win-x64-setup.exe
```

运行安装程序后，从开始菜单打开 Typeless Switch。安装包自带 .NET 8 运行时；Windows 10 若没有 WebView2 Runtime，需要先通过 Microsoft Edge 更新安装它。Windows 11 通常已经包含 WebView2。

安装默认写入当前用户目录，不需要管理员权限，也不会配置开机启动。

当前安装包尚未使用商业代码签名证书，Windows SmartScreen 可能显示“未知发布者”。请只从本仓库 Releases 下载，并核对 Release 页面公布的 SHA-256；如果不信任来源，请不要继续运行。

程序启动后会在后台读取本仓库的 GitHub Releases 信息。发现新版本时会显示版本号，只有确认后才会下载；下载完成后会校验 GitHub 提供的 SHA-256，再询问是否打开安装程序。GitHub API 暂时限流时会自动回退到公开 Release 页面和 `.sha256` 校验文件。网络不可用时不会影响账号切换和词典功能。

## 使用

### 切换账号

1. 打开 Typeless Switch。
2. 在“切换账号”中输入目标邮箱。
3. 点击“切换账号”。程序会先关闭 Typeless 并备份当前本地状态。
4. 在登录窗口中继续邮箱登录并填写六位验证码。
5. 登录成功后，程序写入新会话并重新打开 Typeless。
6. 程序会在 Typeless 启动后连续读取当前账号，只有邮箱和用户 ID 都与目标账号一致才算成功。

关闭登录窗口、Typeless 启动失败或身份验证不一致时，程序会先停止新进程，再恢复切换前的本地状态。操作期间的备份位于 `%TEMP%\typeless-switch-backup-*`，切换成功或回滚完成后会自动删除；仅当恢复本身失败时才会保留，以便排查和手动恢复。

### 本地账号管理

程序会将当前正在使用的 Typeless 会话保存到 Windows 当前用户专属的加密会话库。之后每次通过邮箱验证码登录的新账号也会自动加入列表。

1. 在“本地账号管理”中选择已保存账号。
2. 点击“切换到所选账号”。
3. 程序停止 Typeless、备份当前状态、恢复所选账号并重新启动 Typeless。

如果长期登录状态仍然有效，Typeless 会使用官方刷新流程续期 access token。如果 refresh token 已过期，程序会要求重新进行邮箱验证码登录。

词典操作不会依赖可能已经过期的短期 access token。程序会在每次导入、导出前重新读取当前会话，并使用 Typeless 桌面端实际采用的 refresh token；如果服务端仍拒绝凭据，会重新同步会话并完整重试一次。导入重试前会重新读取目标词典并跳过已存在词条，不会重复导入已经成功的批次。

列表会显示“状态可用”“需要重新登录”“会话异常”“上次验证失败”或“状态待验证”，并记录最近一次严格验证时间。旧版本迁移来的非当前账号会先显示“状态待验证”，成功切换一次后自动更新。

“移除本地记录”只删除本机的账号摘要和加密会话，不会注销或删除 Typeless 远程账号。当前正在使用的账号不能直接移除，需要先切换到其他账号。

### 环境自检

点击窗口右上角“一键自检”，程序会在本机检查 Windows 平台、Typeless 安装、WebView2 Runtime、应用数据写入、当前会话、DPAPI 加密账号库、Typeless 进程和遗留临时备份。自检不会调用 Typeless 私有接口，也不会常驻后台。

结果可以复制为脱敏报告。报告只使用 `%APPDATA%`、`%LOCALAPPDATA%` 等通用路径表达，并会再次过滤邮箱、用户 ID、令牌、Windows 用户名和绝对路径。

### 更新程序

程序每次启动会自动检查 GitHub 最新 Release。也可以点击窗口右上角的“检查更新”。

发现新版本后，确认下载即可；安装包会暂时保存到 `%LOCALAPPDATA%\TypelessSwitch\Updates`，校验通过后可直接打开安装程序。更新不会静默替换当前程序，安装前始终需要用户确认。

### 导出词典

1. 确认窗口顶部显示的是需要导出的账号。
2. 点击“导出到默认位置”，文件会保存到当前用户的“文档\Typeless Switch\Exports”；或点击“选择导出位置…”自行选择文件夹。
3. 导出成功后，生成的 JSON 会自动填入导入框，无需再次查找。

程序会同时创建：

```text
typeless-dictionary-export.json
typeless-dictionary-export.txt
typeless-dictionary-export.csv
```

JSON 是重新导入时使用的标准文件。导出内容可能包含个人用词，请按私密数据保存。

### 导入词典

1. 使用导出后自动填入的 JSON；也可以点击“使用默认文件”或“选择文件”。
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
| Windows 加密账号会话 | `%LOCALAPPDATA%\TypelessSwitch\AccountVault\*.session` |
| 登录 WebView2 数据 | `%LOCALAPPDATA%\TypelessSwitch\WebView2` |
| 默认词典导出 | Windows“文档”目录下的 `Typeless Switch\Exports` |
| 更新安装包缓存 | `%LOCALAPPDATA%\TypelessSwitch\Updates` |
| 切换前临时备份 | `%TEMP%\typeless-switch-backup-*`（操作完成后自动删除） |

`accounts.json` 只记录邮箱、Typeless 用户 ID 和最后使用时间，不保存 access token 或 refresh token。账号会话使用 Windows DPAPI CurrentUser 加密，只能由保存它的 Windows 用户解密；程序不提供令牌导出功能。

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
installer\output\TypelessSwitch-0.3.1-win-x64-setup.exe
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
