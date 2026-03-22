# ComboHint 模组

这是为游戏制作的一个简单模组，用于在战斗中根据卡牌触发文字弹泡并显示高亮颜色。

本项目几乎完全使用VIBECODING，目前还在测试中，请谨慎使用。

- 功能概述：
  - 根据卡牌描述/名称匹配配置中的触发词组
  - 支持三类触发组，每组可在配置中设置不同的颜色
  - 在游戏中显示带颜色的弹泡文本，支持多条匹配并以“、”分隔

- 配置：
  - 编辑 `combo_hint.config.json`，可以修改三类触发词（`triggerTexts_...`）和对应的颜色（例如 `color_weakendefence`），颜色使用 `#RRGGBB` 格式。

- 开发与编译：
  - 源码位于 `mods/combo_hint/src`。
  - 使用 dotnet CLI 可编译项目（已配置为引用本地 `sts2.dll` 与 `GodotSharp.dll` 以便单独构建）。
