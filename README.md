# VSCode Recent

[![Build](https://github.com/ignimutos/VSCodeRecent/actions/workflows/build.yml/badge.svg)](https://github.com/ignimutos/VSCodeRecent/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

> 切换语言：[English](./README_en.md)

PowerToys Command Palette 扩展 —— 快速访问 VSCode 最近打开的项目。

## 功能

- 自动显示 VSCode 最近打开的文件夹与工作区
- 列表按最后打开时间倒序
- 支持 WSL 远程项目（自动还原成 `vscode-remote://wsl+` 打开）
- 回车一键在 VSCode 中打开
- 设置项「显示文件」可切换是否把单独打开的文件也列出来（默认关）；打开后列表分成
  **项目 (N)** / **文件 (N)** 两组（标题带条数），与 VSCode 自己的最近列表一致 ——
  这两段之间没有可比较的时间戳（VSCode 只存顺序不存时间），所以不揉成一条时间线。
  CmdPal 的列表页只能单列滚动，做不到左右分栏；文件太多时直接打字筛选，比滚更快

## 安装

1. 到 [Releases](https://github.com/ignimutos/VSCodeRecent/releases) 下载对应架构的 `.msix`（`x64` 或 `ARM64`）
2. 开启开发者模式：设置 → 系统 → 开发者选项 → 开发人员模式
3. **用管理员身份**打开 PowerShell，进到 `.msix` 所在目录
4. 安装：

```powershell
Add-AppxPackage -Path .\VSCodeRecent-x64.msix -AllowUnsigned
```

5. 打开 Command Palette，运行 `Reload Command Palette Extension`

> 两个前提，缺一不可：
> - **开发者模式**：未签名包要求系统允许旁加载。
> - **管理员权限**：包里含可执行文件，未签名包只能装给「所有用户」，这需要提权。
>   不开管理员会报 `0x80073D2C` 或权限错误。
>
> 覆盖安装旧版本时加 `-ForceUpdateFromAnyVersion`，否则版本号相同/更低会被挡下。

## 使用

1. `Win + Alt + Space` 打开 Command Palette（默认快捷键，可在 Command Palette 设置里改）
2. **滚到根列表最末尾** —— 扩展的顶层命令排在系统命令之后；若开了紧凑模式，先按 `↓` 或 `Tab` 展开列表
3. 选择 **VSCode Recent** 回车进入列表页
4. 选中项目回车，在 VSCode 中打开

更快的一条路：在根搜索框直接敲项目名（至少 2 个字符）。扩展会以回退项的形式出现在结果里 ——

- **只有一条命中**：回车**直接打开**该项目，不经过列表页
- **多条命中**：回车进入列表页，且搜索框已填好你敲的词，继续改即可

> 回退项只在搜索框有输入时出现（宿主规定：`Title` 为空的项不进根列表，而回退项平时必须
> 是空的才不会白占一行），所以不能「空着搜索框就看到项目」。

## 设置

在 Command Palette 的扩展设置里可打开 **显示文件**（默认关）。VSCode 的最近记录里绝大多数是
单独打开的文件，默认过滤掉，只留文件夹和工作区。设置存在
`%LOCALAPPDATA%\VSCodeRecent\settings.json`。

## 系统要求

- Windows 11（10.0.19041.0+）
- Command Palette 0.12+（已是独立 Store 应用，不再随 PowerToys 分发）
- VSCode 已安装且 `code` 命令可用

## VSCode 数据位置

按以下优先级自动查找，逐级回退：

1. **共享存储**（VSCode 1.75+ 默认）
   - `%USERPROFILE%\.vscode-shared\sharedStorage\state.vscdb`
2. **SQLite 数据库**
   - `%APPDATA%\Code\User\globalStorage\*\state.vscdb`
3. **传统 JSON 格式**（兼容旧版本）
   - `%APPDATA%\Code\User\globalStorage\storage.json`

## 开发

需要 .NET 10 SDK。项目目标框架为 `net10.0-windows10.0.26100.0`。

**推荐用一键脚本**（停止进程 → 打包 → 安装 → 重启 Command Palette）：

```powershell
.\dev.cmd                    # Debug + x64，完整流程
.\dev.cmd -Configuration Release
.\dev.cmd -Platform ARM64
.\dev.cmd -NoRestart          # 不重启 Command Palette
.\dev.cmd -Stop               # 只停掉扩展和 Command Palette
.\dev.cmd -Uninstall          # 卸载扩展
```

> 如果在 WSL 的 UNC 路径下直接跑 `.\dev.ps1` 报"未进行数字签名"，用 `dev.cmd` 入口 ——
> 它对单条命令使用 `-ExecutionPolicy Bypass`，不改系统执行策略。
> 也可以在 **Windows 本地磁盘**（而非 `\\wsl.localhost\...`）上跑 `.\dev.ps1`。

手动构建：

```powershell
dotnet restore -p:Platform=x64
dotnet build -c Debug -p:Platform=x64
```

产物：`AppPackages\VSCodeRecent_1.0.0.0_x64_Debug_Test\VSCodeRecent_1.0.0.0_x64_Debug.msix`

如果 `dotnet build` 没有生成 `.msix`，改用官方 recipe 手动 publish：

```powershell
dotnet publish -c Debug -p:Platform=x64 `
  -p:WindowsPackageType=MSIX `
  -p:AppxPackageDir="$PWD\AppPackages\" `
  -p:GenerateAppxPackageOnBuild=true `
  -p:AppxBundle=Never
```

安装并测试：

```powershell
Add-AppxPackage -Path .\AppPackages\VSCodeRecent_1.0.0.0_x64_Debug_Test\VSCodeRecent_1.0.0.0_x64_Debug.msix -AllowUnsigned
```

> 未签名安装需要 `Package.appxmanifest` 的 `Publisher` 里带上 Windows 保留的
> `OID.2.25.311729368913984317654407730594956997722=1`（见
> [MS 文档](https://learn.microsoft.com/en-us/windows/msix/package/unsigned-package)），
> 仓库里已经配好了。真正允许未签名部署的是 `-AllowUnsigned` 参数本身。

改完代码后重新安装，并在 Command Palette 里运行一次 `Reload Command Palette Extension` —— 否则面板不会重新加载扩展。

卸载：

```powershell
Get-AppxPackage -Name "VSCodeRecent" | Remove-AppxPackage
```

## 发布

打 tag 即触发构建与发布：

```bash
git tag v1.0.0
git push origin v1.0.0
```

`.github/workflows/build.yml` 会：

1. 从 tag 解析版本号，覆写 `Package.appxmanifest` 的 `Version`（`v1.2.3` → `1.2.3.0`）
2. 在 x64 与 arm64 两个原生 runner 上各跑测试并构建 `x64` / `ARM64` 两个 `.msix`
3. 建 Release 并上传 `VSCodeRecent-x64.msix` / `VSCodeRecent-ARM64.msix`

### 发布到 Microsoft Store

`.github/workflows/store.yml` 在 Release 发布后调用
[MSStore CLI](https://github.com/microsoft/msstore-cli) 提交。Store 会**用自己的证书
重新签名**，所以不需要自签证书。

工作流做的事：下载 Release 里的 `.msix` → 用 `makeappx` 合成 `.msixbundle`
→ `msstore publish`（上传 → 提交 → 轮询 → 发布）。

> Store 一次只接受一个包文件，所以要合成 bundle；否则第二次 `publish` 会把第一次
> 创建的草稿提交删掉。

**这个工作流是可选的**：下面的 secret 没配齐时，job 第一步就检测出来并跳过，
显示为绿色成功，不会失败、也不会影响 Release 分发。想彻底去掉就直接删掉
`.github/workflows/store.yml`。

**前置步骤（一次性，全部手工）：**

1. 注册 [Partner Center](https://partner.microsoft.com/dashboard) 开发者账号（个人约 $19 一次性）
2. 在 Partner Center 预留应用名，把拿到的 `Package/Identity/Name`、`Publisher`、`PublisherDisplayName`
   填进 `Package.appxmanifest` —— 必须与 Partner Center **逐字符一致**（含大小写）。
   此时同步删掉 `Publisher` 里的未签名 OID
3. 建 Azure AD 租户并关联 Partner Center，注册一个 Azure AD 应用、授予 **Manager** 角色
4. 在 Partner Center 手工提交一次（年龄分级问卷等）。API 无法创建**第一个**提交，
   只能用带列表信息的那次已有提交续写
5. 加两个 GitHub secret：
   - `AZURE_TENANT_ID` / `AZURE_CLIENT_ID` / `AZURE_CLIENT_SECRET` / `SELLER_ID`（Partner Center 账号设置里的 Seller ID）
   - `PRODUCT_ID`（Partner Center 里的应用 ID，即 `AppId`）

之后每次发 Release 自动提交。也可在 Actions 页面手动 `workflow_dispatch` 触发。

### 代码签名

走 Store 不需要自签（Store 重新签名）。仅当将来要绕过 Store 自行分发签名的 MSIX 才需要，
届时同步改两处：

1. `Package.appxmanifest` 的 `Publisher` 改成证书的 CN，删掉 `OID.2.25.311729368913984317654407730594956997722=1`
2. `VSCodeRecent.csproj` 的 `<AppxPackageSigningEnabled>false</AppxPackageSigningEnabled>` 改为 `true`，
   并给 `PackageCertificateKeyFile` / `PackageCertificateThumbprint`

## 项目结构

```
.
├── VSCodeRecent.csproj              # 项目文件
├── Directory.Packages.props         # 集中式包版本管理
├── global.json                      # SDK 版本固定
├── Package.appxmanifest             # MSIX 清单（COM 服务器 + Command Palette 扩展注册）
├── app.manifest                     # 应用清单（DPI 感知）
├── Program.cs                       # 入口点，COM 服务器宿主
├── VSCodeRecentExtension.cs         # IExtension 实现（COM 激活入口）
├── VSCodeCommandsProvider.cs        # 命令提供者（继承 Toolkit 的 CommandProvider）
├── VSCodeInstall.cs                 # VSCode 装在哪：位置探测（标准/便携/Scoop）
├── VSCodeRecentHistory.cs           # 读取最近记录：枚举数据源、去重排序、缓存
├── VSCodeHistorySource.cs           # 单个数据源 + JSON 解析规则（ParseHistoryKey）
├── VSCodeUri.cs                     # VSCode URI → 打开目标（只在这里解一次码）
├── VSCodeRecentSettings.cs          # 扩展设置（JsonSettingsManager）
├── Commands/
│   └── OpenInVSCodeCommand.cs       # 在 VSCode 中打开（只负责启动）
├── Pages/
│   └── VSCodeRecentListPage.cs      # 最近项目列表页
├── Models/
│   ├── VSCodeItem.cs                # 一条最近记录
│   ├── ItemKind.cs                  # 类型：显示名/是否算项目/并列兜底顺序
│   ├── ItemGroups.cs                # 列表页分组切割（项目 / 文件）
│   └── VSCodeOpenTarget.cs          # 打开目标：本地 / WSL，含命令行与显示路径
├── tests/VSCodeRecent.Tests/        # 纯逻辑测试（xUnit），不需要 VSCode
└── Assets/                          # MSIX 图标资源
```

### 测试

```powershell
dotnet test tests\VSCodeRecent.Tests\VSCodeRecent.Tests.csproj -p:Platform=x64
```

覆盖 URI 解析、JSON 解析、过滤、排序与分组 —— 这些都不碰文件系统，所以不需要装 VSCode 或
安装扩展。文件系统相关的部分（位置探测、读库）仍只能手动验证。

CI（`.github/workflows/build.yml`）会在两个平台上各跑一遍：`x64` 用 `windows-latest`，
`ARM64` 用 `windows-11-arm`。ARM64 **必须**跑在 arm64 宿主上 —— 测试程序集按
`Platform` 编成 `win-arm64`，x64 runner 上装不出对应的 dotnet 测试宿主。

### 实现要点

- 提供者**必须继承** `Microsoft.CommandPalette.Extensions.Toolkit.CommandProvider`，不要手写 `ICommandProvider` 接口 —— 基类负责实现全部胶水成员，子类只需 `override TopLevelCommands()`。
- `VSCodeRecentExtension.cs` 的 `[Guid]` 必须与 `Package.appxmanifest` 里的 COM `Class Id` 一致。
- 这是 WinRT/COM 进程外扩展，**不能用 PowerToys Run 的 `plugin.json` 方式加载**。
- URI 解码**只用 `Uri.UnescapeDataString`，不要用 `HttpUtility.UrlDecode`** —— 后者是表单编码语义，会把路径里的 `+` 当空格吃掉。
- PATH 上的 `code` 命令枚举**只有 `VSCodeInstall.CodeExecutableNames` 一份**，启动与便携版探测共用，避免两处清单漂移。
- WSL 的 distro 名在生成命令行时转小写，而路径段保持原样；两者混写会打不开（见 `VSCodeOpenTarget` 注释）。
- 列表页**必须继承 `DynamicListPage`**，不能是 `ListPage`：普通 ListPage 由宿主做前缀模糊匹配，
  页面拿不到输入框内容。`SearchText` 是**唯一的查询来源** —— 宿主只在页面刷新时读一次它
  （`ListViewModel` 里 `SearchText = model.SearchText`），之后每次输入走
  `UpdateSearchText`。另存一份关键词并优先用它，会让「进页面后清空搜索框，列表却还是旧结果」。
- 分组**不要自己插 `Separator`**：宿主按 `IListItem.Section` 认分组，且要求 `Command` 为空
  才算 section header。Toolkit 的 `Section` 构造时就自动插好了，直接用即可。

## 许可证

[MIT](LICENSE)
