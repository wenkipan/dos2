class_name Game
extends RefCounted
# =============================================================================
# 规则与状态层 —— 自走棋式塔防(core.md 改版)。无任何 UI 依赖:视图(Main.gd)
# 持有一个 Game 实例,通过高层意图方法驱动,再读取公共状态渲染。
#   整备意图:bench_pressed / cell_pressed(部署/调位/解散) / disband_toggle /
#             shop_buy / buy_relic / start_battle
#   战斗意图:cell_pressed(使用指令 + 指令定位) / end_turn
#
# 棋盘 5 行 * 6 列。上 3 行(row 0,1,2)是怪物区,怪物每个敌方回合向下推进一行。
# 下 2 行(row 3,4)是玩家区。漏怪(越过 row 4)= 城市完整度 -1,归零即败。
#
# 典型循环(core.md):整备回合 → 小局 → 整备回合 → 小局 …… 抵御 N 波即胜。
#   · 整备回合(PREP):购买卡牌(进备战席)/ 购买遗物 / 解散单位 / 上场与调位。
#   · 小局(BATTLE):玩家只能使用已上场单位的「充能/冷却」指令;不能再部署。
#     每个敌方阶段怪物推进一行,挡路则交锋;清空本波怪物即小局结束,棋子保留。
#   · 每场战斗开始:已上场单位重置回卡面基础数值(满血、清增益/状态、充能复位),
#     并触发各自的部署能力(冻结自身 / 产水 / 召唤前突 等)。
#
# 战斗 / 元素机制(与改版前一致,纯单位驱动元素反应):
#   控制:麻痹、晕眩、冻结;导电(站在水上受双倍雷电);带电地表每回合 1 点雷电。
#   触发:亡语、反击(壁垒)、受伤/失甲/致死/死亡 钩子。
#   指令:充能(弓箭手/水电法师/祈雨祭祀/石像/超导体)、冷却(法师/海绵/充电桩/魔狼)。
# =============================================================================

enum Phase { PREP, BATTLE }

const ROWS := 5
const COLS := 6
const PLAYER_ROWS := [3, 4]   # 玩家可部署的行
const SPAWN_ROW := 0          # 怪物生成行
const CITY_HP_MAX := 10
const WIN_WAVE := 20          # 抵御 N 波即胜
const PARALYSIS_THRESHOLD := 3  # 麻痹值累计到此 → 眩晕一回合并清零

const FROST_GUARD_ARMOR := 4   # 霜甲卫士:每有一个单位被冻结 +护甲
const STATUE_ARMOR := 3        # 石像充能:获得护甲

# 经济 / 商店
const GOLD_START := 6
const GOLD_PER_KILL := 1
const PREP_INCOME := 4         # 每个整备回合的基础收入
const SHOP_SIZE := 5           # 整备回合的卡牌报价数
const CARD_PRICE := 3
const SHOP_REFRESH_COST := 1   # 刷新商店卡牌报价的费用

# --- 运行时状态 ---
var name_to_di: Dictionary = {}   # 卡名 -> CardDB.CARD_DEFS 下标
var unit_pool: Array = []         # CARD_DEFS 中所有「单位」下标(商店/起始抽取池)
var cells: Array = []             # 每格: {unit, monster}
var terrain: Array = []           # 每格: {water:int, electric:int} 剩余回合

var phase: int = Phase.PREP
var bench: Array = []             # 备战席:拥有但未上场的单位 di
var relics: Array = []            # 已购遗物 id
var aim: Dictionary = {}          # 指令定位:{kind, need, picks, src}

var gold := GOLD_START
var city_hp := CITY_HP_MAX
var wave := 1
var battle_turn := 1
var spawn_quota := 0              # 本波还需生成的怪物数
var spawn_plan: Array = []        # 完全信息:本波生成计划 [{col,power}],顺序消耗(前2为开局)

var selected_bench := -1          # 选中的备战席单位 di(-1=无)
var selected_cell := -1          # 选中的已上场单位的格 idx(用于调位;-1=无)
var disband_mode := false

var shop_offers: Array = []       # 整备回合的卡牌报价 [{di, sold}]
var relic_offers: Array = []      # 整备回合的遗物报价 [{id, sold}]

var game_over := false
var log_lines: Array = []


