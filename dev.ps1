# VSCodeRecent 开发一键脚本：停止 → 打包 → 安装 → 校验 → 重启 Command Palette
#
# 用法（在仓库根目录）：
#   .\dev.cmd                      # Debug + x64，完整流程
#   .\dev.cmd -Configuration Release
#   .\dev.cmd -Platform ARM64
#   .\dev.cmd -NoRestart           # 不重启 Command Palette
#   .\dev.cmd -Stop                # 只停掉本扩展 + Command Palette
#   .\dev.cmd -Uninstall           # 卸载本扩展
#
# 直接跑 .\dev.ps1 在 WSL 的 UNC 路径下会被执行策略拦下，用 dev.cmd。

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = 'x64',

    # 不重启 Command Palette
    [switch]$NoRestart,

    # 只停止进程，不做别的
    [switch]$Stop,

    # 卸载扩展
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot

$PackageName = 'VSCodeRecent'
$CmdPalProcess = 'Microsoft.CmdPal.UI'
$ExtensionProcess = 'VSCodeRecent'

function Write-Step([string]$Message) {
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

# 扩展 exe 被 Command Palette 占用时重装会报 0x80073D02，所以必须先把进程杀干净。
#
# 注意两点：
#   1. Command Palette 自 0.12 起是独立 Store 应用（Microsoft.CmdPal.UI），
#      不在 PowerToys 里，重启 PowerToys 是没用的。
#   2. 光发 Stop-Process 不够 —— 进程可能还在退出中，文件仍被占用，
#      必须轮询等它真正消失，否则安装会静默失败（装的还是旧版本）。
function Stop-Everything {
    foreach ($name in @($ExtensionProcess, $CmdPalProcess)) {
        $procs = Get-Process -Name $name -ErrorAction SilentlyContinue
        if ($procs) {
            Write-Host "  停止 $name ($($procs.Count) 个进程)"
            $procs | Stop-Process -Force -ErrorAction SilentlyContinue
        }
    }

    # 轮询等待真正退出
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline) {
        $alive = @()
        foreach ($name in @($ExtensionProcess, $CmdPalProcess)) {
            $alive += @(Get-Process -Name $name -ErrorAction SilentlyContinue)
        }
        if ($alive.Count -eq 0) { return }

        # 再补一刀，然后继续等
        $alive | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 300
    }

    Write-Warning "有进程在 30 秒内没有退出，安装可能失败：$ExtensionProcess / $CmdPalProcess"
}

function Start-CmdPal {
    param([switch]$Quiet)

    $pkg = Get-AppxPackage -Name 'Microsoft.CommandPalette' -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if (-not $pkg) {
        Write-Warning "未找到 Microsoft.CommandPalette 包，请手动打开 Command Palette。"
        return
    }

    # AppUserModelId 形如 <PackageFamilyName>!<ApplicationId>
    $aumid = "$($pkg.PackageFamilyName)!App"
    if (-not $Quiet) { Write-Host "  启动 Command Palette ($aumid)" }
    Start-Process "shell:AppsFolder\$aumid"
}

# 取 msix 包内 VSCodeRecent.dll 的 SHA256。
#
# 不能拿 bin 下的构建产物去比：发布流程会做 ReadyToRun 预编译
# （pubxml 里 PublishReadyToRun=True），装进包里的是 obj\...\R2R\ 下那份
# 更大的 DLL，两者哈希天然不同。只有「包内 DLL」与「已安装 DLL」才是同源的。
function Get-MsixDllHash {
    param([string]$MsixPath)

    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $zip = [System.IO.Compression.ZipFile]::OpenRead($MsixPath)
    try {
        $entry = $zip.Entries |
            Where-Object { $_.FullName -eq 'VSCodeRecent.dll' } |
            Select-Object -First 1
        if (-not $entry) { return $null }

        $ms = New-Object System.IO.MemoryStream
        $src = $entry.Open()
        try { $src.CopyTo($ms) } finally { $src.Dispose() }

        $ms.Position = 0
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try {
            return [BitConverter]::ToString($sha.ComputeHash($ms)).Replace('-', '')
        } finally {
            $sha.Dispose()
            $ms.Dispose()
        }
    } finally {
        $zip.Dispose()
    }
}

