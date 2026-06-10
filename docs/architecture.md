# 正式实现架构草案

本文档用于指导正式项目的代码结构。目标不是照搬 `~/sts2/decompiled`，而是吸收其中适合本项目的工程边界：规则核心独立、动作队列串行结算、卡牌/遗物/状态通过钩子接入、数据定义和运行时实例分离。

当前 `godot_demo/` 已经验证了一个正确方向：`Game.gd` 基本无 UI 依赖，`Main.gd` 负责渲染和输入，`Engine.gd` 可用固定 seed 做无头复现。正式实现应保留这些优点，并把 `Game.gd` 里逐渐膨胀的规则拆成稳定模块。

## 架构目标

- 规则层不依赖 UI、动画、节点树。
- 任意一局都能由 `seed + 初始数据版本 + 玩家动作序列` 复现。
- 玩家输入只生成意图或动作，不直接改战局状态。
- 所有战斗结算由动作队列串行推进，避免嵌套触发顺序失控。
- 卡牌、遗物、状态、地表、怪物能力通过统一钩子参与结算。
- CSV 和 Markdown 仍是设计源头；代码实现服务于设计数据，而不是反过来。
- UI 从只读快照和事件流渲染，不读取或修改内部可变对象。

## 从 STS2 学到的边界

### Core 和 Nodes 分离

STS2 的大结构里，`Core.*` 负责规则、实体、动作、存档、随机和数据模型，`Nodes.*` 负责 Godot 节点、界面、动画和音效。正式项目也应该保持这条线：

- 核心层可以在无头环境运行。
- UI 层只把点击、拖拽、快捷键转换成命令。
- 动画可以订阅核心层产生的事件，但不能决定规则结果。

### 动作队列

STS2 的 `GameAction` 和 `ActionExecutor` 表达了一个关键原则：玩家选择、结束回合、打牌、拾取奖励等都进入队列，由执行器按顺序结算。

本项目建议采用同样思想，但先做单机简化版：

```text
玩家输入
  -> Intent/Action
  -> ActionQueue.enqueue(action)
  -> ActionQueue.resolve_all()
  -> Action 内部调用 Effect/System
  -> 产生 GameEvent
  -> UI 播放事件并刷新快照
```

这样可以明确处理复杂连锁。例如：落雷造成伤害 -> 水地表转带电 -> 湿润目标受到双倍雷电 -> 麻痹满值眩晕 -> 遗物触发加护甲 -> 死亡触发亡语。

### 钩子系统

STS2 的 `Hook` 会遍历当前战斗里的监听者：遗物、卡牌、能力、状态、怪物、全局 modifier 等。它避免把所有特殊规则都写进中央战斗函数。

正式项目不需要一开始做上百个钩子。先定义少量稳定时机即可：

```text
before_battle_start
after_battle_start
before_unit_deploy
after_unit_deploy
before_command_use
after_command_use
modify_damage
before_damage
after_damage
before_status_apply
after_status_apply
before_unit_death
after_unit_death
before_monster_advance
after_monster_advance
before_turn_end
after_turn_end
before_wave_clear
after_wave_clear
```

监听者来源：

- 场上己方单位
- 场上怪物
- 单位和怪物身上的状态
- 格子上的地表
- 已拥有遗物
- 全局规则修正器

监听顺序必须稳定，建议使用：

```text
priority 升序 -> 入场顺序/实例 id 升序
```

不要依赖数组当前遍历顺序作为规则承诺。

### 定义和实例分离

STS2 的模型有 canonical 定义和 mutable 实例之分。这个思路非常适合本项目：

- `CardDef`：卡牌静态定义，来自 CSV。
- `RelicDef`：遗物静态定义，来自 CSV。
- `MonsterDef`：怪物静态定义，来自 CSV。
- `UnitInstance`：战局中的单位，有唯一 id、当前战力、护甲、状态、位置。
- `MonsterInstance`：战局中的怪物，有唯一 id、当前血量、护甲、意图、位置。
- `TerrainInstance`：格子上的地表状态，如水、带电、血水。
- `RelicInstance`：已获得遗物，保存计数、冷却、是否已触发等运行时字段。

所有动作和事件都引用实例 id，不直接引用数组下标。数组下标可以变，实例 id 不应变。

### 效果命令

STS2 的 `AttackCommand`、`DamageCmd`、`PowerCmd` 把常见规则结算封装成可组合命令。本项目也需要自己的效果原语：