# 由视图在 _ready 中调用一次:建表、铺棋盘、发起始备战席、进入第 1 个整备回合。
func setup() -> void:
	for i in CardDB.CARD_DEFS.size():
		name_to_di[CardDB.CARD_DEFS[i].name] = i
		if CardDB.CARD_DEFS[i].type == "unit":
			unit_pool.append(i)
	_init_state()
	for n in CardDB.STARTER_NAMES:
		bench.append(name_to_di[n])
	_log("自走棋塔防:整备回合构筑棋盘,小局只能用已上场单位的指令。抵御 %d 波即胜。" % WIN_WAVE)
	_log("整备:点备战席单位→点己方下两行格子上场;点已上场单位可调位;「解散」收回备战席。")
	_enter_prep(false)


func _init_state() -> void:
	cells.clear()
	terrain.clear()
	for i in ROWS * COLS:
		cells.append({"unit": null, "monster": null})
		terrain.append({"water": 0, "electric": 0})


func busy() -> bool:
	return not aim.is_empty()


func _has_relic(id: String) -> bool:
	return id in relics


# -----------------------------------------------------------------------------
# 整备回合(PREP)
# -----------------------------------------------------------------------------
func _enter_prep(give_income: bool) -> void:
	phase = Phase.PREP
	aim = {}
	selected_bench = -1
	selected_cell = -1
	disband_mode = false
	if give_income:
		var inc := PREP_INCOME + (2 if _has_relic("horn") else 0)
		gold += inc
		_log("【整备 · 第 %d 波前】收入 +%d 金(现 %d)。" % [wave, inc, gold])
	_reset_units_to_base()
	_new_shop()
	_roll_spawn_plan()   # 完全信息:整备时就生成本波的怪物生成计划,供玩家全知规划


func _new_shop() -> void:
	_roll_card_offers()
	relic_offers.clear()
	for r in CardDB.RELIC_DEFS:
		relic_offers.append({"id": r.id, "sold": _has_relic(r.id)})


func _roll_card_offers() -> void:
	shop_offers.clear()
	for i in SHOP_SIZE:
		shop_offers.append({"di": unit_pool[randi() % unit_pool.size()], "sold": false})


func refresh_shop() -> void:
	if game_over or phase != Phase.PREP or busy():
		return
	if gold < SHOP_REFRESH_COST:
		_log("金币不足,无法刷新商店(需 %d 金)。" % SHOP_REFRESH_COST)
		return
	gold -= SHOP_REFRESH_COST
	_roll_card_offers()
	_log("刷新商店,-%d 金(现 %d)。" % [SHOP_REFRESH_COST, gold])


# 每场战斗/整备:已上场单位重置回卡面基础(满血、清增益/状态、充能复位)。
func _reset_units_to_base() -> void:
	var bonus := 1 if _has_relic("totem") else 0
	for i in cells.size():
		var u = cells[i].unit
		if u == null:
			continue
		var def: Dictionary = CardDB.CARD_DEFS[u.def_index]
		u.power = int(def.power)
		u.hp = int(def.power)
		u.max_hp = int(def.power)
		u.armor = int(def.armor) + bonus
		u.charge = int(def.get("charge", 0))
		u.cd = 0
		u.stun = 0
		u.freeze = 0
		u.paralysis = 0


func bench_pressed(di: int) -> void:
	if game_over or phase != Phase.PREP or busy():
		return
	disband_mode = false
	selected_cell = -1
	selected_bench = -1 if selected_bench == di else di


# 整备回合点格:部署 / 调位 / 解散
func _prep_cell(idx: int) -> void:
	# 解散:点已上场单位 → 收回备战席
	if disband_mode:
		var u = cells[idx].unit
		if u == null:
			_log("该格没有己方单位可解散。")
			return
		bench.append(u.def_index)
		cells[idx].unit = null
		_log("解散 %s,收回备战席。" % u.name)
		disband_mode = false
		return
	# 从备战席上场
	if selected_bench >= 0:
		if _place_from_bench(selected_bench, idx):
			bench.erase(selected_bench)
			selected_bench = -1
		return
	# 调位:已选中一个已上场单位 → 移动到空格
	if selected_cell >= 0:
		if idx == selected_cell:
			selected_cell = -1
			return
		_move_unit(selected_cell, idx)
		return
	# 否则:点己方单位 → 选中以调位
	if cells[idx].unit != null:
		selected_cell = idx
		_log("已选中 %s,点一个空的己方格子移动它;再点它取消。" % cells[idx].unit.name)


