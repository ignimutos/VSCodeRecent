# MaterialIcons

按文件名关联的彩色图标，来源：[Material Icon Theme](https://github.com/PKief/vscode-material-icon-theme)
（`pkief.material-icon-theme`，MIT License，Copyright (c) 2025 Material Extensions）。
VS Code 资源管理器里的图标就是这套。

## 文件构成

| 文件 | 说明 |
| --- | --- |
| `*.png` | 图标本体，64×64，透明底 |
| `material-icons.json` | 文件名 → 图标名的关联表 |

PNG 已经过无损重压缩（`optipng -o7 -strip all` 再 `zopflipng -m`），比初次导出小约 27%，
像素与导出结果逐点一致。重新生成时必须补上这一步，否则体积会白涨回去。

`material-icons.json` 由主题的 `dist/material-icons.json` 裁出，只保留两张关联表：

- `fileNames` —— 精确文件名规则（`package.json` → `nodejs`）
- `fileExtensions` —— 扩展名规则（`yaml` → `yaml`）

值已改写为裸图标名（去掉 `./../icons/` 前缀与 `.svg` 后缀），并补了一条 `defaults`
给出文件夹 / 文件 / 工作区的兜底图标。运行时由 `Models/MaterialIconTheme.cs` 读取，
匹配规则与 VS Code 一致：**先精确文件名，再按从长到短的扩展名**。

## 两处刻意的偏离

1. **`vscode.svg` 被排除。** 它在主题里是 `.code-workspace` / `*.vsix` 等关联的目标，
   但该 SVG 就是 VS Code 官方 logo。官方
   [brand 指南](https://code.visualstudio.com/brand) 明确禁止把该图标用于
   "identify or promote your own product, service, application, VS Code extension"，
   而 `microsoft/vscode` 的 MIT 只覆盖代码、不含商标授权。这七个原本指向它的
   fileName / extension 规则改指 `folder-open`，工作区行也统一用 `folder-open`。
   见 `Pages/VSCodeRecentListPage.cs` 的 `IconFor`。
2. **只保留 587 个图标。** 主题全量有 1251 个 SVG，但裁出的关联表只引用到 587 个，
   其余（各框架/厂商的专属图标）在本扩展的三档 `ItemKind` 下永远取不到，
   已删除以免仓库白白多出 5 MB。

## 重新生成

上游更新时按下面步骤重跑。`rsvg-convert` 来自 `librsvg2-bin`；ImageMagick 内置的
MSVG 渲染器会丢掉 fill，不能用。

```bash
THEME=<material-icon-theme>/dist/material-icons.json
SVG=<material-icon-theme>/icons
BASE=Assets/MaterialIcons

for f in "$SVG"/*.svg; do
  b=$(basename "$f" .svg)
  [ "$b" = vscode ] && continue          # 见上文「刻意的偏离」
  rsvg-convert -w 64 -h 64 "$f" -o /tmp/_i.png
  magick /tmp/_i.png -background none -gravity center -extent 64x64 "$BASE/$b.png"
done
```

个别文件的 viewBox 极大（如 `editorconfig.svg` 是 3473×3473），ImageMagick 会报
`cache resources exhausted`，直接走 `rsvg-convert` 即可。

导出后做一次无损重压缩（`apt-get install optipng zopfli`）：

```bash
cd Assets/MaterialIcons
for f in *.png; do
  optipng -o7 -strip all -quiet "$f"
  zopflipng -y -m "$f" "$f"
done
```

两者都只改压缩方式、不动像素，可随时重跑。

然后重新裁表：从 `material-icons.json` 取 `fileNames` / `fileExtensions`，
把值映射成裸图标名（`iconDefinitions[x].iconPath` 的 basename），丢掉值为 `vscode`
的项改写为 `folder-open`，最后删除未被任何规则引用的 PNG。

License: MIT —— https://github.com/PKief/vscode-material-icon-theme/blob/main/LICENSE
