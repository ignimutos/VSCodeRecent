# Icons

`workspace.png` / `folder.png` / `file.png` (and the `.dark` variants) are derived from
[Fluent System Icons](https://github.com/microsoft/fluentui-system-icons) by Microsoft,
used under the MIT License.

| File | Source |
| --- | --- |
| `folder.png` | `assets/Folder/SVG/ic_fluent_folder_24_regular.svg` |
| `file.png` | `assets/Document/SVG/ic_fluent_document_24_regular.svg` |
| `workspace.png` | `assets/Board/SVG/ic_fluent_board_24_regular.svg` |

Each SVG was recolored (`#212121` for the light-theme variant, `#E8E8E8` for the dark-theme
variant) and rasterized to a 128×128 PNG with ImageMagick:

```bash
sed 's/#212121/#E8E8E8/g' ic_fluent_folder_24_regular.svg > _t.svg
magick -background none -density 384 _t.svg -resize 128x128 folder.dark.png
```

Fluent System Icons license: MIT — https://github.com/microsoft/fluentui-system-icons/blob/main/LICENSE

## Light/dark variants

Both variants are shipped and passed to `IconHelpers.FromRelativePaths(light, dark)` from
`Pages/VSCodeRecentListPage.cs`. Note a host-side bug in the Command Palette (not in this
extension): on first open, and again after a theme switch, some rows render the wrong variant
and appear washed out. `IconBox._lastTheme` is uninitialized (`ElementTheme.Default`) while
`SourceRequested` fires on subscription, before `Loaded` assigns `ActualTheme`, so the host's
`args.Theme == ElementTheme.Light ? Light : Dark` picks the dark asset; the later `Refresh()`
re-requests correctly but both requests pass the same `ReferenceEquals(sourceKey, SourceKey)`
check and the slower one wins. Reopening the palette clears it. Upstream is folding theme into
the icon cache identity in PR #50181–#50192 ("CmdPal Icons"). See the TODO on
`VSCodeRecentListPage.IconForType` before changing this mechanism.