func _place_from_bench(di: int, idx: int) -> bool:
	var def: Dictionary = CardDB.CARD_DEFS[di]
	var r := idx / COLS
	var anywhere: bool = def.get("anywhere", false)
	if not anywhere and not (r in PLAYER_ROWS):
		_log("单位只能上场到己方下两行。")
		return false
	if cells[idx].unit != null or cells[idx].monster != null:
		_log("该格已被占据。")
		return false
	cells[idx].unit = _make_unit(def, di)
	_log("上场 %s(战 %d / 甲 %d)。" % [def.name, def.power, def.get("armor", 0)])
	return true


func _move_unit(src: int, dst: int) -> void:
	var r := dst / COLS
	var u = cells[src].unit
	if u == null:
		selected_cell = -1
		return
	var anywhere: bool = CardDB.CARD_DEFS[u.def_index].get("anywhere", false)
	if not anywhere and not (r in PLAYER_ROWS):
		_log("只能移动到己方下两行。")
		return
	if cells[dst].unit != null or cells[dst].monster != null:
		_log("目标格已被占据。")
		return
	cells[dst].unit = u
	cells[src].unit = null
	selected_cell = -1
	_log("移动 %s。" % u.name)


func disband_toggle() -> void:
	if game_over or phase != Phase.PREP or busy():
		return
	disband_mode = not disband_mode
	if disband_mode:
		selected_bench = -1
		selected_cell = -1
		_log("解散模式:点一个已上场单位收回备战席;再点「解散」取消。")


# -----------------------------------------------------------------------------
# 商店(整备回合内)
# -----------------------------------------------------------------------------
func shop_buy(i: int) -> void:
	if game_over or phase != Phase.PREP or i < 0 or i >= shop_offers.size():
		return
	var offer: Dictionary = shop_offers[i]
	if offer.sold:
		return
	if gold < CARD_PRICE:
		_log("金币不足,买不起 %s。" % CardDB.CARD_DEFS[offer.di].name)
		return
	gold -= CARD_PRICE
	bench.append(offer.di)
	offer.sold = true
	_log("购买 %s 进备战席(余 %d 金)。" % [CardDB.CARD_DEFS[offer.di].name, gold])


func buy_relic(i: int) -> void:
	if game_over or phase != Phase.PREP or i < 0 or i >= relic_offers.size():
		return
	var offer: Dictionary = relic_offers[i]
	if offer.sold:
		return
	var def: Dictionary = CardDB.RELIC_DEFS[i]
	if gold < int(def.price):
		_log("金币不足,买不起遗物 %s。" % def.name)
		return
	gold -= int(def.price)
	relics.append(def.id)
	offer.sold = true
	_log("购得遗物 %s:%s(余 %d 金)。" % [def.name, def.desc, gold])


# -----------------------------------------------------------------------------
# 开始小局 / 战斗回合
# -----------------------------------------------------------------------------
func start_battle() -> void:
	if game_over or phase != Phase.PREP or busy():
		return
	if _count_units() == 0:
		_log("棋盘上还没有单位,先上场再开战。")
		return
	phase = Phase.BATTLE
	battle_turn = 1
	selected_bench = -1
	selected_cell = -1
	disband_mode = false
	_reset_units_to_base()
	_fire_deploy_abilities()        # 战斗开始:触发各单位部署能力
	if spawn_plan.is_empty():
		_roll_spawn_plan()          # 兜底:若计划为空则补生成
	spawn_quota = spawn_plan.size() # 本波怪物总量(= 完全信息生成计划长度)
	var wave_total := spawn_quota
	_spawn_next()                   # 开局先放 2 只(按计划)
	_spawn_next()
	_log("=== 第 %d 波 开战(怪物 %d 只,点数约 %d)===" % [wave, wave_total, _monster_power()])
	_begin_battle_turn()


func _begin_battle_turn() -> void:
	aim = {}
	selected_cell = -1
	# 冷却 -1
	for i in cells.size():
		var u = cells[i].unit
		if u != null and int(u.get("cd", 0)) > 0:
			u.cd -= 1


func end_turn() -> void:
	if game_over or phase != Phase.BATTLE:
		return
	if busy():
		_log("请先完成上方的「指令定位」。")
		return
	_end_of_turn_triggers()    # 回合末:拒马/避雷石像/海啸/引雷小鬼
	_enemy_phase()
	if city_hp <= 0:
		_end_game(false)
		return
	_tick_after_enemy()        # 带电伤害 + 状态/地表倒计时
	# 本波清空判定:怪物配额耗尽且场上无怪 → 小局结束
	if spawn_quota <= 0 and _count_monsters() == 0:
		_wave_cleared()
		return
	battle_turn += 1
	_log("—— 第 %d 波 · 回合 %d ——" % [wave, battle_turn])
	_begin_battle_turn()