```text
DamageEffect
HealEffect
GainArmorEffect
LoseArmorEffect
ApplyStatusEffect
RemoveStatusEffect
SpawnTerrainEffect
ClearTerrainEffect
MoveEntityEffect
PushEntityEffect
KillEntityEffect
SummonUnitEffect
SpawnMonsterEffect
GainGoldEffect
DrawOrOfferCardEffect
```

卡牌和遗物优先组合这些效果。只有高度特殊的能力才写专用脚本。

## 推荐目录结构

如果正式项目使用 Godot C#，建议把规则核心写成 C# 普通类，Godot 节点只做表现。若继续使用 GDScript，也按同样模块边界拆分。

```text
godot_game/
  project.godot
  scenes/
    Main.tscn
    combat/
    shop/
    common/
  scripts/
    ui/
      Main.gd
      BoardView.gd
      ShopView.gd
      HandView.gd
      EventPlayer.gd
    bridge/
      GameController.gd
      SnapshotMapper.gd

core/
  GameSession
  RunState
  BattleState
  Board
  ActionQueue
  actions/
  effects/
  hooks/
  systems/
  defs/
  instances/
  rng/
  save/
  tests/

data/
  card.csv
  relic.csv
  monster.csv
  attr.csv
  generated/
```

在当前仓库演进时，可以先不搬目录，只要代码边界按此切开。

## 核心模块职责

### GameSession

一局游戏的总入口。持有 `RunState`、当前 `BattleState`、`ActionQueue`、随机流和事件日志。

负责：

- 新开一局。
- 进入整备回合。
- 开始小局。
- 接收玩家动作。
- 产出只读快照。
- 存档和读档。

不负责：

- UI 布局。
- 动画时长。
- 鼠标拖拽细节。

### RunState

跨小局保留的运行状态。

包含：

- 金币。
- 波次。
- 城市完整度或领袖生命。
- 拥有卡牌。
- 备战席。
- 已部署单位的长期信息。
- 已拥有遗物。
- 商店报价。
- 随机流状态。

### BattleState

单场小局的战斗状态。

包含：

- 棋盘。
- 当前回合。
- 当前阶段。
- 场上己方单位。
- 场上怪物。
- 地表。
- 本波生成计划。
- 当前待选择目标的意图。

### Board

棋盘和空间规则。

负责：

- 5 行 6 列坐标换算。
- 格子合法性。
- 阵营区域判断。
- 邻接关系。
- 同列前方/后方查询。
- 寻找生成格。
- 移动、推拉、占位检查。

棋盘应提供清晰 API，避免各系统到处手写 `idx / COLS` 和 `idx % COLS`。

### ActionQueue

串行结算动作。

核心接口：

```text
enqueue(action)
resolve_next()
resolve_all()
is_waiting_for_player_choice()
provide_choice(choice)
```

动作执行时可以继续 enqueue 新动作或效果，但必须通过队列进入统一顺序。

### HookBus

统一触发钩子。

核心接口：

```text
emit(event)
modify(event, value)
collect_listeners(state, event)
```

`modify_damage` 这类钩子应返回修改后的值和修改来源，方便日志和 UI 显示。

### EffectSystem

执行基础效果原语。所有会改变战局的公共操作应集中在这里，保证钩子、日志、死亡清理、事件发射一致。

例如伤害流程：

```text
DamageEffect
  -> Hook.modify_damage
  -> Hook.before_damage
  -> 护甲吸收
  -> 血量/战力变化
  -> 元素反应
  -> Hook.after_damage
  -> 检查死亡
```

不要让卡牌脚本直接扣血、直接删单位。

### EventLog

核心层输出事件给 UI 和测试。

事件示例：

```text
unit_deployed
entity_moved
damage_dealt
armor_changed
status_applied
terrain_changed
entity_died
gold_changed
wave_started
wave_cleared
choice_requested
log_message
```

UI 可以按事件播放动画；测试可以断言事件序列或最终快照。

## 数据流

### 设计数据

`csvs/card.csv`、`csvs/relic.csv`、`csvs/monster.csv`、`csvs/attr.csv` 是人工编辑源头。不要在导入流程里重排或规范化这些 CSV 行。

建议生成中间数据：

```text
csvs/*.csv
  -> importer/validator
  -> data/generated/*.json 或 Godot Resource
  -> DefDatabase
```

