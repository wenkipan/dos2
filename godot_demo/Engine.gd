extends SceneTree
# =============================================================================
# 无头交互接口 —— 给「人」(由我,Claude)手动操作 Game 用的命令行驱动。
# 不含任何 AI / 机器人决策:它只负责 (1) 持久化 Game 状态到文件,(2) 应用我给的
# 一条/多条意图命令,(3) 把完整「界面信息」打印出来供我读取后决定下一步。
#
# 用法:
#   godot --headless --path godot_demo --script res://Engine.gd -- "<命令; 命令; ...>"
#
# 命令(对应 Game 的高层意图方法;分号分隔可一次多条):
#   new [seed]      新开一局(可选随机种子,便于复现)
#   view            只打印当前界面,不改动
#   bench <di>      选中备战席某单位(di = CARD_DEFS 下标,见 bench 列表)
#   cell <idx>      点棋盘格 idx(整备=部署/调位/解散; 小局=用指令/选指令目标)
#   buy <i>         购买商店第 i 个卡牌报价
#   relic <i>       购买第 i 个遗物
#   refresh         刷新商店(花费金币)
#   disband         切换解散模式
#   start           开始小局(战斗)
#   end             结束当前战斗回合(推进怪物)
#   pass [n]        自动连续结束回合(默认 n=99),但遇到以下情况立即停下并渲染:
#                   城市完整度下降 / 本波清空(进入整备)/ 游戏结束。用于快速跳过平稳回合。
#
# 部署一个单位 = "bench <di>; cell <idx>" 两步;调位 = "cell <src>; cell <dst>"。
# 用指令 = "cell <单位格>"(若需目标会进入定位)再 "cell <目标格>"。
# =============================================================================

const STATE_PATH := "user://engine_state.txt"
const PERSIST := ["cells", "terrain", "phase", "bench", "relics", "aim", "gold",
	"city_hp", "wave", "battle_turn", "spawn_quota", "spawn_plan", "selected_bench",
	"selected_cell", "disband_mode", "shop_offers", "relic_offers",
	"game_over", "log_lines"]


func _init() -> void:
	var raw := " ".join(OS.get_cmdline_user_args())
	var cmds := raw.split(";", false)

	var game: Game = null
	for raw_c in cmds:
		var c: String = raw_c.strip_edges()
		if c == "":
			continue
		var parts := c.split(" ", false)
		var op: String = parts[0]

		if op == "new":
			var sd := int(parts[1]) if parts.size() > 1 else int(Time.get_unix_time_from_system())
			seed(sd)
			game = Game.new()
			game.setup()
			print("[新开一局 seed=%d]" % sd)
			continue

		if game == null:
			game = _load()
			if game == null:
				print("没有存档,请先执行: new [seed]")
				quit()
				return

		match op:
			"view":
				pass
			"bench":
				game.bench_pressed(int(parts[1]))
			"cell":
				game.cell_pressed(int(parts[1]))
			"buy":
				game.shop_buy(int(parts[1]))
			"relic":
				game.buy_relic(int(parts[1]))
			"refresh":
				game.refresh_shop()
			"disband":
				game.disband_toggle()
			"start":
				game.start_battle()
			"end":
				game.end_turn()
			"pass":
				var n := int(parts[1]) if parts.size() > 1 else 99
				var did := 0
				while did < n and not game.game_over and game.phase == Game.Phase.BATTLE and game.aim.is_empty():
					var hp_before := game.city_hp
					var wave_before := game.wave
					game.end_turn()
					did += 1
					if game.city_hp < hp_before or game.wave != wave_before or game.game_over:
						break
				print("[pass: 自动结束了 %d 个回合]" % did)
			_:
				print("未知命令: ", c)

	if game == null:
		game = _load()
	if game != null:
		_save(game)
		_render(game)
	quit()


# -----------------------------------------------------------------------------
# 持久化(用 var_to_str / str_to_var,完整保留 Variant 类型)
# -----------------------------------------------------------------------------
func _save(game: Game) -> void:
	var snap := {}
	for k in PERSIST:
		snap[k] = game.get(k)
	var f := FileAccess.open(STATE_PATH, FileAccess.WRITE)
	f.store_string(var_to_str(snap))
	f.close()


