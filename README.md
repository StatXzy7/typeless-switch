# Typeless Switch

Typeless Switch 是一个面向 Windows 和 macOS 的 Typeless 本地账号与词典迁移工具。它可以导出当前账号的自定义词典、通过邮箱验证码切换账号，并把词典批量导入目标账号。

> 当前版本提供命令行脚本；轻量级 Windows GUI 是后续开发方向。请只操作你拥有或获准使用的账号，并遵守 Typeless 的服务条款。

## 功能

- 导出完整词典为 JSON、TXT 和 CSV。
- 自动跳过目标账号中已经存在的词条。
- 默认使用官方 bulk-import 接口分批并行导入。
- 可选并发 full 模式，保留语言、分类和替换文本等元数据。
- 通过无头浏览器完成邮箱登录，兼容中英文登录页面。
- 切换账号前备份本地状态，并重建本地登录会话。
- 运行依赖安装在仓库内的 scripts/.vendor，不污染全局 Node.js 环境。

## 支持范围

| 环境 | 状态 | 说明 |
|---|---:|---|
| Windows 10/11 | 主要支持 | 使用 PowerShell 包装脚本 |
| macOS | 支持 | 使用 Bash 包装脚本 |
| Linux | 暂不支持 | Typeless 桌面端和本地数据布局尚未适配 |

## 环境要求

- 已安装 Typeless 桌面端。
- Node.js 18 或更高版本，并能使用 npm。
- 可以接收 Typeless 六位验证码的真实邮箱。
- Windows 使用 PowerShell；macOS 使用系统 Bash。

首次运行时，包装脚本会在 scripts/.vendor 中自动安装 electron-store 和需要的浏览器自动化依赖。

## 使用约定

以下所有命令都假设终端当前目录是仓库根目录，也就是能够看到 README.md 和 scripts 目录的位置。

不要把文档中的命令改成作者电脑上的绝对路径。仓库文件统一通过 ./scripts 和 ./references 访问；Typeless 的系统数据目录由脚本根据当前用户和操作系统自动解析。

## Windows 快速开始

完全退出 Typeless，包括系统托盘中的后台进程，然后在仓库根目录打开 PowerShell。

### 1. 导出源账号词典

~~~powershell
powershell -ExecutionPolicy Bypass -File .\scripts\export-dictionary.ps1
~~~

成功后会生成：

- references/typeless-dictionary-export.json
- references/typeless-dictionary-export.txt
- references/typeless-dictionary-export.csv

### 2. 切换到目标账号

~~~powershell
powershell -ExecutionPolicy Bypass -File .\scripts\switch-account.ps1 --email "user@example.com"
~~~

脚本发送验证码后，在终端输入收到的六位验证码。

### 3. 导入词典

~~~powershell
powershell -ExecutionPolicy Bypass -File .\scripts\import-dictionary.ps1
~~~

没有传入 --input 时，包装脚本会自动读取 references/typeless-dictionary-export.json。

### 4. 验证迁移

~~~powershell
powershell -ExecutionPolicy Bypass -File .\scripts\export-dictionary.ps1
~~~

核对输出的账号和词条总数。

## macOS 快速开始

完全退出 Typeless，然后在仓库根目录打开终端。

~~~bash
bash ./scripts/export-dictionary.sh
bash ./scripts/switch-account.sh --email "user@example.com"
bash ./scripts/import-dictionary.sh
bash ./scripts/export-dictionary.sh
~~~

四条命令依次完成导出、切换、导入和验证。默认导入文件同样是 references/typeless-dictionary-export.json。

## 导入模式

### bulk：默认模式

~~~powershell
powershell -ExecutionPolicy Bypass -File .\scripts\import-dictionary.ps1 --mode bulk
~~~

~~~bash
bash ./scripts/import-dictionary.sh --mode bulk
~~~

- 每批最多 200 个词条。
- 多个批次并行提交。
- 速度最快。
- 与 Typeless 官方 CSV 导入类似，只迁移词条文本。

### full：保留元数据

~~~powershell
powershell -ExecutionPolicy Bypass -File .\scripts\import-dictionary.ps1 --mode full --concurrency 12
~~~

~~~bash
bash ./scripts/import-dictionary.sh --mode full --concurrency 12
~~~

- 使用并发请求导入。
- 保留语言、分类和替换文本等字段。
- --concurrency 控制最大并发数，默认值为 12。

### 预览但不写入

~~~powershell
powershell -ExecutionPolicy Bypass -File .\scripts\import-dictionary.ps1 --dry-run
~~~

~~~bash
bash ./scripts/import-dictionary.sh --dry-run
~~~

## 使用自定义相对路径

如果导入文件不在 references 中，可以从仓库根目录传入相对路径：

~~~powershell
powershell -ExecutionPolicy Bypass -File .\scripts\import-dictionary.ps1 --input ".\backups\dictionary.json"
~~~