func _wave_cleared() -> void:
	_log("✔ 第 %d 波抵御成功!棋子保留。" % wave)
	wave += 1
	if wave > WIN_WAVE:
		_end_game(true)
		return
	_enter_prep(true)


# -----------------------------------------------------------------------------
# 玩家点格(按阶段分派)
# -----------------------------------------------------------------------------
func cell_pressed(idx: int) -> void:
	if game_over:
		return
	if phase == Phase.PREP:
		_prep_cell(idx)
	else:
		_battle_cell(idx)


func _battle_cell(idx: int) -> void:
	# 指令定位中:本次点击作为目标
	if not aim.is_empty():
		if String(aim.kind).begins_with("cmd:") and idx == int(aim.get("src", -1)):
			aim = {}
			_log("已取消指令。")
			return
		aim.picks.append(idx)
		_advance_aim()
		return
	# 使用该格己方单位的指令
	if cells[idx].unit != null:
		_try_use_command(idx)


# -----------------------------------------------------------------------------
# 部署能力(战斗开始触发)
# -----------------------------------------------------------------------------
func _fire_deploy_abilities() -> void:
	for i in cells.size():
		var u = cells[i].unit
		if u != null:
			_on_deploy(u, i)


func _make_unit(def: Dictionary, di: int) -> Dictionary:
	var bonus := 1 if _has_relic("totem") else 0
	return {
		"name": def.name, "power": int(def.power), "hp": int(def.power), "max_hp": int(def.power),
		"armor": int(def.armor) + bonus, "ability": def.get("ability", ""), "def_index": di,
		"charge": int(def.get("charge", 0)), "cd": 0, "cooldown": int(def.get("cooldown", 0)),
		"mech": def.get("mech", false), "side": "ally",
		"stun": 0, "freeze": 0, "paralysis": 0,
	}


func _on_deploy(u: Dictionary, idx: int) -> void:
	match u.ability:
		"dragoon":
			_dragoon_strike(idx)
		"ronin":
			if _has_adjacent_unit(idx):
				_log("浪人:周围有友军,自毁。")
				_kill_unit_at(idx)
		"volunteer":
			var n := _count_units() - 1
			if n > 0:
				_buff_unit(u, n)
				_log("志愿军:%d 个友军,获得 %d 点增益。" % [n, n])
		"frost_guard":
			_apply_freeze(u)
			_log("霜甲卫士:冻结自身。")
		"tide_guard":
			var c := idx % COLS
			var r := idx / COLS
			for rr in range(0, r):
				_set_water(rr * COLS + c, 3)
			_log("潮汐卫兵:本列前方产水 3 回合。")


func _dragoon_strike(idx: int) -> void:
	var r := idx / COLS
	var c := idx % COLS
	if r == 0:
		return
	var t := (r - 1) * COLS + c
	if cells[t].monster != null:
		_kill_monster_at(t)
		_log("龙骑兵:摧毁了前方的怪物。")
	elif cells[t].unit != null:
		_kill_unit_at(t)
		_log("龙骑兵:摧毁了前方的单位。")


# -----------------------------------------------------------------------------
# 主动指令(充能 / 冷却)
# -----------------------------------------------------------------------------
func _cmd_meta(u: Dictionary) -> Dictionary:
	return CardDB.COMMANDS.get(u.ability, {})


func cmd_ready(u: Dictionary) -> bool:
	var m := _cmd_meta(u)
	if m.is_empty():
		return false
	if int(u.get("stun", 0)) > 0 or int(u.get("freeze", 0)) > 0:
		return false
	if m.gate == "charge":
		return int(u.get("charge", 0)) > 0
	return int(u.get("cd", 0)) <= 0


func _try_use_command(src: int) -> void:
	var u = cells[src].unit
	if u == null:
		return
	var m := _cmd_meta(u)
	if m.is_empty():
		_log("%s 没有可用指令。" % u.name)
		return
	if not cmd_ready(u):
		_log("%s 的指令不可用(无充能 / 冷却中 / 被控)。" % u.name)
		return
	# 充电桩:处于带电地表 → 群体充能,无需目标
	if u.ability == "charge_station" and _cell_electric(src):
		_exec_command(u, src, -1)
		_cleanup_dead()
		return
	if m.target == "self":
		_exec_command(u, src, -1)
		_cleanup_dead()
		return
	aim = {"kind": "cmd:" + u.ability, "need": 1, "picks": [], "src": src}
	_log("%s:请点一个目标格。" % u.name)


