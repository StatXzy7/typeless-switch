# Typeless Switch 技术参考

本文说明 Windows GUI 的数据边界、会话格式、词典接口、并发策略和回滚流程。所有仓库文件都使用相对路径；Windows 用户目录使用环境变量表达。

## 组件

```text
src\TypelessSwitch.App\
  MainWindow.xaml(.cs)       主界面、进度与取消
  LoginWindow.xaml(.cs)      WebView2 邮箱登录

src\TypelessSwitch.Core\
  TypelessPaths.cs           用户路径和程序发现
  SessionStoreService.cs     Typeless 会话加解密
  LocalStateService.cs       停止、备份、清理、恢复和重启
  DictionaryService.cs       列表、导出和并发导入
  AccountRegistryService.cs  非敏感账号摘要
```

GUI 只有一个主进程。没有后台服务、托盘进程、Node.js 或 Puppeteer 运行时。WebView2 只在账号切换期间初始化。

## 路径模型

| 用途 | Windows 通用路径 |
|---|---|
| Typeless 用户目录 | `%APPDATA%\Typeless.exe` |
| 加密会话 | `%APPDATA%\Typeless.exe\user-data.json` |
| 应用状态 | `%APPDATA%\Typeless.exe\app-storage.json` |
| 设备缓存 | `%APPDATA%\Typeless\Cache\device.cache` |
| Typeless Switch 数据 | `%LOCALAPPDATA%\TypelessSwitch` |
| 默认词典导出 | Windows“文档”目录下的 `Typeless Switch\Exports` |
| 切换备份 | `%TEMP%\typeless-switch-backup-*` |

Typeless 程序依次从 `TYPELESS_APP_PATH`、`%LOCALAPPDATA%\Programs\Typeless` 和 `%ProgramFiles%\Typeless` 查找。

## 会话兼容

`SessionStoreService` 兼容 Typeless 当前使用的 `conf`/`electron-store` 文件布局：

1. 对 `win32-x64` 计算 SHA-256 十六进制种子；Windows 当前版本使用应用名 `Typeless.exe`，旧版本候选包括 `Typeless` 和 `typeless`。
2. 使用 `typeless-user-service` 作为盐，以 PBKDF2-SHA256 迭代 10,000 次得到 32 字节应用密钥。
3. 文件布局为 16 字节 IV、一个冒号字节、AES-256-CBC 密文。
4. AES 密钥使用应用密钥和 Node `iv.toString()` 兼容盐，以 PBKDF2-SHA512 迭代 10,000 次得到。
5. 外层 JSON 的 `userData` 是包含邮箱、token、登录时间和用户 ID 的 JSON 字符串。

写入采用同目录临时文件加原子替换，避免中途退出留下半个会话文件。测试中的固定密文由 Node.js 参考实现生成，并包含非法 UTF-8 IV 字节，用于防止不同 UTF-8 替换策略造成不兼容。

## 账号切换事务

```text
停止 Typeless
  → 备份本地状态
  → 清理旧会话、浏览器缓存和设备缓存
  → WebView2 邮箱验证码登录
  → 读取登录页 localStorage token
  → 加密写入新会话
  → 启动 Typeless
```

只要登录被取消或写入失败，就从备份恢复原目录和设备缓存，然后重新启动 Typeless。备份放在操作系统临时目录，不写入仓库。

WebView2 读取的 localStorage 键为：

```text
MAXAI_CLIENT__FEATURES__AUTH__TOKEN_INFO
```

程序不会把 token 写入日志或 `accounts.json`。

## 词典导出

列表请求：

```text
GET https://api.typeless.com/user/dictionary/list?size=10000
```

一次响应同时生成三个文件：

- JSON：完整结构化备份，也是标准导入源。
- TXT：每行一个词条。
- CSV：词条、语言、分类、自动和替换标志。

GUI 提供“一键导出到默认位置”和“选择导出位置”两种入口。默认目录通过
`Environment.SpecialFolder.MyDocuments` 动态解析，不依赖用户名或固定盘符。导出成功后，
JSON 路径会直接填入导入框；应用启动时也会自动选中已经存在的默认 JSON。

## 并发导入

导入前先调用列表接口，并跳过目标账号中已有的词条。

### Bulk 模式

```text
POST https://api.typeless.com/user/dictionary/bulk-import
```

- 每批最多 200 个 term。
- 多批通过有界并行循环同时提交。
- GUI 默认最大并发为 12，服务层上限为 16。
- 只保留词条文本。

### Full 模式

```text
POST https://api.typeless.com/user/dictionary/add
```

- 每个请求包含 term、lang、category、auto、replace 和 replace_targets。
- 使用 1 到 32 的可配置并发数，默认 12。
- 按 term 和 lang 组合去重。

两个模式都支持 `CancellationToken`，并通过 `IProgress` 报告完成、成功和失败数量。

## 验证

`tests\TypelessSwitch.Tests` 当前覆盖：

- Node `conf` 固定密文兼容。
- 会话写入/读取往返。
- 450 个词条拆成 200、200、50 三批并确认请求发生并发。
- 本地状态备份、清理和失败恢复。

Release 构建入口：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

构建脚本先运行测试，再创建 `artifacts\publish\win-x64` 和 `installer\output`。这些生成目录由 `.gitignore` 排除。

真实邮箱验证码登录和远端词典写入需要账号所有者交互，不能由离线测试替代。
