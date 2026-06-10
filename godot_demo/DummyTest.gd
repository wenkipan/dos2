extends SceneTree
# =============================================================================
# 标准木桩测试 —— 实测「点数 / 护甲」兑换率。
#
# 复用 Game.gd 的真实战斗(_clash / _apply_damage),不另写公式,保证结论忠实于规则。
# 关键规则提醒(见 Game.gd):单位 hp == power(点数同时是攻击与血量);护甲先于血量
# 吸伤;_clash 是「同时互砍」——即使本击致死,攻击仍同时生效。
#
# 运行:
#   godot --headless --path godot_demo --script res://DummyTest.gd
#
# 价值口径:一个挡线单位的核心贡献 = 在被打穿前清掉多少只「标准木桩怪」(kills),
# 以及总共吸收了多少点来袭伤害(soak)。木桩怪 = 固定 power M、0 甲、hp==M。
# =============================================================================

const COLS := 6   # 与 Game.COLS 一致


func _initialize() -> void:
	var g = Game.new()
	g._init_state()

	print("============================================================")
	print(" 标准木桩测试:实测 点数/护甲 兑换率(复用真实 _clash)")
	print("   规则:单位 hp==点数;护甲先吸伤;同时互砍。怪物 hp==点数,0 甲。")
	print("============================================================\n")

	_test_soak(g)
	_test_throughput_sweep(g)
	_test_marginal_value(g)
	_test_iso_value(g)

	print("\n============================================================")
	print(" 结论见脚本末尾注释 / 对话总结。")
	print("============================================================")
	quit()


# 造一个「纯木桩」单位(无任何 ability,排除触发器干扰)。
func _make_unit(power: int, armor: int) -> Dictionary:
	return {
		"name": "木桩兵", "power": power, "hp": power, "max_hp": power,
		"armor": armor, "ability": "", "def_index": -1,
		"charge": 0, "cd": 0, "cooldown": 0, "mech": false, "side": "ally",
		"stun": 0, "freeze": 0, "paralysis": 0,
	}


func _make_monster(m: int) -> Dictionary:
	return {
		"power": m, "hp": m, "max_hp": m, "armor": 0,
		"side": "enemy", "stun": 0, "freeze": 0, "paralysis": 0,
	}


# 让 (power,armor) 的单位面对源源不断的「power=M」木桩怪,直到被打穿。
# 返回 {kills, soak}:清怪数 与 累计吸收伤害。
func _sim(g, power: int, armor: int, m: int, cap := 5000) -> Dictionary:
	g._init_state()
	var ui := 3 * COLS              # 玩家行的一格
	var mi := ui - COLS            # 正前方一格(怪被挡在此)
	g.cells[ui].unit = _make_unit(power, armor)
	var kills := 0
	var soak := 0
	for _iter in cap:
		if g.cells[mi].monster == null:
			g.cells[mi].monster = _make_monster(m)
		var u = g.cells[ui].unit
		var mon = g.cells[mi].monster
		var pool_before: int = int(u.hp) + int(u.armor)
		g._clash(ui, mi)
		if int(mon.hp) <= 0:
			kills += 1                       # 本次交锋怪被打死(同时互砍下即使我方同归于尽也算)
		if g.cells[ui].unit == null:
			soak += pool_before              # 单位被打穿,池子全耗
			break
		var u2 = g.cells[ui].unit
		soak += pool_before - (int(u2.hp) + int(u2.armor))
	return {"kills": kills, "soak": soak}


# --- 测试 1:纯吸伤(防御端)。M=1 的细砍,验证「1 甲 == 1 点 血量」 ---
func _test_soak(g) -> void:
	print("【测试1】纯吸伤基线(木桩怪 M=1,只看能扛多少点伤害):")
	print("  形态            点数  护甲 | 总吸伤")
	var cases := [[6, 0], [3, 3], [0, 6], [4, 2], [2, 4], [8, 4]]
	for cse in cases:
		var r := _sim(g, cse[0], cse[1], 1)
		print("  P=%-2d A=%-2d        %4d  %4d | %5d" % [cse[0], cse[1], cse[0], cse[1], r.soak])
	print("  → 防御端:总吸伤恒 = 点数 + 护甲,故纯吸伤上 1 甲 == 1 点。\n")


