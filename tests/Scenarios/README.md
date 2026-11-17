# 游戏内场景回归

使用游戏 v0.113.1 自带的 `ScenarioRunner`，自动执行固定种子的战斗脚本。测试辅助模组仅放入工作区中的游戏副本，不属于 `dist/ComboHint` 发布包。

发行版 `NGame.GameStartup` 会在 `IsReleaseGame()` 为 true 时跳过 `--scenario` 和 AutoSlay 的自动入口，因此只传启动参数不会运行场景。辅助模组在隔离检查通过后，延迟调用公开的 `ScenarioRunner.StartFromCommandLine()`；没有修改游戏程序集，也没有把发行版伪装为开发版。

## 运行

需要 Windows、.NET 9 SDK、本机游戏和完整的 BaseLib 3.4.8。首次运行会在工作区上一级 `.research/scenario-runtime` 中复制游戏运行文件，约需 3 GB 空间。后续复用副本。请串行运行同一个测试目录。

```powershell
# 无窗口快速回归，默认英文
pwsh -File tests/Scenarios/Run-Scenarios.ps1

# 中文界面、实际渲染及截图
pwsh -File tests/Scenarios/Run-Scenarios.ps1 -Language zhs -Graphics

# 故意制造失败，确认运行器能够识别断言失败
pwsh -File tests/Scenarios/Run-Scenarios.ps1 -NegativeControl

# 通过公开模型与本地化接口导出现有原版卡牌 ID、基础/升级描述
pwsh -File tests/Scenarios/Run-Scenarios.ps1 -CardCatalog -Language eng
pwsh -File tests/Scenarios/Run-Scenarios.ps1 -CardCatalog -Language zhs
```

安装位置不同时传入 `-Sts2Path` 和 `-BaseLibPath`。可用 `-WorkingRoot` 指定专用测试目录，不能指向游戏安装目录。

子进程使用独立的 `APPDATA` / `LOCALAPPDATA`、`override.cfg` 和 `--force-steam=off`。启动游戏前先用无场景 autoload 的 MainLoop 探针确认 `user://` 的实际位置；辅助模组再次检查路径和 Steam 状态。测试使用新建的本地账号设置，不复制真实进度存档。超时只结束当前脚本创建的测试进程。

## 覆盖范围

- `combat-eng.scenario`：连续战斗、手牌增减、提示框刷新、合并气泡与防抖取消、斩杀去重、设置切换和旧控制器释放。中文模式复用同一脚本并转换本地化断言。
- `remaining-features`：十项效果设置的独立性、保存／读取／恢复默认、纯文本设置名称和悬停标题、气泡时长滑块及边界值。遍历全部多人牌的基础／升级实例，检查关闭后不回退成其他提示，开启后保留实际卡牌预览。
- 疯狂科学：遍历所有 Rider 与三种牌型的基础／升级实例，检查 Sapping 的虚弱和易伤，以及同 ID 不同变体的来源隔离。`mad-science <Rider>` 创建真实战斗卡牌并加入手牌。
- `CharacterEffectsTest`：中毒、灾厄来源牌的基础／升级模型，默认关闭、混合虚弱独立显示、原生说明、颜色和去重；不喜欢你的 50% / 75% 增幅。消费者和自身减益不作为敌方状态来源。
- `effect-catalog`：原生效果名、图标、描述、共享状态去重、多人卡牌预览、自制弱化／暴露说明、玩家作用域及升级数值，导出 `user://bubble-effects.json`。
- `hover-effect <overlay|bubble> <key> [card]`：向真实 Viewport 发送鼠标移动，经 RichTextLabel 命中测试打开原生 NHoverTipSet，检查效果与卡牌预览类型，以及移出、隐藏、刷新和销毁后的清理。`no-hover <overlay|bubble>` 检查斩杀不打开说明。
- `config-menu`、`config-expand`、`config-scroll`、`config-hover`、`config-hover-visible`、`config-close`：打开 BaseLib 实际子菜单、展开效果分组、滚动和悬停，并生成配置页截图。

测试专用 Harmony 后置补丁记录实际气泡的创建次数、文本、发言角色配色和当前时长。清空手牌使用真实 `CardPileCmd.RemoveFromCombat`；设置变更使用与配置页相同的通知入口。为完成鼠标悬停检查，指定测试气泡延长至 15 秒，正式模组仍使用正常设置。

`-CardCatalog` 通过 `ModelDb.AllCards`、`CardModel.Id`、`Description.GetRawText()` 和 `GetDescriptionForPile(PileType.None, null)` 导出卡牌 ID、标题及描述。基础／升级描述从独立的 `MutableClone()` 读取，升级使用 `UpgradeInternal()` 和 `FinalizeUpgradeInternal()`。产物位于 `catalog-<语言>-<模式>/roaming/ComboHintAutomation/card-catalog.json`；此模式不进入战斗或生成截图。人工审核数据保存在 `tests/Fixtures`，运行时不扫描描述关键词。

## 判定与输出

正常运行必须同时满足：退出码为 0、场景完成标记、全部断言完成标记，以及没有 ComboHint 回调或初始化错误。图形模式还要求本次生成截图；无窗口模式不把截图命令成功当作已截图。负对照必须返回非零退出码和预期断言失败。

`result.json`、`scenario.log`、`engine.log` 和截图保存在工作目录下的 `combat-<语言>-<模式>`。截图需要人工或图像工具复核，文本断言不能替代布局检查。

场景覆盖手牌识别与 UI，不逐张打出全部卡牌或建立多人联机。图形模式使用 OpenGL Compatibility；其他渲染后端、分辨率、控制器、配置菜单鼠标点击和其他模组组合需另行检查。