正式实现不要长期维护 `CardDB.gd` 这种手写硬编码表。它适合 demo，不适合后续大量设计迭代。

### 卡牌定义建议

卡牌字段建议逐步稳定为：

```text
id
name
kind
cost
rarity
tags
base_power
base_armor
command_gate
target_rule
triggers
effects
script_id
text
```

`effects` 可以先用简单结构，例如：

```text
damage:lightning:1
terrain:water:2
armor:self:3
status:freeze:1
```

当表达力不够时，再为少数复杂卡使用 `script_id`。

### 运行时快照

UI 不应直接拿内部对象。核心层提供快照：

```text
GameSnapshot
  run
  battle
  board_cells
  bench
  shop
  relics
  available_actions
  pending_choice
  logs
```

快照是只读数据。UI 只能通过 `GameController.submit_action()` 回传动作。

## 随机与复现

随机必须分流，不要所有系统共享一个全局 `randi()`。

建议随机流：

```text
run_rng
shop_rng
wave_rng
combat_rng
effect_rng
ui_rng
```

`ui_rng` 只能用于视觉随机，不影响规则。规则随机必须进入存档和回放。

每次测试记录：

```text
seed
data_version
action_sequence
final_snapshot
important_events
```

当前 `Engine.gd` 的命令行驱动应保留，并逐步改成正式核心的回放入口。

## 存档

存档保存运行时状态，不保存 UI 状态。

需要保存：

- 数据版本。
- seed 和各随机流当前位置。
- RunState。
- BattleState。
- ActionQueue 中等待选择的动作。
- 已产生但未播放完不影响规则的事件可以不保存。

存档只引用定义 id，不复制完整卡牌文本。读档时通过当前数据版本加载定义。

## 动作设计

第一批动作建议：

```text
StartRunAction
BuyCardAction
BuyRelicAction
RefreshShopAction
DeployUnitAction
MoveUnitAction
DisbandUnitAction
StartBattleAction
UseUnitCommandAction
ChooseTargetAction
EndPlayerTurnAction
EnemyPhaseAction
SpawnMonsterAction
AdvanceMonsterAction
ResolveClashAction
ClearWaveAction
EndRunAction
```

动作结构建议：

```text
Action
  id
  owner
  type
  input_payload
  validate(state)
  execute(context)
```

`validate` 用于判断按钮是否可点、目标是否合法。`execute` 只在动作队列里调用。

## 战斗结算流程

小局开始：

```text
StartBattleAction
  -> 重置已部署单位到卡面基础值
  -> Hook.before_battle_start
  -> 部署能力结算
  -> 生成开局怪物
  -> Hook.after_battle_start
  -> 进入玩家回合
```

玩家使用指令：

```text
UseUnitCommandAction
  -> 校验阶段、单位、冷却/充能、目标
  -> Hook.before_command_use
  -> 消耗充能或设置冷却
  -> 执行效果
  -> Hook.after_command_use
  -> 清理死亡
```

结束回合：

```text
EndPlayerTurnAction
  -> Hook.before_turn_end
  -> 己方回合末能力
  -> 地表回合末效果
  -> 怪物阶段
  -> 地表和状态递减
  -> 生成下一只怪物
  -> Hook.after_turn_end
  -> 检查本波是否清空
```

怪物推进：

```text
AdvanceMonsterAction
  -> Hook.before_monster_advance
  -> 若下方为空则移动
  -> 若有己方单位则 ResolveClashAction
  -> 若越过底线则城市受损
  -> Hook.after_monster_advance
```

交锋：

```text
ResolveClashAction
  -> 单位和怪物互相造成战力伤害
  -> 壁垒、反击、失甲、受伤钩子
  -> 死亡和亡语
  -> 清理格子
```

## 元素和地表

元素反应应从一开始就通过效果系统实现，不要散落在卡牌脚本里。

建议基础结构：

```text
DamageType:
  physical
  lightning
  fire
  ice

TerrainType:
  water
  electric
  blood_water
  oil
  spike

StatusType:
  wet
  paralysis
  stun
  freeze
```

雷电伤害流程示例：

```text
DamageEffect(type=lightning)
  -> 若目标湿润，伤害倍率 x2
  -> 若目标所在格为水，水转带电 1
  -> 若目标所在格已带电且本次来源允许叠层，带电层数 +1
  -> 实际扣血/护甲
  -> 麻痹累计
  -> 麻痹满值转眩晕
```