# --- 测试 2:清怪吞吐 sweep。对不同波次怪物点数 M,看 (P,A) 的清怪数 ---
func _test_throughput_sweep(g) -> void:
	print("【测试2】清怪吞吐:面对 power=M 的木桩怪,被打穿前清掉几只")
	var ms := [6, 12, 18, 24, 30]
	# 同一「数值预算 24」下,把预算全给点数 vs 平摊 vs 全给护甲(护甲不能单独攻击)
	var lines := [
		{"tag": "全点数 P=24,A=0 ", "p": 24, "a": 0},
		{"tag": "偏点数 P=18,A=6 ", "p": 18, "a": 6},
		{"tag": "平摊   P=12,A=12", "p": 12, "a": 12},
		{"tag": "偏护甲 P=6, A=18", "p": 6,  "a": 18},
	]
	var header := "  形态(预算=24)     "
	for m in ms:
		header += "  M=%-2d" % m
	print(header)
	for ln in lines:
		var row := "  %s" % ln.tag
		for m in ms:
			var r := _sim(g, ln.p, ln.a, m)
			row += "  %4d" % r.kills
		print(row)
	print("  → 当 M ≤ 点数:各形态清怪数相同(点数与护甲对等,纯靠总池子扛)。")
	print("  → 当 M >  点数:点数越高越优(因为点数还决定能否「一击清怪」)。\n")


# --- 测试 3:边际价值。在典型站位上,+1 点数 vs +1 护甲 各值多少清怪 ---
func _test_marginal_value(g) -> void:
	print("【测试3】边际价值:从基准 (P,A) 出发,+1点数 与 +1护甲 谁更值")
	print("  基准         M  | soak  +1点数soak  +1护甲soak | 清怪 +点 +甲")
	var bases := [[20, 0], [20, 0], [12, 12], [12, 12], [10, 10]]
	var ms := [12, 24, 18, 30, 25]
	for i in bases.size():
		var p: int = bases[i][0]
		var a: int = bases[i][1]
		var m: int = ms[i]
		var b := _sim(g, p, a, m)
		var bp := _sim(g, p + 1, a, m)
		var ba := _sim(g, p, a + 1, m)
		print("  P=%-2d A=%-2d   %-2d  | %4d  %8d  %8d | %d→%d/%d" % [
			p, a, m, int(b.soak), int(bp.soak), int(ba.soak),
			int(b.kills), int(bp.kills), int(ba.kills)])
	print("  → M>P 时 +1点数 既加血又加攻(soak 涨更多 / 更快清怪);M≤P 时 +1点数与+1甲对等。\n")


# --- 测试 4:等价值曲线。固定目标清怪数,反推 1 甲 ≈ 几点 ---
func _test_iso_value(g) -> void:
	print("【测试4】等价值:面对 M,达到与「纯点数 P0」相同清怪数所需的形态")
	var ms := [8, 12, 16]
	for m in ms:
		# 基准:纯点数,P0 使其清怪数明确
		var p0 := 16
		var target: int = _sim(g, p0, 0, m).kills
		print("  M=%d:基准 P=%d A=0 → 清 %d 只。等效形态:" % [m, p0, target])
		# 试图用更低点数 + 护甲补足
		for p in [15, 14, 13, 12, 11, 10, 9, 8]:
			# 二分/线性找最小 armor 使 kills>=target
			var a := 0
			while a <= 60 and _sim(g, p, a, m).kills < target:
				a += 1
			if a <= 60:
				var rate := float(p0 - p) / float(a) if a > 0 else 0.0
				print("    P=%-2d 需 A=%-2d 才追平  (省 %d 点 ↔ 加 %d 甲,1甲≈%.2f点)" % [
					p, a, p0 - p, a, rate])
	print("")
