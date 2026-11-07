# 全自动制毒机 · DreamDustAutomation

《深空当铺 / Probably Stolen》的自动化学制造模组。当前源码版本：**0.3.62-beta**。

[Steam 创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3830572817)

把材料放进统一投料仓，选择药物、浓度与数量，机器根据本批材料和设备升级规划加工。内部使用原版搅拌器、加热板、蒸发器和成型机，按原版规则计算产量、损耗与消耗。

## 功能

0.3.62：跨天与白天加热均检查实际反应终点；可恢复旧版“尚未蒸干”导致的装药停机。已有高纯药料选择较低档位时，会尝试按需添加酸或碱，并在预览中显示用量及实际浓度。

- 七种原版药物；正常制药与“只做假药”两种模式。
- 浓度分档、数量选择、分批萃取，以及加热或蒸发路线。
- 读取材料成分、试剂、滤材、模块和机内余料，动态计算方案。
- 暂存药料、分次产出、进度与时间提示、余料清理。
- 投料、成品和垃圾分仓，自动整理与品质标签。
- 中文、英文界面。

## 编译

需要 Windows、PowerShell 7，以及本机安装的游戏正式版。源码不包含原版游戏 DLL。

将仓库放在游戏目录下的 `Modding/DreamDustAutomation`：

```text
Probably Stolen/
├─ Probably Stolen_Data/Managed/
└─ Modding/DreamDustAutomation/
```

在仓库目录执行：

```powershell
pwsh -NoProfile -File ./Build.ps1
```

产物位于 `dist/DreamDustAutomation/`。脚本使用 PowerShell 7 自带的 Roslyn 编译器，引用本机游戏程序集，并运行原版模组权限扫描。

退出游戏后，可编译并安装到本地 Mods 目录：

```powershell
pwsh -NoProfile -File ./Build.ps1 -Install
```

已有打包图片可直接用于编译；`Prepare-Assets.ps1` 用于从 `assets/` 重新生成图片。

## 测试

```powershell
# 核心化学与加工流程回归
pwsh -NoProfile -File ./Test-Chemistry.ps1

# 过夜与存档边界
pwsh -NoProfile -File ./Test-Chemistry.ps1 -Suite OvernightFeedbackTests
pwsh -NoProfile -File ./Test-Chemistry.ps1 -Suite SaveMutationTests

# 中英文提示校验
pwsh -NoProfile -File ./Test-Feedback.ps1
```

测试结果写入 `TestResults/`。化学测试使用本机原版数值逻辑，并在测试副本中替换本地化查询与进料斗界面颜色解析；不会修改游戏安装文件。请依次运行化学测试，避免同时写入共享测试目录。

`Test-Layout.ps1`、`Test-DragVisual.ps1`、`Test-Power.ps1`、`Test-Stock.ps1`、`Test-Starter.ps1` 还依赖开发者本地 `research/` 中的原版代码分析文件。这些文件未随仓库分发，因此完整 `Test-Comprehensive.ps1` 的 Interface/World 组需要额外的本地准备。Chemistry 组可使用本机游戏 DLL 运行。

这些是无界面测试；实际界面、帧率和其他模组兼容性仍需在游戏中验证。Open Beta 与正式版可能存在差异。

## 目录

| 路径 | 内容 |
| --- | --- |
| `src/` | 模组逻辑、规划、加工控制与界面 |
| `tests/` | 回归测试与测试替身 |
| `package/` | 清单、中英文文本、游戏内贴图与封面 |
| `assets/` | 图片源文件 |
| `Build.ps1` | 编译、扫描与可选安装 |
| `Test-*.ps1` | 测试入口 |

仓库仅包含模组源码及资源，不包含游戏本体、反编译原版源码、玩家存档、Steam 工具或上传凭据。

## 反馈

提交问题时请附游戏版本、模组版本、药物与浓度、材料和模块配置，以及复现步骤或报错截图。

## English

Automation mod for **Probably Stolen**. Load supplies together, choose a product, grade and quantity, then review and start the plan. The mod uses the original processing devices and vanilla chemistry rules. It supports seven products, batch extraction, deferred output, inventory sorting and Chinese/English UI.

Build on Windows with PowerShell 7 from `<game>/Modding/DreamDustAutomation` using `pwsh -NoProfile -File ./Build.ps1`. The installed game's assemblies are required and are not redistributed. Headless tests do not verify live rendering or frame rate. Some UI/world test scripts require developer-local analysis files excluded from this repository.