# 取 msix 里的身份版本号。
#
# 版本号是这套流程的关键：PackageFullName = 名称 + 版本 + 架构 + 发布者。
# 版本没变就是同一个 PackageFullName，Add-AppxPackage 会当"已安装"直接返回成功，
# 一个文件都不换 —— 这正是必须靠哈希校验才能发现、且永远要第三次才成功的原因。
function Get-MsixVersion {
    param([string]$MsixPath)

    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $zip = [System.IO.Compression.ZipFile]::OpenRead($MsixPath)
    try {
        $entry = $zip.Entries |
            Where-Object { $_.FullName -eq 'AppxManifest.xml' } |
            Select-Object -First 1
        if (-not $entry) { return $null }

        $reader = New-Object System.IO.StreamReader($entry.Open())
        try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }

        if ($text -match '<Identity[^>]*Version="([0-9]+(?:\.[0-9]+)+)"') {
            return [version]$Matches[1]
        }
        return $null
    } finally {
        $zip.Dispose()
    }
}

function Get-InstalledDllHash {
    $pkg = Get-AppxPackage -Name $PackageName -ErrorAction SilentlyContinue
    if (-not $pkg) { return $null }

    $dll = Join-Path $pkg.InstallLocation 'VSCodeRecent.dll'
    if (-not (Test-Path $dll)) { return $null }

    return (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
}

# 安装包。返回是否成功，不抛异常，由调用方决定是否重试。
function Install-Package {
    param(
        [string]$MsixPath,
        [switch]$HardReset
    )

    Stop-Everything

    try {
        $existing = Get-AppxPackage -Name $PackageName -ErrorAction SilentlyContinue

        if ($HardReset -and $existing) {
            Write-Host "  先卸载旧包（强制换掉文件）"
            $existing | Remove-AppxPackage -ErrorAction Stop
            Start-Sleep -Milliseconds 500
            $existing = $null
        }

        if ($existing) {
            # 覆盖安装；-ForceUpdateFromAnyVersion 免得被版本号相同/更低挡住
            Add-AppxPackage -Path $MsixPath -AllowUnsigned -ForceUpdateFromAnyVersion -ErrorAction Stop
        } else {
            Add-AppxPackage -Path $MsixPath -AllowUnsigned -ErrorAction Stop
        }

        return $true
    } catch {
        Write-Warning "安装失败：$($_.Exception.Message)"
        return $false
    }
}

if ($Uninstall) {
    Write-Step "停止进程"
    Stop-Everything

    Write-Step "卸载 $PackageName"
    $pkg = Get-AppxPackage -Name $PackageName -ErrorAction SilentlyContinue
    if ($pkg) {
        $pkg | Remove-AppxPackage
        Write-Host "  已卸载"
    } else {
        Write-Host "  未安装，跳过"
    }
    return
}

if ($Stop) {
    Write-Step "停止进程"
    Stop-Everything
    Write-Host "  完成"
    return
}

Write-Step "停止 $ExtensionProcess 与 Command Palette"
Stop-Everything

Write-Step "还原依赖"
dotnet restore -p:Platform=$Platform
if ($LASTEXITCODE -ne 0) { throw "dotnet restore 失败" }

Write-Step "编译并打包 ($Configuration / $Platform)"
dotnet build -c $Configuration -p:Platform=$Platform
if ($LASTEXITCODE -ne 0) { throw "dotnet build 失败" }

# 产物名形如 AppPackages\VSCodeRecent_<ver>_<plat>_<cfg>_Test\VSCodeRecent_<ver>_<plat>_<cfg>.msix
$msix = Get-ChildItem -Path 'AppPackages' -Recurse -Filter '*.msix' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\Dependencies\\' } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $msix) { throw "没有找到 .msix，请检查 dotnet build 的输出" }
Write-Host "  安装包: $($msix.Name)  ($($msix.LastWriteTime))"

$msixHash = Get-MsixDllHash -MsixPath $msix.FullName
if (-not $msixHash) { throw "msix 里没有 VSCodeRecent.dll" }
Write-Host "  包内 DLL: $($msixHash.Substring(0, 16))..."

$msixVersion = Get-MsixVersion -MsixPath $msix.FullName
if (-not $msixVersion) { throw "读不出 msix 里的版本号" }

# 版本号没变 → PackageFullName 没变 → Add-AppxPackage 覆盖安装是空操作
# （返回成功但不换文件）。这时第一次就得走卸载重装，否则前两次必然白跑。
$existingVersion = (Get-AppxPackage -Name $PackageName -ErrorAction SilentlyContinue |
    Select-Object -First 1).Version
$sameVersion = $existingVersion -and $existingVersion -eq $msixVersion

Write-Host "  版本: 包内 $msixVersion / 已装 $(if ($existingVersion) { $existingVersion } else { '(未安装)' })"
if ($sameVersion) {
    Write-Host "  版本相同，覆盖安装不会换文件 → 直接卸载重装" -ForegroundColor Yellow
}

# 安装 → 校验 → 不一致就重试。
#
# 覆盖安装还可能被 0x80073D02 挡掉（扩展进程占着文件），而 Add-AppxPackage 失败
# 不一定抛出可捕获的异常，所以不能只看它的返回值，必须拿哈希确认文件真的换了。
# 最后一轮用「先卸载再装」强杀，保证一定能换掉。
Write-Step "安装并校验"
$maxAttempts = 3
$installedHash = $null

for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
    $hardReset = $sameVersion -or ($attempt -eq $maxAttempts)
    if ($attempt -gt 1) {
        Write-Host "  第 $attempt 次尝试$(if ($hardReset) { '（先卸载，强制换文件）' } else { '' })"
    }

    [void](Install-Package -MsixPath $msix.FullName -HardReset:$hardReset)

    $installedHash = Get-InstalledDllHash
    if ($installedHash -and $installedHash -eq $msixHash) { break }

    if ($installedHash) {
        Write-Host "  已装 DLL: $($installedHash.Substring(0, 16))...  不一致，重试"
    } else {
        Write-Host "  读取安装位置失败，重试"
    }

    Start-Sleep -Seconds 1
}

