extends Control
# =============================================================================
# 视图层 —— 构建 UI(全部用 Button/Label)、按阶段渲染棋盘 / 备战席 / 商店、转发输入。
# 游戏规则与状态全部在 Game.gd;数据表在 CardDB.gd。本文件只读 game 的公共状态,
# 通过 game 的高层意图方法驱动逻辑,每次输入后调用 refresh_ui() 重绘。
#
# 两个阶段:
#   整备(PREP):备战席 + 商店(卡牌/遗物)可见;点备战席→点格上场;点单位调位;解散。
#   小局(BATTLE):只渲染棋盘 + 战斗日志;点己方单位用其充能/冷却指令;结束回合推进。
# =============================================================================

var game: Game

# --- UI 节点引用 ---
var cell_buttons: Array = []
var hud_label: Label
var log_label: Label
var bench_label: Label
var bench_box: HBoxContainer
var shop_label: Label
var shop_box: HBoxContainer
var relic_label: Label
var relic_box: HBoxContainer
var refresh_btn: Button
var disband_btn: Button
var action_btn: Button


func _ready() -> void:
	game = Game.new()
	game.setup()
	_build_ui()
	refresh_ui()


# -----------------------------------------------------------------------------
# UI 构建(全部用 Button/Label)
# -----------------------------------------------------------------------------
func _build_ui() -> void:
	var root := VBoxContainer.new()
	root.anchor_right = 1.0
	root.anchor_bottom = 1.0
	root.offset_left = 12
	root.offset_top = 12
	root.offset_right = -12
	root.offset_bottom = -12
	root.add_theme_constant_override("separation", 6)
	add_child(root)

	hud_label = Label.new()
	hud_label.add_theme_font_size_override("font_size", 18)
	root.add_child(hud_label)

	var grid := GridContainer.new()
	grid.columns = Game.COLS
	grid.add_theme_constant_override("h_separation", 4)
	grid.add_theme_constant_override("v_separation", 4)
	root.add_child(grid)
	for r in Game.ROWS:
		for c in Game.COLS:
			var b := Button.new()
			b.custom_minimum_size = Vector2(150, 60)
			b.clip_text = true
			b.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
			b.add_theme_font_size_override("font_size", 13)
			var idx := r * Game.COLS + c
			b.pressed.connect(_on_cell_pressed.bind(idx))
			grid.add_child(b)
			cell_buttons.append(b)

	log_label = Label.new()
	log_label.add_theme_font_size_override("font_size", 14)
	log_label.custom_minimum_size = Vector2(0, 110)
	log_label.vertical_alignment = VERTICAL_ALIGNMENT_TOP
	root.add_child(log_label)

	bench_label = Label.new()
	bench_label.add_theme_font_size_override("font_size", 14)
	root.add_child(bench_label)
	bench_box = HBoxContainer.new()
	bench_box.add_theme_constant_override("separation", 6)
	root.add_child(bench_box)

	shop_label = Label.new()
	shop_label.add_theme_font_size_override("font_size", 15)
	root.add_child(shop_label)
	shop_box = HBoxContainer.new()
	shop_box.add_theme_constant_override("separation", 6)
	root.add_child(shop_box)

	relic_label = Label.new()
	relic_label.add_theme_font_size_override("font_size", 15)
	root.add_child(relic_label)
	relic_box = HBoxContainer.new()
	relic_box.add_theme_constant_override("separation", 6)
	root.add_child(relic_box)

	var bottom := HBoxContainer.new()
	bottom.add_theme_constant_override("separation", 8)
	root.add_child(bottom)

	refresh_btn = Button.new()
	refresh_btn.custom_minimum_size = Vector2(0, 40)
	refresh_btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	refresh_btn.pressed.connect(_on_refresh_shop)
	bottom.add_child(refresh_btn)

	disband_btn = Button.new()
	disband_btn.custom_minimum_size = Vector2(0, 40)
	disband_btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	disband_btn.pressed.connect(_on_disband_toggle)
	bottom.add_child(disband_btn)

	action_btn = Button.new()
	action_btn.custom_minimum_size = Vector2(0, 40)
	action_btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	action_btn.pressed.connect(_on_action)
	bottom.add_child(action_btn)


# -----------------------------------------------------------------------------
# 输入转发:调用 game 的意图方法,然后重绘
# -----------------------------------------------------------------------------
func _on_cell_pressed(idx: int) -> void:
	game.cell_pressed(idx)
	refresh_ui()


func _on_bench_pressed(di: int) -> void:
	game.bench_pressed(di)
	refresh_ui()


func _on_shop_buy(i: int) -> void:
	game.shop_buy(i)
	refresh_ui()


func _on_buy_relic(i: int) -> void:
	game.buy_relic(i)
	refresh_ui()


func _on_refresh_shop() -> void:
	game.refresh_shop()
	refresh_ui()