func _load() -> Game:
	if not FileAccess.file_exists(STATE_PATH):
		return null
	var f := FileAccess.open(STATE_PATH, FileAccess.READ)
	var snap = str_to_var(f.get_as_text())
	f.close()
	var game := Game.new()
	_build_tables(game)
	for k in PERSIST:
		if snap.has(k):
			game.set(k, snap[k])
	return game


# 重建从 CardDB 派生的只读表(setup() 里那一段),不重置运行时状态。
func _build_tables(game: Game) -> void:
	for i in CardDB.CARD_DEFS.size():
		game.name_to_di[CardDB.CARD_DEFS[i].name] = i
		if CardDB.CARD_DEFS[i].type == "unit":
			game.unit_pool.append(i)


# -----------------------------------------------------------------------------
# 渲染「界面信息」
# -----------------------------------------------------------------------------
func _render(game: Game) -> void:
	var prep: bool = game.phase == Game.Phase.PREP
	var ph := "整备" if prep else "小局·回合%d" % game.battle_turn
	if game.game_over:
		ph = "已结束"
	elif not game.aim.is_empty():
		ph += " [指令定位中:点目标格]"
	print("\n================ 界面 ================")
	print("城市 %d/%d | 波次 %d/%d | 金币 %d | 上场 %d | 备战席 %d | %s" % [
		game.city_hp, Game.CITY_HP_MAX, game.wave, Game.WIN_WAVE, game.gold,
		game._count_units(), game.bench.size(), ph])

	_render_board(game)
	_render_spawn_plan(game)

	if prep:
		_render_bench(game)
		_render_shop(game)
		_render_relics(game)
	else:
		_render_threats(game)
		_render_battle_help(game)

	print("-- 日志 --")
	for l in game.log_lines:
		print("  ", l)
	print("======================================")


# 完全信息:展示本波怪物生成计划(顺序 = 进场先后,前两个为开局,其余每回合 1 只)
func _render_spawn_plan(game: Game) -> void:
	if game.spawn_plan.is_empty():
		if game.phase == Game.Phase.BATTLE:
			print("-- 完全信息·剩余生成:(本波已全部进场)--")
		return
	var prep: bool = game.phase == Game.Phase.PREP
	var cols := []
	for e in game.spawn_plan:
		cols.append("c%d" % int(e.col))
	var p: int = int(game.spawn_plan[0].power)
	if prep:
		var head: Array = cols.slice(0, min(2, cols.size()))
		var rest: Array = cols.slice(2, cols.size())
		var rest_s: String = (" → ".join(rest)) if rest.size() > 0 else "(无)"
		print("-- 完全信息·本波生成计划(点数%d,共%d只):开局[%s] 随后每回合→ %s --" % [
			p, cols.size(), ", ".join(head), rest_s])
	else:
		print("-- 完全信息·剩余生成(点数%d,每回合1只按序):%s --" % [p, " → ".join(cols)])


func _render_board(game: Game) -> void:
	print("棋盘(格内左上为格号 idx;上%d行怪物区, 下两行=玩家区):" % (Game.ROWS - 2))
	# 列号表头
	var head := "       "
	for c in Game.COLS:
		head += "c%-13d" % c
	print(head)
	for r in Game.ROWS:
		var tag := "玩" if r in Game.PLAYER_ROWS else "敌"
		var line := "r%d %s | " % [r, tag]
		for c in Game.COLS:
			var idx := r * Game.COLS + c
			line += "%-14s" % _cell_str(game, idx)
		print(line)