func _advance_aim() -> void:
	if aim.is_empty():
		return
	if aim.picks.size() >= int(aim.need):
		var a := aim.duplicate(true)
		aim = {}
		_resolve_aim(a)


func _resolve_aim(a: Dictionary) -> void:
	if not String(a.kind).begins_with("cmd:"):
		return
	var src: int = a.src
	var u = cells[src].unit
	if u == null:
		return
	if not _exec_command(u, src, a.picks[0]):
		_log("指令目标无效。")
	_cleanup_dead()


func _consume_cmd(u: Dictionary) -> void:
	var m := _cmd_meta(u)
	if m.gate == "charge":
		u.charge -= 1
	else:
		u.cd = int(u.get("cooldown", 0))


func _exec_command(u: Dictionary, src: int, target: int) -> bool:
	var ok := false
	match u.ability:
		"archer":
			if target >= 0 and cells[target].monster != null:
				_apply_damage(cells[target].monster, target, 1, "physical")
				_log("弓箭手射击,造成 1 点伤害。")
				ok = true
		"storm_mage":
			if target >= 0 and cells[target].monster != null:
				_lightning_hit(cells[target].monster, target, 1)
				_log("水电法师:1 点雷电伤害。")
				ok = true
		"rain_priest":
			if target >= 0:
				_set_water(target, 2)
				_log("祈雨祭祀:目标格附水 2 回合。")
				ok = true
		"statue":
			u.armor += STATUE_ARMOR
			_log("石像:获得 %d 点护甲。" % STATUE_ARMOR)
			ok = true
		"superconductor":
			var n := 0
			for i in cells.size():
				if terrain[i].electric > 0 and cells[i].monster != null:
					_add_paralysis(cells[i].monster, i, 1)
					n += 1
			_log("超导体:%d 个带电地表上的敌人 +1 麻痹。" % n)
			ok = true
		"mage":
			if target >= 0 and cells[target].unit != null:
				cells[target].unit.charge = int(cells[target].unit.get("charge", 0)) + 1
				_log("法师:%s 充能 +1。" % cells[target].unit.name)
				ok = true
		"sponge":
			if target >= 0:
				if terrain[target].water > 0 or terrain[target].electric > 0:
					_clear_terrain(target)
					_buff_unit(u, 1)
					_log("海绵:移除一格地表,获得 1 增益。")
				else:
					_log("海绵:该格无地表。")
				ok = true
		"charge_station":
			if target < 0:
				var fed := 0
				for i in cells.size():
					if cells[i].unit != null:
						cells[i].unit.charge = int(cells[i].unit.get("charge", 0)) + 1
						fed += 1
				_log("充电桩(带电):群体充能 +1(%d 个单位)。" % fed)
				ok = true
			elif cells[target].unit != null:
				cells[target].unit.charge = int(cells[target].unit.get("charge", 0)) + 1
				_log("充电桩:%s 充能 +1。" % cells[target].unit.name)
				ok = true
		"wolf":
			if target >= 0 and target != src and cells[target].unit != null and _adjacent(src, target):
				var victim = cells[target].unit
				_buff_unit(u, victim.power)
				cells[target].unit = null
				_log("魔狼:吞噬 %s,获得 %d 点增益。" % [victim.name, victim.power])
				ok = true
	if ok:
		_consume_cmd(u)
	return ok