func _on_disband_toggle() -> void:
	game.disband_toggle()
	refresh_ui()


func _on_action() -> void:
	if game.phase == Game.Phase.PREP:
		game.start_battle()
	else:
		game.end_turn()
	refresh_ui()


# -----------------------------------------------------------------------------
# 渲染
# -----------------------------------------------------------------------------
func refresh_ui() -> void:
	var prep := game.phase == Game.Phase.PREP
	var phase_txt := ""
	if game.game_over:
		phase_txt = "已结束"
	elif prep:
		phase_txt = "整备回合(构筑棋盘)"
	elif not game.aim.is_empty():
		phase_txt = "小局 · 指令定位(点目标格)"
	else:
		phase_txt = "小局 · 回合 %d(用单位指令)" % game.battle_turn
	hud_label.text = "城市:%d/%d  波次:%d/%d  金币:%d  上场:%d  备战席:%d  %s" % [
		game.city_hp, Game.CITY_HP_MAX, game.wave, Game.WIN_WAVE, game.gold,
		game._count_units(), game.bench.size(), phase_txt
	]

	_render_board(prep)
	_render_bench(prep)
	_render_shop(prep)
	_render_relics(prep)

	refresh_btn.text = "刷新商店(花 %d 金)" % Game.SHOP_REFRESH_COST
	refresh_btn.disabled = game.game_over or not prep or game.busy() or game.gold < Game.SHOP_REFRESH_COST
	_set_btn_bg(refresh_btn, Color(0.33, 0.28, 0.16))
	refresh_btn.add_theme_color_override("font_color", Color(1, 1, 1))

	disband_btn.text = "取消解散" if game.disband_mode else "解散单位(收回备战席)"
	disband_btn.disabled = game.game_over or not prep or game.busy()
	_set_btn_bg(disband_btn, Color(0.55, 0.30, 0.20) if game.disband_mode else Color(0.30, 0.30, 0.36))
	disband_btn.add_theme_color_override("font_color", Color(1, 1, 1))

	action_btn.text = "开始小局 ▶" if prep else "结束回合 ▶"
	action_btn.disabled = game.game_over or game.busy()
	_set_btn_bg(action_btn, Color(0.20, 0.40, 0.26) if prep else Color(0.30, 0.30, 0.36))
	action_btn.add_theme_color_override("font_color", Color(1, 1, 1))

	log_label.text = "\n".join(game.log_lines)


func _render_board(prep: bool) -> void:
	for i in game.cells.size():
		var b: Button = cell_buttons[i]
		var cell = game.cells[i]
		var txt := ""
		var col := Color(0.22, 0.22, 0.26)
		if (i / Game.COLS) in Game.PLAYER_ROWS:
			col = Color(0.20, 0.28, 0.24)
		if game.terrain[i].water > 0:
			col = col.lerp(Color(0.18, 0.34, 0.55), 0.55)
		if game.terrain[i].electric > 0:
			col = col.lerp(Color(0.45, 0.35, 0.62), 0.6)
		if cell.monster != null:
			col = col.lerp(Color(0.6, 0.2, 0.2), 0.55)
			txt = "怪 战%d" % cell.monster.hp
			if cell.monster.armor > 0:
				txt += " 甲%d" % cell.monster.armor
			txt += _status_tag(cell.monster)
		elif cell.unit != null:
			if game.disband_mode:
				col = col.lerp(Color(0.6, 0.4, 0.2), 0.5)
			if prep and game.selected_cell == i:
				col = Color(0.85, 0.7, 0.2)
			if not game.aim.is_empty() and int(game.aim.get("src", -1)) == i:
				col = Color(0.85, 0.7, 0.2)
			txt = "%s 战%d" % [cell.unit.name, cell.unit.hp]
			if cell.unit.armor > 0:
				txt += " 甲%d" % cell.unit.armor
			if int(cell.unit.get("charge", 0)) > 0:
				txt += " 充%d" % cell.unit.charge
			if int(cell.unit.get("cooldown", 0)) > 0 and int(cell.unit.get("cd", 0)) > 0:
				txt += " 却%d" % cell.unit.cd
			txt += _status_tag(cell.unit)
			if not prep and game.cmd_ready(cell.unit):
				txt += " ▶"
		txt += _terrain_tag(i)
		b.text = txt
		b.add_theme_color_override("font_color", Color(1, 1, 1))
		b.disabled = game.game_over
		_set_btn_bg(b, col)


func _status_tag(e) -> String:
	var s := ""
	if int(e.get("freeze", 0)) > 0:
		s += " 冻%d" % e.freeze
	if int(e.get("stun", 0)) > 0:
		s += " 晕%d" % e.stun
	if int(e.get("paralysis", 0)) > 0:
		s += " 麻%d" % e.paralysis
	if int(e.get("wet", 0)) > 0:
		s += " 湿%d" % e.wet
	return s