func _cell_str(game: Game, idx: int) -> String:
	var cell = game.cells[idx]
	var s := "#%d:" % idx
	if cell.monster != null:
		s += "怪%d" % cell.monster.hp
		if cell.monster.armor > 0:
			s += "甲%d" % cell.monster.armor
		s += _ctl(cell.monster)
	elif cell.unit != null:
		s += "%s%d" % [_short(cell.unit.name), cell.unit.hp]
		if cell.unit.armor > 0:
			s += "甲%d" % cell.unit.armor
		if int(cell.unit.get("charge", 0)) > 0:
			s += "充%d" % cell.unit.charge
		if int(cell.unit.get("cooldown", 0)) > 0 and int(cell.unit.get("cd", 0)) > 0:
			s += "却%d" % cell.unit.cd
		s += _ctl(cell.unit)
		if game.phase == Game.Phase.BATTLE and game.cmd_ready(cell.unit):
			s += "▶"
	var t := ""
	if game.terrain[idx].water > 0:
		t += "水%d" % game.terrain[idx].water
	if game.terrain[idx].electric > 0:
		t += "电%d" % game.terrain[idx].electric
	if t != "":
		s += "{" + t + "}"
	return s


func _ctl(e) -> String:
	var s := ""
	if int(e.get("freeze", 0)) > 0:
		s += "冻%d" % e.freeze
	if int(e.get("stun", 0)) > 0:
		s += "晕%d" % e.stun
	if int(e.get("paralysis", 0)) > 0:
		s += "麻%d" % e.paralysis
	return s


func _short(name: String) -> String:
	return name.substr(0, 2)


func _render_bench(game: Game) -> void:
	print("-- 备战席(共 %d)| 命令: bench <di> 选, 再 cell <idx> 上场 --" % game.bench.size())
	var counts: Dictionary = game.bench_counts()
	if counts.is_empty():
		print("  (空)")
	for di in counts:
		var d: Dictionary = CardDB.CARD_DEFS[di]
		print("  di=%-2d %s x%d  战%d甲%d  %s" % [
			di, d.name, counts[di], d.power, d.get("armor", 0), d.get("desc", "")])


func _render_shop(game: Game) -> void:
	print("-- 商店卡牌(单价 %d 金)| 命令: buy <i> --" % Game.CARD_PRICE)
	for i in game.shop_offers.size():
		var off: Dictionary = game.shop_offers[i]
		var d: Dictionary = CardDB.CARD_DEFS[off.di]
		var sold := " [已售]" if off.sold else ""
		print("  i=%d %s 战%d甲%d  %s%s" % [
			i, d.name, d.power, d.get("armor", 0), d.get("desc", ""), sold])
	print("-- 命令: refresh 刷新商店(花 %d 金) --" % Game.SHOP_REFRESH_COST)


func _render_relics(game: Game) -> void:
	print("-- 遗物 | 命令: relic <i> --")
	for i in game.relic_offers.size():
		var off: Dictionary = game.relic_offers[i]
		var d: Dictionary = CardDB.RELIC_DEFS[i]
		var sold := " [已拥有]" if off.sold else ""
		print("  i=%d %s %d金  %s%s" % [i, d.name, d.price, d.desc, sold])


func _render_threats(game: Game) -> void:
	# 怪物威胁表:按「距离漏怪步数」升序(越小越危险)。步数 = ROWS - row。
	var threats := []
	for i in game.cells.size():
		var m = game.cells[i].monster
		if m != null:
			var r := i / Game.COLS
			threats.append({"idx": i, "c": i % Game.COLS, "r": r,
				"steps": Game.ROWS - r, "hp": int(m.hp), "ctl": _ctl(m)})
	threats.sort_custom(func(a, b): return a.steps < b.steps)
	print("-- 威胁(%d 只怪 | 距漏怪步数↑越危险)--" % threats.size())
	for t in threats:
		print("  #%d c%d hp%d%s  %d步漏" % [t.idx, t.c, t.hp, t.ctl, t.steps])
	# 每列我方最前方防守者(谁在挡这一列)
	var line := "-- 各列前哨: "
	for c in Game.COLS:
		var who := "空"
		for r in Game.PLAYER_ROWS:
			var u = game.cells[r * Game.COLS + c].unit
			if u != null:
				who = "%s%d" % [_short(u.name), u.hp]
				if u.armor > 0:
					who += "甲%d" % u.armor
				break
		line += "c%d=%s " % [c, who]
	print(line, "--")


func _render_battle_help(game: Game) -> void:
	if not game.aim.is_empty():
		print("-- 指令定位中:cell <目标格>;再点施法单位自身可取消 --")
	else:
		print("-- 小局:cell <己方单位格> 用其指令(▶=可用); end 结束回合 --")