# -----------------------------------------------------------------------------
# 回合流程:回合末触发 / 敌方阶段 / 结算
# -----------------------------------------------------------------------------
func _end_of_turn_triggers() -> void:
	# 拒马:相邻友军 +1 甲(壁垒:需自身有护甲)
	var grants := {}
	for i in cells.size():
		var u = cells[i].unit
		if u == null or _controlled(u):
			continue
		if u.ability == "bulwark_armor" and u.armor > 0:
			var r := i / COLS
			var c := i % COLS
			for d in [[-1, 0], [1, 0], [0, -1], [0, 1]]:
				var nr: int = r + d[0]
				var nc: int = c + d[1]
				if nr >= 0 and nr < ROWS and nc >= 0 and nc < COLS:
					var t := nr * COLS + nc
					if cells[t].unit != null:
						grants[t] = grants.get(t, 0) + 1
	for t in grants:
		cells[t].unit.armor += grants[t]
	if not grants.is_empty():
		_log("壁垒(拒马):%d 个相邻单位获得护甲。" % grants.size())
	# 避雷石像:处于带电地表 → +3 甲(壁垒:需有护甲)
	for i in cells.size():
		var u = cells[i].unit
		if u != null and u.ability == "lightning_statue" and u.armor > 0 and terrain[i].electric > 0:
			u.armor += 3
			_log("避雷石像:带电地表,+3 护甲。")
	# 海啸:对水上敌人造 1 伤害
	if _has_active_ability("tsunami"):
		for i in cells.size():
			if terrain[i].water > 0 and cells[i].monster != null:
				_apply_damage(cells[i].monster, i, 1, "physical")
	# 引雷小鬼:对麻痹敌人造 1 伤害
	if _has_active_ability("shock_imp"):
		for i in cells.size():
			var m = cells[i].monster
			if m != null and int(m.get("paralysis", 0)) > 0:
				_apply_damage(m, i, 1, "physical")
	_cleanup_dead()


func _enemy_phase() -> void:
	for r in range(ROWS - 1, -1, -1):
		for c in COLS:
			_advance_monster(r * COLS + c)
	_cleanup_dead()
	# 本波增援:每回合按计划补 1 只,直到配额耗尽
	if spawn_quota > 0:
		_spawn_next()


# 带电地表伤害 + 状态/地表倒计时
func _tick_after_enemy() -> void:
	for i in cells.size():
		if terrain[i].electric > 0:
			if cells[i].monster != null:
				_lightning_hit(cells[i].monster, i, 1)
			if cells[i].unit != null:
				_lightning_hit(cells[i].unit, i, 1)
	_cleanup_dead()
	# 眩晕 -1
	for i in cells.size():
		for key in ["unit", "monster"]:
			var e = cells[i][key]
			if e != null and int(e.get("stun", 0)) > 0:
				e.stun -= 1
	# 地表倒计时
	for i in terrain.size():
		if terrain[i].water > 0:
			terrain[i].water -= 1
		if terrain[i].electric > 0:
			terrain[i].electric -= 1
	# 冻结 -1;解冻在原地生成水 1 回合
	for i in cells.size():
		for key in ["unit", "monster"]:
			var e = cells[i][key]
			if e != null and int(e.get("freeze", 0)) > 0:
				e.freeze -= 1
				if e.freeze == 0:
					_set_water(i, 1)


func _advance_monster(idx: int) -> void:
	var m = cells[idx].monster
	if m == null:
		return
	if int(m.get("stun", 0)) > 0 or int(m.get("freeze", 0)) > 0:
		return  # 被控:本回合不推进
	var r := idx / COLS
	var c := idx % COLS
	var tr := r + 1
	if tr >= ROWS:
		cells[idx].monster = null
		city_hp -= 1
		_log("⚠ 漏怪!城市完整度 -1(剩 %d)。" % city_hp)
		return
	var t := tr * COLS + c
	if cells[t].monster != null:
		return
	if cells[t].unit != null:
		_clash(t, idx)
		return
	cells[t].monster = m
	cells[idx].monster = null


# 交锋:每个敌方阶段一次「同时互砍」。双方各以战力攻击对方(护甲先于血量),
# 即使一方被这一击打死,它的攻击仍同时生效。双方都存活则怪被挡在原地,下回合再交锋。
func _clash(unit_idx: int, monster_idx: int) -> void:
	var u = cells[unit_idx].unit
	var m = cells[monster_idx].monster
	if u == null or m == null:
		return
	var up := int(u.power)
	var mp := int(m.power)
	_apply_damage(m, monster_idx, up, "physical")
	_apply_damage(u, unit_idx, mp, "physical")
	# 静电战士壁垒:对攻击者施加麻痹(需有护甲且存活)
	if u.ability == "static_warrior" and u.armor > 0 and u.hp > 0:
		_add_paralysis(m, monster_idx, 1)
	var unit_dead: bool = u.hp <= 0
	var monster_dead: bool = m.hp <= 0
	if monster_dead:
		_kill_monster_at(monster_idx)
		if not unit_dead:
			_log("%s 挡下了进攻。" % u.name)
	if unit_dead:
		var is_imp: bool = u.ability == "imp"
		_kill_unit_at(unit_idx)
		_log("%s 阵亡,防线被突破!" % u.name)
		if is_imp and not monster_dead:
			_kill_monster_at(monster_idx)
			_log("小鬼亡语:摧毁了攻击者。")
			monster_dead = true
		if not monster_dead:
			cells[unit_idx].monster = cells[monster_idx].monster
			cells[monster_idx].monster = null
	elif not monster_dead:
		_log("%s 与怪僵持(单位 hp%d / 怪 hp%d)。" % [u.name, u.hp, m.hp])


