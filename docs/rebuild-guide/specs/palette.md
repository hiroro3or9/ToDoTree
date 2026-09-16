# 配色（全キー）

ライトとダークで**同じキー**を用意する。値はWPFの`#AARRGGBB`。先頭2桁はアルファ。
`Themes/Palette.Light.xaml` と `Palette.Dark.xaml` に`SolidColorBrush`として並べ、
画面側は`DynamicResource`で引く。ここにない色をコード内へ直接書かない。

## 基本（ウィンドウ・面・文字）

| キー | Light | Dark |
|---|---|---|
| `Brush.Window` | `#FFEAF0F3` | `#FF09111D` |
| `Brush.Chrome` | `#FFF8FBFC` | `#FF101C2C` |
| `Brush.Surface` | `#FFFFFFFF` | `#FF132235` |
| `Brush.SurfaceAlt` | `#FFF0F5F6` | `#FF192B40` |
| `Brush.SurfaceHover` | `#FFEDEFF4` | `#FF232935` |
| `Brush.SurfaceActive` | `#FFE3E6ED` | `#FF2C3341` |
| `Brush.Canvas` | `#FFF3F8F9` | `#FF0B1625` |
| `Brush.Border` | `#FFD8E4E8` | `#FF263A50` |
| `Brush.BorderStrong` | `#FFD3D8E2` | `#FF3A4252` |
| `Brush.Divider` | `#FFEBEDF2` | `#FF1F242E` |
| `Brush.Text` | `#FF192D40` | `#FFE7EAF2` |
| `Brush.TextSubtle` | `#FF536D7C` | `#FFACBED0` |
| `Brush.TextFaint` | `#FF667E8C` | `#FF8FA3BA` |
| `Brush.TextOnAccent` | `#FFFFFFFF` | `#FF0C1020` |
| `Brush.Accent` | `#FF087F73` | `#FF66DFC3` |
| `Brush.AccentHover` | `#FF06685F` | `#FF98ECD8` |
| `Brush.AccentPressed` | `#FF05564F` | `#FF42BFA4` |
| `Brush.AccentSoft` | `#FFE5F5F0` | `#FF173C3C` |
| `Brush.AccentBorder` | `#FFACD9CB` | `#FF34766E` |
| `Brush.Danger` | `#FFDC2626` | `#FFF87171` |
| `Brush.DangerSoft` | `#FFFEF0F0` | `#FF2A181B` |
| `Brush.Warning` | `#FFE0640D` | `#FFFB923C` |
| `Brush.Success` | `#FF16A34A` | `#FF4ADE80` |
| `Brush.SuccessSoft` | `#FFF1FAF4` | `#FF13251C` |
| `Brush.SuccessBorder` | `#FFC9E9D6` | `#FF254936` |
| `Brush.Scroll` | `#FFCFD4DE` | `#FF333A48` |
| `Brush.ScrollHover` | `#FFAAB2C0` | `#FF4B5566` |
| `Brush.Overlay` | `#F5FFFFFF` | `#F21A1F29` |
| `Brush.OverlayBorder` | `#FFE5E8EE` | `#FF2C3341` |

## カード

| キー | Light | Dark |
|---|---|---|
| `Node.Blocked.Fill` | `#FFFFFFFF` | `#FF182A40` |
| `Node.Blocked.Stroke` | `#FFDCE1E9` | `#FF2F3644` |
| `Node.Ready.Fill` | `#FFF4FCF7` | `#FF12382F` |
| `Node.Ready.Stroke` | `#FF22C55E` | `#FF34D399` |
| `Node.Progress.Fill` | `#FFF1F4FF` | `#FF1C2949` |
| `Node.Progress.Stroke` | `#FF4B6BFB` | `#FF7C93FF` |
| `Node.Done.Fill` | `#FFF6F7F9` | `#FF14171D` |
| `Node.Done.Stroke` | `#FFDBE0E7` | `#FF262C37` |
| `Node.Selected.Stroke` | `#FF087F73` | `#FF98ECD8` |
| `Node.Critical.Stroke` | `#FFF97316` | `#FFFB923C` |
| `Node.AtRisk` | `#FFEA580C` | `#FFFB923C` |
| `Node.Overdue` | `#FFDC2626` | `#FFF87171` |
| `Node.Text` | `#FF1B2030` | `#FFE7EAF2` |
| `Node.TextSubtle` | `#FF617887` | `#FFA3B4C9` |
| `Node.TextDone` | `#FF71828B` | `#FF6B7486` |
| `Node.Connector.Fill` | `#FFFFFFFF` | `#FF1C212B` |
| `Node.Repeat.Badge` | `#FFEFF1F6` | `#FF232B39` |
| `Node.Repeat.Text` | `#FF3D4B63` | `#FFCBD5E6` |
| `Node.Repeat.Loop` | `#FF6B7A93` | `#FF93A2BC` |

## 種別の帯

| キー | Light | Dark |
|---|---|---|
| `Kind.Start` | `#FF8B5CF6` | `#FFA78BFA` |
| `Kind.Step` | `#FFCBD5E1` | `#FF3E4657` |
| `Kind.Milestone` | `#FF0EA5E9` | `#FF38BDF8` |
| `Kind.Goal` | `#FFF59E0B` | `#FFFBBF24` |

## 線・選択