~~~bash
bash ./scripts/import-dictionary.sh --input "./backups/dictionary.json"
~~~

路径可以包含空格，但必须加引号。包装脚本会根据自身位置定位依赖，不依赖仓库被克隆到哪个磁盘、目录或用户主目录。

## 账号切换行为

切换脚本会：

1. 把当前 Typeless 本地状态备份到操作系统临时目录。
2. 清除本地登录、额度请求和 Electron 会话缓存。
3. 更新本地设备标识。
4. 启动无头浏览器并打开 Typeless 登录页面。
5. 提交目标邮箱和验证码。
6. 把新会话加密写回 Typeless 本地数据目录。
7. 将账号摘要记录到仓库根目录的 accounts.json。

accounts.json、导出的词典和 scripts/.vendor 已被 .gitignore 排除，不会随正常提交上传。

## 路径兼容性

仓库内部只使用相对路径。Typeless 自身的文件必须位于操作系统用户目录中，因此脚本使用环境变量解析：

- Windows：从 %APPDATA%、%LOCALAPPDATA% 和 %ProgramFiles% 推导。
- macOS：从 $HOME 和标准 Applications 目录推导。
- 自定义 Typeless 安装位置：设置 TYPELESS_APP_PATH。
- 自定义浏览器位置：设置 PUPPETEER_EXECUTABLE_PATH 或 CHROME_PATH。

这些变量只需要指向本机实际位置，不应写入仓库文档或提交到 Git。

## 邮箱兼容性

通常可用：

- Gmail
- Outlook / Hotmail
- QQ 邮箱
- 163 邮箱
- 能正常接收验证码的自定义域名邮箱

已知不稳定或不可用：

- 包含加号别名的地址，例如 user+tag@example.com
- 临时邮箱
- 会破坏邮件签名或延迟转发的匿名转发服务

Google 或 Apple 登录的源账号可以先在 Typeless 桌面端手动登录，再运行导出。自动切换流程目前只支持邮箱验证码登录。

## 常见问题

### 无法读取登录态或 access token

打开 Typeless，重新登录源账号，等待同步完成，然后完全退出 Typeless 再导出。若本地 token 已过期，也需要重新登录。

### 登录页面按钮找不到

脚本兼容常见的中英文按钮文本。若 Typeless 更新了页面结构，请保留完整错误信息并提交 issue。

### account exceeded limit 或 websocket connection limit

先确保 Typeless 已完全退出，再重新运行切换脚本。问题仍存在时可能属于服务端账号或设备槽状态，本工具不能绕过服务端限制。

### 导入失败

先使用 --dry-run 检查文件格式，再确认目标账号登录态有效。bulk 模式失败时可保留错误输出，并尝试降低批次操作频率或使用 full 模式定位具体词条。

### Typeless 安装在非默认目录

为当前终端设置 TYPELESS_APP_PATH，再重新运行包装脚本。不要把个人绝对路径提交到仓库。

## 项目结构

~~~text
.
├── README.md
├── SKILL.md
├── LICENSE
├── scripts/
│   ├── export-dictionary.mjs
│   ├── export-dictionary.ps1
│   ├── export-dictionary.sh
│   ├── import-dictionary.mjs
│   ├── import-dictionary.ps1
│   ├── import-dictionary.sh
│   ├── read-user-session.mjs
│   ├── switch-account.mjs
│   ├── switch-account.ps1
│   ├── switch-account.sh
│   ├── reset-device-windows.ps1
│   └── reset-device-macos.sh
└── references/
    └── extract-dictionary.md
~~~

## 安全说明

- 不要在 issue、日志或聊天中发布 access token、refresh token 或 user-data.json。
- 导出的词典可能包含个人用词，应按私密数据处理。
- 切换账号会修改 Typeless 本地登录状态；操作前必须先导出需要保留的词典。
- 本工具只处理本地会话和官方接口，不保证绕过任何服务端账号、额度或设备限制。

## English quick guide

Run every command from the repository root. Repository files are referenced only through relative paths, so the checkout can live under any user profile or drive.

Windows:

~~~powershell
powershell -ExecutionPolicy Bypass -File .\scripts\export-dictionary.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\switch-account.ps1 --email "user@example.com"
powershell -ExecutionPolicy Bypass -File .\scripts\import-dictionary.ps1
~~~

macOS:

~~~bash
bash ./scripts/export-dictionary.sh
bash ./scripts/switch-account.sh --email "user@example.com"
bash ./scripts/import-dictionary.sh
~~~

The default import uses parallel bulk batches. Use --mode full --concurrency 12 to preserve dictionary metadata, or --dry-run to preview without writing. System-specific Typeless locations are resolved from environment variables; set TYPELESS_APP_PATH only when the desktop app is installed in a non-standard location.

## License

Apache License 2.0. See LICENSE.