# -----------------------------------------------------------------------------
# 伤害 / 元素结算
# -----------------------------------------------------------------------------
func _apply_damage(e, idx: int, dmg: int, type: String) -> void:
	if type == "lightning":
		if idx >= 0 and _cell_water(idx):
			dmg *= 2
			terrain[idx].water = 0
			terrain[idx].electric = max(terrain[idx].electric, 2)
		if e.get("mech", false):
			return  # 电控机械:免疫雷电
	if dmg <= 0:
		return
	var raw := dmg
	if e.armor > 0:
		var absorbed: int = min(e.armor, dmg)
		e.armor -= absorbed
		dmg -= absorbed
		if e.get("ability", "") == "dwarf" and absorbed > 0:
			_buff_unit(e, absorbed)   # 矮人:失去护甲→等量增益
	e.hp -= dmg
	if e.get("ability", "") == "dragon_turtle":
		e.armor += raw               # 龙龟:受伤→等量护甲
	if e.get("ability", "") == "crybaby":
		_spawn_water_random(2)       # 催泪人鱼:随机格产水
	if type == "lightning":
		_add_paralysis(e, idx, raw)  # 雷电累计麻痹


func _lightning_hit(e, idx: int, dmg: int) -> void:
	var alive: bool = e.hp > 0
	_apply_damage(e, idx, dmg, "lightning")
	if alive and e.hp <= 0:
		_charge_golem_gain()         # 充电魔像:雷电致死→增益


func _add_paralysis(e, _idx: int, amt: int) -> void:
	if amt <= 0:
		return
	var before: int = int(e.get("paralysis", 0))
	e.paralysis = before + amt
	if before == 0 and e.paralysis > 0 and e.get("side", "") == "enemy":
		_lightning_rod_gain()        # 避雷针:敌人陷入麻痹→友方+1甲
	if e.paralysis >= PARALYSIS_THRESHOLD:
		e.paralysis = 0
		e.stun = max(int(e.get("stun", 0)), 1)  # 麻痹满 → 眩晕一回合


func _apply_freeze(e) -> void:
	if int(e.get("freeze", 0)) <= 0:
		e.freeze = 1
		_frost_guard_gain()          # 每有单位被冻结 → 霜甲卫士 +甲
	else:
		e.freeze = max(int(e.freeze), 1)


# -----------------------------------------------------------------------------
# 全局触发辅助
# -----------------------------------------------------------------------------
func _charge_golem_gain() -> void:
	for i in cells.size():
		var u = cells[i].unit
		if u != null and u.ability == "charge_golem":
			_buff_unit(u, 2)


func _lightning_rod_gain() -> void:
	for i in cells.size():
		var u = cells[i].unit
		if u != null and u.ability == "lightning_rod":
			u.armor += 1


func _frost_guard_gain() -> void:
	for i in cells.size():
		var u = cells[i].unit
		if u != null and u.ability == "frost_guard":
			u.armor += FROST_GUARD_ARMOR


func _has_active_ability(ability: String) -> bool:
	for i in cells.size():
		var u = cells[i].unit
		if u != null and u.ability == ability and not _controlled(u):
			return true
	return false


# -----------------------------------------------------------------------------
# 地表
# -----------------------------------------------------------------------------
func _cell_water(idx: int) -> bool:
	return idx >= 0 and idx < terrain.size() and terrain[idx].water > 0


func _cell_electric(idx: int) -> bool:
	return idx >= 0 and idx < terrain.size() and terrain[idx].electric > 0


func _set_water(idx: int, turns: int) -> void:
	if idx < 0 or idx >= terrain.size():
		return
	terrain[idx].water = max(terrain[idx].water, turns)
	terrain[idx].electric = 0
	var u = cells[idx].unit
	if u != null and u.ability == "siren":       # 小女海妖:其格被附水→增益
		_buff_unit(u, turns)
		_log("小女海妖:获得 %d 点增益。" % turns)


func _clear_terrain(idx: int) -> void:
	terrain[idx].water = 0
	terrain[idx].electric = 0


func _spawn_water_random(turns: int) -> void:
	_set_water(randi() % cells.size(), turns)