| キー | Light | Dark |
|---|---|---|
| `Edge.Normal` | `#FFB9C0CE` | `#FF3C4457` |
| `Edge.Settled` | `#FFDCE0E8` | `#FF272D3A` |
| `Edge.Highlight` | `#FF087F73` | `#FF66DFC3` |
| `Edge.Critical` | `#FFF97316` | `#FFFB923C` |
| `Edge.Selected` | `#FF087F73` | `#FF98ECD8` |
| `Edge.Preview` | `#FF4B6BFB` | `#FF7C93FF` |
| `Marquee.Fill` | `#1F4B6BFB` | `#2E7C93FF` |
| `Marquee.Stroke` | `#FF4B6BFB` | `#FF7C93FF` |

## ブロック

| キー | Light | Dark |
|---|---|---|
| `Block.Fill` | `#0F5B7488` | `#14A3B4C9` |
| `Block.Selected.Fill` | `#1A087F73` | `#2298ECD8` |
| `Block.Stroke` | `#FFC3CCD8` | `#FF3A4454` |
| `Block.Selected.Stroke` | `#FF087F73` | `#FF98ECD8` |
| `Block.Header.Fill` | `#FFEDF1F5` | `#FF232A36` |
| `Block.Header.Selected.Fill` | `#FFD9EFEA` | `#FF1E3B3A` |
| `Block.Header.Text` | `#FF334155` | `#FFDCE3EE` |
| `Block.Header.Count` | `#FF7A8A99` | `#FF95A5B8` |
| `Block.Member.Stroke` | `#66087F73` | `#8098ECD8` |

## 色プリセット9色（ブロック・線）

| キー | Light | Dark |
|---|---|---|
| `Block.Fill.slate` | `#1494A3B8` | `#1E94A3B8` |
| `Block.Stroke.slate` | `#FF94A3B8` | `#FF64748B` |
| `Block.Header.Fill.slate` | `#FFE4EAF1` | `#FF2A3444` |
| `Edge.Normal.slate` | `#FF94A3B8` | `#FF64748B` |
| `Block.Fill.red` | `#14DC2626` | `#1EF87171` |
| `Block.Stroke.red` | `#FFDC2626` | `#FFF87171` |
| `Block.Header.Fill.red` | `#FFFBE9E9` | `#FF3A2028` |
| `Edge.Normal.red` | `#FFDC2626` | `#FFEF6B6B` |
| `Block.Fill.orange` | `#14F97316` | `#1EFB923C` |
| `Block.Stroke.orange` | `#FFF97316` | `#FFFB923C` |
| `Block.Header.Fill.orange` | `#FFFDEEE1` | `#FF36261B` |
| `Edge.Normal.orange` | `#FFEA580C` | `#FFF59A4B` |
| `Block.Fill.amber` | `#14D97706` | `#1EFBBF24` |
| `Block.Stroke.amber` | `#FFD97706` | `#FFFBBF24` |
| `Block.Header.Fill.amber` | `#FFFBF1DC` | `#FF332B18` |
| `Edge.Normal.amber` | `#FFD97706` | `#FFEBB43C` |
| `Block.Fill.green` | `#1422C55E` | `#1E4ADE80` |
| `Block.Stroke.green` | `#FF22C55E` | `#FF4ADE80` |
| `Block.Header.Fill.green` | `#FFE8F7EE` | `#FF1A3328` |
| `Edge.Normal.green` | `#FF16A34A` | `#FF4ADE80` |
| `Block.Fill.teal` | `#140D9488` | `#1E2DD4BF` |
| `Block.Stroke.teal` | `#FF0D9488` | `#FF2DD4BF` |
| `Block.Header.Fill.teal` | `#FFE2F3F0` | `#FF15332F` |
| `Edge.Normal.teal` | `#FF0D9488` | `#FF2DD4BF` |
| `Block.Fill.blue` | `#143B82F6` | `#1E60A5FA` |
| `Block.Stroke.blue` | `#FF3B82F6` | `#FF60A5FA` |
| `Block.Header.Fill.blue` | `#FFE7EFFD` | `#FF1B2A45` |
| `Edge.Normal.blue` | `#FF2563EB` | `#FF6E9BF7` |
| `Block.Fill.violet` | `#148B5CF6` | `#1EA78BFA` |
| `Block.Stroke.violet` | `#FF8B5CF6` | `#FFA78BFA` |
| `Block.Header.Fill.violet` | `#FFF0EAFE` | `#FF272348` |
| `Edge.Normal.violet` | `#FF7C3AED` | `#FF9B87F5` |
| `Block.Fill.pink` | `#14EC4899` | `#1EF472B6` |
| `Block.Stroke.pink` | `#FFEC4899` | `#FFF472B6` |
| `Block.Header.Fill.pink` | `#FFFCE8F1` | `#FF3A2036` |
| `Edge.Normal.pink` | `#FFDB2777` | `#FFEE72B0` |

## ミニマップ・ガイド

| キー | Light | Dark |
|---|---|---|
| `MiniMap.Surface` | `#FFF7F8FA` | `#FF14181F` |
| `MiniMap.Viewport.Fill` | `#144B6BFB` | `#1F7C93FF` |
| `MiniMap.Viewport.Stroke` | `#FF4B6BFB` | `#FF7C93FF` |
| `MiniMap.Block.Stroke` | `#FFB6C2CF` | `#FF48546A` |
| `AlignmentGuideBrush` | `#2563EB` | `#93C5FD` |

## 補足

- 未知の色IDを読み込んだら文字列を保持したまま既定色で描く。
- `AlignmentGuideBrush`だけは`#RRGGBB`で不透明。ガイド線は破線1pxで描く。
- ブロックの塗りはアルファが小さい（`#0F`〜`#2E`）。不透明で描くと下の線が見えなくなる。