func _terrain_tag(i: int) -> String:
	var s := ""
	if game.terrain[i].water > 0:
		s += " 水%d" % game.terrain[i].water
	if game.terrain[i].electric > 0:
		s += " 电%d|%d" % [game.terrain[i].electric, game.terrain[i].eturns]
	return s


func _render_bench(prep: bool) -> void:
	_clear_box(bench_box)
	if not prep:
		bench_label.text = "—— 小局进行中:只能点己方单位使用指令,「结束回合」推进 ——"
		return
	var counts: Dictionary = game.bench_counts()
	bench_label.text = "备战席(共 %d)—— 点一个单位选中,再点己方下两行的空格上场:" % game.bench.size()
	if counts.is_empty():
		var empty := Label.new()
		empty.text = "(空,去商店买单位)"
		empty.add_theme_color_override("font_color", Color(0.7, 0.7, 0.7))
		bench_box.add_child(empty)
		return
	for di in counts:
		var d: Dictionary = CardDB.CARD_DEFS[di]
		var cb := Button.new()
		cb.custom_minimum_size = Vector2(0, 60)
		cb.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		cb.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		cb.clip_text = true
		cb.add_theme_font_size_override("font_size", 12)
		cb.add_theme_color_override("font_color", Color(1, 1, 1))
		var stat := "战%d" % d.power
		if int(d.armor) > 0:
			stat += " 甲%d" % d.armor
		cb.text = "%s ×%d\n%s · %s" % [d.name, counts[di], stat, d.get("desc", "")]
		var bg := Color(0.30, 0.30, 0.36)
		if di == game.selected_bench:
			bg = Color(0.85, 0.7, 0.2)
		_set_btn_bg(cb, bg)
		cb.pressed.connect(_on_bench_pressed.bind(di))
		bench_box.add_child(cb)


func _render_shop(prep: bool) -> void:
	_clear_box(shop_box)
	if not prep:
		shop_label.text = ""
		return
	shop_label.text = "【商店 · 卡牌】单价 %d 金 —— 购买进备战席:" % Game.CARD_PRICE
	for i in game.shop_offers.size():
		var offer: Dictionary = game.shop_offers[i]
		var d: Dictionary = CardDB.CARD_DEFS[offer.di]
		var btn := Button.new()
		btn.custom_minimum_size = Vector2(0, 60)
		btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		btn.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		btn.clip_text = true
		btn.add_theme_color_override("font_color", Color(1, 1, 1))
		if offer.sold:
			btn.text = "%s\n已售出" % d.name
			btn.disabled = true
			_set_btn_bg(btn, Color(0.20, 0.20, 0.22))
		else:
			var stat := "战%d" % d.power
			if int(d.armor) > 0:
				stat += " 甲%d" % d.armor
			btn.text = "%s %d金\n%s · %s" % [d.name, Game.CARD_PRICE, stat, d.get("desc", "")]
			btn.disabled = game.gold < Game.CARD_PRICE
			_set_btn_bg(btn, Color(0.33, 0.28, 0.16))
		btn.pressed.connect(_on_shop_buy.bind(i))
		shop_box.add_child(btn)


func _render_relics(prep: bool) -> void:
	_clear_box(relic_box)
	if not prep:
		relic_label.text = ""
		return
	relic_label.text = "【商店 · 遗物】置于上栏,作用于全局战局:"
	for i in game.relic_offers.size():
		var offer: Dictionary = game.relic_offers[i]
		var d: Dictionary = CardDB.RELIC_DEFS[i]
		var btn := Button.new()
		btn.custom_minimum_size = Vector2(0, 56)
		btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		btn.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		btn.clip_text = true
		btn.add_theme_color_override("font_color", Color(1, 1, 1))
		if offer.sold:
			btn.text = "%s\n已拥有" % d.name
			btn.disabled = true
			_set_btn_bg(btn, Color(0.20, 0.20, 0.22))
		else:
			btn.text = "%s %d金\n%s" % [d.name, d.price, d.desc]
			btn.disabled = game.gold < int(d.price)
			_set_btn_bg(btn, Color(0.30, 0.24, 0.34))
		btn.pressed.connect(_on_buy_relic.bind(i))
		relic_box.add_child(btn)


func _clear_box(box: Node) -> void:
	while box.get_child_count() > 0:
		var ch := box.get_child(0)
		box.remove_child(ch)
		ch.queue_free()


func _set_btn_bg(b: Button, c: Color) -> void:
	var sb := StyleBoxFlat.new()
	sb.bg_color = c
	sb.set_corner_radius_all(4)
	sb.set_content_margin_all(4)
	b.add_theme_stylebox_override("normal", sb)
	var hb := sb.duplicate()
	hb.bg_color = c.lightened(0.12)
	b.add_theme_stylebox_override("hover", hb)
	b.add_theme_stylebox_override("pressed", hb)
	b.add_theme_stylebox_override("disabled", sb)