# -----------------------------------------------------------------------------
# 怪物 / 死亡 / 增益
# -----------------------------------------------------------------------------
# 完全信息:整备时预生成本波生成计划(顺序、列、点数),供玩家全知规划。
func _roll_spawn_plan() -> void:
	spawn_plan.clear()
	var n := 2 + wave
	var p := _monster_power()
	# 开局 2 只同时进场,必须分配到不同列(否则会就近落位,使预览与实际不符)
	var opening := range(COLS)
	opening.shuffle()
	for i in min(2, n):
		spawn_plan.append({"col": opening[i], "power": p})
	# 其余每回合 1 只:进场时该列通常已空,随机列即可保证预览与实际一致
	for i in range(2, n):
		spawn_plan.append({"col": randi() % COLS, "power": p})


# 按计划生成下一只怪:计划列被占则就近找空列;顶行全满则等下回合(不消耗计划)。
func _spawn_next() -> bool:
	if spawn_plan.is_empty():
		return false
	var entry: Dictionary = spawn_plan[0]
	var col: int = int(entry.col)
	if cells[SPAWN_ROW * COLS + col].monster != null:
		col = _nearest_empty_spawn_col(col)
		if col < 0:
			return false
	spawn_plan.pop_front()
	var p: int = int(entry.power)
	cells[SPAWN_ROW * COLS + col].monster = {
		"power": p, "hp": p, "max_hp": p, "armor": 0,
		"side": "enemy", "stun": 0, "freeze": 0, "paralysis": 0,
	}
	spawn_quota -= 1
	return true


func _nearest_empty_spawn_col(col: int) -> int:
	for d in range(COLS):
		for c in [col - d, col + d]:
			if c >= 0 and c < COLS and cells[SPAWN_ROW * COLS + c].monster == null:
				return c
	return -1


func _monster_power() -> int:
	return 3 + wave


func _kill_unit_at(idx: int) -> void:
	var u = cells[idx].unit
	if u == null:
		return
	cells[idx].unit = null
	_on_any_death()


func _kill_monster_at(idx: int) -> void:
	if cells[idx].monster == null:
		return
	cells[idx].monster = null
	gold += GOLD_PER_KILL + (1 if _has_relic("crown") else 0)
	_on_any_death()


func _on_any_death() -> void:
	for i in cells.size():
		var u = cells[i].unit
		if u != null and u.ability == "ghost":
			_buff_unit(u, 1)


func _buff_unit(u: Dictionary, amt: int) -> void:
	u.power += amt
	u.hp += amt
	u.max_hp += amt


func _cleanup_dead() -> void:
	for i in cells.size():
		if cells[i].unit != null and cells[i].unit.hp <= 0:
			_kill_unit_at(i)
		if cells[i].monster != null and cells[i].monster.hp <= 0:
			_kill_monster_at(i)


# -----------------------------------------------------------------------------
# 工具
# -----------------------------------------------------------------------------
func _controlled(e) -> bool:
	return int(e.get("stun", 0)) > 0 or int(e.get("freeze", 0)) > 0


func _adjacent(a: int, b: int) -> bool:
	var ar := a / COLS
	var ac := a % COLS
	var br := b / COLS
	var bc := b % COLS
	return abs(ar - br) + abs(ac - bc) == 1


func _has_adjacent_unit(idx: int) -> bool:
	var r := idx / COLS
	var c := idx % COLS
	for d in [[-1, 0], [1, 0], [0, -1], [0, 1]]:
		var nr: int = r + d[0]
		var nc: int = c + d[1]
		if nr >= 0 and nr < ROWS and nc >= 0 and nc < COLS:
			if cells[nr * COLS + nc].unit != null:
				return true
	return false


func _count_units() -> int:
	var n := 0
	for i in cells.size():
		if cells[i].unit != null:
			n += 1
	return n


func _count_monsters() -> int:
	var n := 0
	for i in cells.size():
		if cells[i].monster != null:
			n += 1
	return n


# 备战席按卡名聚合的数量表:{di: count},供视图渲染「已有单位与数量」
func bench_counts() -> Dictionary:
	var d := {}
	for di in bench:
		d[di] = int(d.get(di, 0)) + 1
	return d


func _end_game(won: bool) -> void:
	game_over = true
	_log("=== %s ===" % ("胜利!抵御了全部 %d 波。" % WIN_WAVE if won else "失败……城市沦陷。"))


func _log(s: String) -> void:
	log_lines.append(s)
	while log_lines.size() > 7:
		log_lines.pop_front()