if (-not $installedHash -or $installedHash -ne $msixHash) {
    throw @"
安装未能生效：已安装的 DLL 与包内的不一致。
  包内: $($msixHash.Substring(0, 16))...
  已装: $(if ($installedHash) { $installedHash.Substring(0, 16) + '...' } else { '(读不到)' })
手动执行 .\dev.cmd -Uninstall 后再重跑本脚本。
"@
}

$installed = Get-AppxPackage -Name $PackageName
Write-Host "  已安装: $($installed.Version)  状态: $($installed.Status)" -ForegroundColor Green
Write-Host "  安装位置 DLL 与包内一致 ✓" -ForegroundColor Green

# 安装期间 CmdPal 可能又把扩展拉起来（用旧文件），再清一次，
# 保证接下来 CmdPal 激活时读的是新 DLL。
Stop-Everything

if (-not $NoRestart) {
    Write-Step "启动 Command Palette"
    Start-CmdPal
}

Write-Host ''
Write-Host "完成（安装已校验一致）。" -ForegroundColor Green
Write-Host "打开 Command Palette (Win+Alt+Space)：" -ForegroundColor DarkGray
Write-Host "  - 直接输入项目名（如 roman）即可内联命中" -ForegroundColor DarkGray
Write-Host "  - 或输入 { 加关键词，如 {git" -ForegroundColor DarkGray
Write-Host "  - 扩展的顶层命令在根列表最末尾" -ForegroundColor DarkGray