这里要注意防止递归叠层。类似“带电地表自身造成的雷电伤害不增加层数”应成为 `DamageEffect` 的参数，例如：

```text
can_stack_electric_terrain = false
```

## UI 边界

UI 层负责：

- 渲染棋盘、备战席、商店、遗物、日志。
- 展示可用动作和可选目标。
- 播放核心事件对应的动画。
- 把点击、拖拽、键盘输入转换为动作请求。

UI 层不负责：

- 判断伤害数值。
- 修改单位血量。
- 生成怪物。
- 触发遗物。
- 决定死亡顺序。

如果动画需要等待，应该等待事件播放，而不是让规则层等待动画。

## 测试策略

最小测试层级：

1. `Board` 单元测试：坐标、邻接、路径、占位。
2. `EffectSystem` 测试：伤害、护甲、死亡、地表反应。
3. `HookBus` 测试：监听顺序、数值修改、触发次数。
4. `ActionQueue` 测试：动作串行、嵌套 enqueue、等待选择。
5. 场景回放测试：固定 seed + 动作列表 -> 最终快照。

当前命令行用例应继续保留，例如：

```bash
godot --headless --path godot_demo --script res://Engine.gd -- "new 123; view"
```

正式版应提供类似命令：

```bash
godot --headless --path game --script res://tools/Engine.gd -- "new 123; deploy unit_001 24; start; end"
```

## 第一阶段迁移计划

### 阶段 1：冻结原型行为

- 保留当前 `godot_demo/`。
- 为关键 seed 写几条命令行回放。
- 记录最终快照或关键日志，作为迁移对照。

### 阶段 2：抽出战局数据

- 从 `Game.gd` 抽出 `Board`。
- 抽出 `UnitInstance`、`MonsterInstance`、`TerrainInstance`。
- 所有单位和怪物获得稳定实例 id。
- UI 仍可沿用现有渲染。

### 阶段 3：引入动作队列

- `bench_pressed`、`cell_pressed`、`end_turn` 等输入方法只创建动作。
- 原有规则函数逐步移动到动作和系统里。
- `Engine.gd` 记录动作序列，而不是直接依赖内部函数。

### 阶段 4：引入效果系统

- `_apply_damage` 改成 `DamageEffect`。
- `_set_water`、`_electrify`、`_clear_terrain` 改成地表效果。
- 死亡、击杀金币、亡语统一走死亡流程。

### 阶段 5：引入钩子

- 先迁移遗物：收入、击杀金币、初始护甲。
- 再迁移单位被动：鬼灵、龙龟、矮人、避雷针等。
- 最后迁移地表和状态反应。

### 阶段 6：数据导入

- 从 CSV 生成 `CardDef`、`RelicDef`、`MonsterDef`。
- `CardDB.gd` 只保留 demo，正式版不再手写大型数据表。
- 导入器做校验，但不重排 CSV。

## 近期实现取舍

应该先做：

- 核心层无 UI 依赖。
- 动作队列。
- 可复现随机。
- 基础钩子。
- CSV 导入校验。
- 无头回放测试。

可以晚点做：

- 多人同步。
- 回放文件格式压缩。
- 大型 mod API。
- 热更新数据。
- 完整动画时间轴编辑器。
- 复杂 ECS 框架。

明确不建议：

- 把所有规则继续塞进一个巨大的 `Game.gd`。
- 每个遗物都在伤害、死亡、回合结束函数里加 `if has_relic(...)`。
- 让 UI 节点直接改核心状态。
- 长期维护两份卡牌数据：CSV 一份、代码硬编码一份。
- 使用全局随机导致 seed 复现失败。

## 推荐决策

正式项目建议选择：

```text
Godot 负责 UI、场景、动画、输入
规则核心使用普通类实现
核心状态完全可序列化
动作队列作为唯一状态修改入口
效果系统作为唯一公共结算入口
钩子系统承载卡牌、遗物、状态、地表的特殊规则
CSV/Markdown 作为设计源头
命令行回放作为回归测试基础
```

这套结构比当前 demo 重，但不会一步到位变成 STS2 那样庞大。它解决的是本项目后续一定会遇到的问题：元素反应多、地表触发多、遗物和单位被动多、固定 seed 复现和调平衡频繁。
