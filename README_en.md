# VSCode Recent

[![Build](https://github.com/ignimutos/VSCodeRecent/actions/workflows/build.yml/badge.svg)](https://github.com/ignimutos/VSCodeRecent/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

> Read this in other languages: [简体中文](./README.md)

A PowerToys Command Palette extension — quick access to recently opened VSCode projects.

## Features

- Auto-display recently opened VSCode folders and workspaces
- List ordered by last-opened time, newest first
- WSL remote project support (restores the `vscode-remote://wsl+` URI)
- One keystroke to open in VSCode
- A **Show files** setting to include individually opened files (off by default). When on,
  the list splits into **Projects (N)** / **Files (N)** groups, each labelled with its count —
  the same split VSCode itself uses, since the two segments have no comparable timestamps
  (VSCode stores order, not time). CmdPal list pages are a single scrolling column, so side-by-side
  columns are not possible; when the file list is long, typing to filter beats scrolling

## Installation

1. Grab the `.msix` for your architecture (`x64` or `ARM64`) from
   [Releases](https://github.com/ignimutos/VSCodeRecent/releases)
2. Enable Developer Mode: Settings → System → For developers → Developer Mode
3. Open PowerShell **as administrator** and `cd` to the folder holding the `.msix`
4. Install:

```powershell
Add-AppxPackage -Path .\VSCodeRecent-x64.msix -AllowUnsigned
```

5. Open Command Palette and run `Reload Command Palette Extension`

> Two prerequisites, both required:
> - **Developer Mode** — an unsigned package needs sideloading allowed.
> - **Administrator** — the package contains executables, and an unsigned package can
>   only be installed for all users, which requires elevation. Without it you get
>   `0x80073D2C` or a permission error.
>
> When overwriting an older version, add `-ForceUpdateFromAnyVersion`, otherwise an equal
> or lower version number is rejected.

## Usage

1. Press `Win + Alt + Space` to open Command Palette (default; changable in Command Palette settings)
2. **Scroll to the very bottom of the root list** — extension top-level commands come after the built-in ones. In compact mode, press `↓` or `Tab` first to expand the list
3. Select **VSCode Recent** and press Enter
4. Select a project and press Enter to open it in VSCode

There is a faster path: type a project name (at least 2 characters) into the root search box.
The extension shows up in the results as a fallback item —

- **Exactly one match**: Enter **opens that project directly**, skipping the list page
- **Several matches**: Enter opens the list page with your query already filled in; keep editing it there

> The fallback only appears once the search box has input — the host keeps items with an empty `Title`
> out of the root list, and a fallback must start empty or it wastes a row. So "see your projects with
> an empty search box" is not possible.

## Settings

In the Command Palette extension settings, enable **Show files** (off by default). Most VSCode
recent entries are individually opened files, so they are filtered out unless you opt in.
Settings live in `%LOCALAPPDATA%\VSCodeRecent\settings.json`.

## Requirements

- Windows 11 (10.0.19041.0+)
- Command Palette 0.12+ (a standalone Store app since that version, no longer shipped with PowerToys)
- VSCode installed with the `code` command available

## VSCode Data Location

Searched in priority order, falling back at each step:

1. **Shared Storage** (VSCode 1.75+ default)
   - `%USERPROFILE%\.vscode-shared\sharedStorage\state.vscdb`
2. **SQLite Database**
   - `%APPDATA%\Code\User\globalStorage\*\state.vscdb`
3. **Legacy JSON format**
   - `%APPDATA%\Code\User\globalStorage\storage.json`

## Development

Requires the .NET 10 SDK. Target framework is `net10.0-windows10.0.26100.0`.

**Use the one-shot script** (stop processes → package → install → restart Command Palette):

```powershell
.\dev.cmd                    # Debug + x64, full pipeline
.\dev.cmd -Configuration Release
.\dev.cmd -Platform ARM64
.\dev.cmd -NoRestart          # skip restarting Command Palette
.\dev.cmd -Stop               # only stop the extension and Command Palette
.\dev.cmd -Uninstall          # uninstall the extension
```

> If running `.\dev.ps1` directly from a WSL UNC path fails with "not digitally signed",
> use the `dev.cmd` entry point — it applies `-ExecutionPolicy Bypass` to that single
> invocation without changing the system execution policy. Alternatively run
> `.\dev.ps1` from a **local Windows disk** rather than `\\wsl.localhost\...`.

Manual build:

```powershell
dotnet restore -p:Platform=x64
dotnet build -c Debug -p:Platform=x64
```

Output: `AppPackages\VSCodeRecent_1.0.0.0_x64_Debug_Test\VSCodeRecent_1.0.0.0_x64_Debug.msix`

If `dotnet build` does not produce a `.msix`, use the official publish recipe:

```powershell
dotnet publish -c Debug -p:Platform=x64 `
  -p:WindowsPackageType=MSIX `
  -p:AppxPackageDir="$PWD\AppPackages\" `
  -p:GenerateAppxPackageOnBuild=true `
  -p:AppxBundle=Never
```

Install and test:

```powershell
Add-AppxPackage -Path .\AppPackages\VSCodeRecent_1.0.0.0_x64_Debug_Test\VSCodeRecent_1.0.0.0_x64_Debug.msix -AllowUnsigned
```

> Unsigned install requires the manifest `Publisher` to carry the Windows-reserved
> `OID.2.25.311729368913984317654407730594956997722=1` (see
> [MS docs](https://learn.microsoft.com/en-us/windows/msix/package/unsigned-package)).
> The repo is already configured this way; `-AllowUnsigned` is what actually permits
> the unsigned deployment.

After rebuilding, reinstall and run `Reload Command Palette Extension` from the palette — otherwise it will not reload the extension.

Uninstall:

```powershell
Get-AppxPackage -Name "VSCodeRecent" | Remove-AppxPackage
```

## Publishing

Pushing a tag runs the build and creates a release:

```bash
git tag v1.0.0
git push origin v1.0.0
```

`.github/workflows/build.yml` will:

1. Derive the version from the tag and stamp it into `Package.appxmanifest`
   (`v1.2.3` → `1.2.3.0`)
2. Run the tests and build the `x64` and `ARM64` `.msix` packages on native x64 and arm64
   runners respectively
3. Create a release and upload `VSCodeRecent-x64.msix` / `VSCodeRecent-ARM64.msix`

### Publishing to the Microsoft Store

`.github/workflows/store.yml` submits through the
[MSStore CLI](https://github.com/microsoft/msstore-cli) after a release is published.
The Store **re-signs** packages with its own certificate, so no signing certificate of
your own is needed.

The workflow downloads the `.msix` from the release, combines the architectures into an
`.msixbundle` with `makeappx`, then runs `msstore publish` (upload → commit → poll → publish).

> The Store accepts a single package file per submission, hence the bundle. Two separate
> `publish` calls would make the second one delete the draft submission the first created.

**This workflow is optional**: when the secrets below are not configured, the first step
detects that and the job skips — it shows as a green success and never blocks release
distribution. To drop it entirely, just delete `.github/workflows/store.yml`.

**Prerequisites (one-time, all manual):**

1. Register a [Partner Center](https://partner.microsoft.com/dashboard) developer account
   (~$19 one-off for individuals)
2. Reserve the app name in Partner Center, then fill the assigned
   `Package/Identity/Name`, `Publisher` and `PublisherDisplayName` into
   `Package.appxmanifest` — they must match Partner Center **exactly**, including case.
   Drop the unsigned-namespace OID from `Publisher` at the same time
3. Create an Azure AD tenant, associate it with Partner Center, and register an Azure AD
   application with the **Manager** role
4. Submit once by hand from Partner Center (age ratings questionnaire etc.). The API cannot
   create the *first* submission; it can only continue an existing one that already has
   listings
5. Add the GitHub secrets:
   - `AZURE_TENANT_ID` / `AZURE_CLIENT_ID` / `AZURE_CLIENT_SECRET` / `SELLER_ID` (the
     Seller ID from Partner Center account settings)
   - `PRODUCT_ID` (the Partner Center app ID, a.k.a. `AppId`)

Every later release is then submitted automatically. You can also trigger it by hand from
the Actions tab via `workflow_dispatch`.

### Code signing

The Store path needs no signing of your own (the Store re-signs). This is only needed if
you later ship signed MSIX packages outside the Store. Two places must change:

1. Set `Package.appxmanifest`'s `Publisher` to the certificate CN and remove
   `OID.2.25.311729368913984317654407730594956997722=1`
2. Flip `<AppxPackageSigningEnabled>false</AppxPackageSigningEnabled>` to `true` in
   `VSCodeRecent.csproj` and supply `PackageCertificateKeyFile` /
   `PackageCertificateThumbprint`

Pick either **Azure Trusted Signing** (from $9.99/month, CI-friendly) or a traditional OV
certificate (EV needs a hardware token, which does not fit pure CI).

## Project Structure

```
.
├── VSCodeRecent.csproj              # Project file
├── Directory.Packages.props         # Central package version management
├── global.json                      # Pinned SDK version
├── Package.appxmanifest             # MSIX manifest (COM server + Command Palette registration)
├── app.manifest                     # Application manifest (DPI awareness)
├── Program.cs                       # Entry point, COM server host
├── VSCodeRecentExtension.cs         # IExtension implementation (COM activation entry)
├── VSCodeCommandsProvider.cs        # Command provider (derives from Toolkit CommandProvider)
├── VSCodeInstall.cs                 # Where VSCode lives: location probes (standard / portable / Scoop)
├── VSCodeRecentHistory.cs           # VSCode history reader: sources, dedupe, sort, cache
├── VSCodeHistorySource.cs           # One data source + JSON parsing rules (ParseHistoryKey)
├── VSCodeUri.cs                     # VSCode URI → open target (decoded in exactly one place)
├── VSCodeRecentSettings.cs          # Extension settings (JsonSettingsManager)
├── Commands/
│   └── OpenInVSCodeCommand.cs       # Open in VSCode (launching only)
├── Pages/
│   └── VSCodeRecentListPage.cs      # Recent projects list page
├── Models/
│   ├── VSCodeItem.cs                # One recent entry
│   ├── ItemKind.cs                  # Kind: label / is-project / tie-break rank
│   ├── ItemGroups.cs                # List-page grouping (projects / files)
│   └── VSCodeOpenTarget.cs          # Open target: local / WSL, command line + display path
├── tests/VSCodeRecent.Tests/        # Pure-logic tests (xUnit), no VSCode needed
└── Assets/                          # MSIX icon assets
```

### Implementation Notes

- The provider **must derive from** `Microsoft.CommandPalette.Extensions.Toolkit.CommandProvider`; do not hand-implement `ICommandProvider`. The base class supplies all the plumbing, and subclasses only `override TopLevelCommands()`.
- The `[Guid]` in `VSCodeRecentExtension.cs` must match the COM `Class Id` in `Package.appxmanifest`.
- This is an out-of-process WinRT/COM extension. It **cannot** be loaded the PowerToys Run `plugin.json` way.
- The list page **must derive from `DynamicListPage`**, not `ListPage`: with a plain `ListPage` the host
  does the prefix fuzzy-matching and the page never sees the query. `SearchText` is the **single source
  of truth** for the query — the host only reads it once, when it initializes the page
  (`SearchText = model.SearchText` in `ListViewModel`), and every later keystroke arrives via
  `UpdateSearchText`. Keeping a second copy and preferring it causes "clearing the search box leaves the
  old results".
- Do **not** insert `Separator`s for grouping yourself: the host groups by `IListItem.Section`, and an
  item only counts as a section header when its `Command` is empty. The Toolkit `Section` helper inserts
  the separator for you.

## License

[MIT](LICENSE)
