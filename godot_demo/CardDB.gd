class_name CardDB
extends RefCounted
# =============================================================================
# 卡牌 / 指令 / 法术 数据表(只读常量)。对应 card.csv 第 1~41 行。
# 纯数据,不依赖 Game / UI。通过 CardDB.CARD_DEFS 等直接访问,无需实例化。
# =============================================================================

# --- 卡牌定义(card.csv 第 1~41 行)。单位用 ability,法术用 effect ---
# charge=初始充能;cooldown=冷却回合;mech=电控机械(免疫雷电);anywhere=可部署任意格
const CARD_DEFS := [
	# 兵 / 墙体
	{"name": "新兵",     "type": "unit",  "power": 6,  "armor": 0, "ability": "",               "desc": "纯墙体"},
	{"name": "老兵",     "type": "unit",  "power": 8,  "armor": 2, "ability": "",               "desc": "高战高甲"},
	{"name": "弓箭手",   "type": "unit",  "power": 4,  "armor": 0, "ability": "archer",         "charge": 3, "desc": "充能:对敌造成1伤害"},
	{"name": "法师",     "type": "unit",  "power": 4,  "armor": 0, "ability": "mage",           "cooldown": 1, "desc": "冷却1:令一单位充能+1"},
	{"name": "拒马",     "type": "unit",  "power": 4,  "armor": 2, "ability": "bulwark_armor",  "desc": "壁垒:回合末相邻+1甲"},
	{"name": "浪人",     "type": "unit",  "power": 10, "armor": 0, "ability": "ronin",          "anywhere": true, "desc": "部署:旁有友军则自毁(可任意格)"},
	{"name": "龙骑兵",   "type": "unit",  "power": 6,  "armor": 0, "ability": "dragoon",        "desc": "部署:摧毁前方一格"},
	{"name": "志愿军",   "type": "unit",  "power": 5,  "armor": 0, "ability": "volunteer",      "desc": "部署:每个友军+1增益"},
	{"name": "矮人",     "type": "unit",  "power": 4,  "armor": 2, "ability": "dwarf",          "desc": "失去护甲→获等量增益"},
	{"name": "龙龟",     "type": "unit",  "power": 4,  "armor": 4, "ability": "dragon_turtle",  "desc": "受伤→获等量护甲"},
	{"name": "石像",     "type": "unit",  "power": 4,  "armor": 6, "ability": "statue",         "charge": 1, "desc": "充能1:获得3点护甲"},
	{"name": "海绵",     "type": "unit",  "power": 4,  "armor": 0, "ability": "sponge",         "cooldown": 1, "desc": "冷却1:移除一格地表+1增益"},
	# 冰 / 水
	{"name": "霜甲卫士", "type": "unit",  "power": 6,  "armor": 4, "ability": "frost_guard",    "desc": "部署冻结自身;每冻结一单位+4甲"},
	{"name": "祈雨祭祀", "type": "unit",  "power": 4,  "armor": 0, "ability": "rain_priest",    "charge": 3, "desc": "充能:目标格附水2回合"},
	{"name": "小女海妖", "type": "unit",  "power": 4,  "armor": 0, "ability": "siren",          "desc": "其格被附水→获水回合数增益"},
	{"name": "催泪人鱼", "type": "unit",  "power": 8,  "armor": 0, "ability": "crybaby",        "desc": "每受伤→随机格产水2回合"},
	{"name": "海啸",     "type": "unit",  "power": 4,  "armor": 0, "ability": "tsunami",        "desc": "回合末对水上敌人造1伤害"},
	{"name": "潮汐卫兵", "type": "unit",  "power": 5,  "armor": 2, "ability": "tide_guard",     "desc": "部署:本列前方产水3回合"},
	# 雷 / 电控机械
	{"name": "水电法师", "type": "unit",  "power": 4,  "armor": 0, "ability": "storm_mage",     "charge": 3, "desc": "充能:对敌造1点雷电伤害"},
	{"name": "充电魔像", "type": "unit",  "power": 4,  "armor": 0, "ability": "charge_golem",   "mech": true, "desc": "雷电每致死一单位+2增益"},
	{"name": "避雷针",   "type": "unit",  "power": 2,  "armor": 0, "ability": "lightning_rod",  "mech": true, "desc": "每有敌人因麻痹眩晕→友方+1甲"},
	{"name": "引雷小鬼", "type": "unit",  "power": 4,  "armor": 0, "ability": "shock_imp",      "desc": "每回合对有麻痹值的敌人造1伤害"},
	{"name": "静电战士", "type": "unit",  "power": 4,  "armor": 6, "ability": "static_warrior", "mech": true, "desc": "壁垒:对攻击者+1麻痹值"},
	{"name": "避雷石像", "type": "unit",  "power": 4,  "armor": 4, "ability": "lightning_statue","mech": true, "desc": "壁垒:处带电地表回合末+3甲"},
	{"name": "充电桩",   "type": "unit",  "power": 4,  "armor": 0, "ability": "charge_station", "cooldown": 1, "mech": true, "desc": "冷却1:充能+1;带电地表→群体"},
	{"name": "超导体",   "type": "unit",  "power": 2,  "armor": 0, "ability": "superconductor", "charge": 1, "mech": true, "desc": "充能1:带电地表敌人+1麻痹值"},
	# 触发 / 其它
	{"name": "鬼灵",     "type": "unit",  "power": 2,  "armor": 0, "ability": "ghost",          "desc": "每有单位死亡→自身增益1"},
	{"name": "小鬼",     "type": "unit",  "power": 2,  "armor": 0, "ability": "imp",            "desc": "亡语:摧毁攻击者"},
	{"name": "魔狼",     "type": "unit",  "power": 4,  "armor": 0, "ability": "wolf",           "cooldown": 3, "desc": "冷却3:吞噬邻近单位获其点数"},
	# 法术
	{"name": "镜像",     "type": "spell", "power": 0,  "armor": 0, "effect": "mirror",          "desc": "在单位左侧生成同名镜像"},
	{"name": "护盾术",   "type": "spell", "power": 0,  "armor": 0, "effect": "shield",          "desc": "目标单位+6护甲"},
	{"name": "复原",     "type": "spell", "power": 0,  "armor": 0, "effect": "restore",         "desc": "从墓地取一张牌"},
	{"name": "滚石",     "type": "spell", "power": 0,  "armor": 0, "effect": "knockback",       "desc": "强制移动单位1格"},
	{"name": "飓风",     "type": "spell", "power": 0,  "armor": 0, "effect": "hurricane",       "desc": "清除所有地表"},
	{"name": "绝对零度", "type": "spell", "power": 0,  "armor": 0, "effect": "absolute_zero",   "desc": "冻结所有湿润单位"},
	{"name": "寒霜之拥", "type": "spell", "power": 0,  "armor": 0, "effect": "frost_embrace",   "desc": "冻结单位;己方则+8甲"},
	{"name": "降雨术",   "type": "spell", "power": 0,  "armor": 0, "effect": "rain",            "desc": "2x4 范围产水3回合"},
	{"name": "落雷术",   "type": "spell", "power": 0,  "armor": 0, "effect": "lightning_bolt",  "desc": "对一敌造5点雷电伤害"},
	{"name": "雷霆制裁", "type": "spell", "power": 0,  "armor": 0, "effect": "thunder_judgment","desc": "6点雷电;眩晕则再+6"},
	{"name": "牺牲",     "type": "spell", "power": 0,  "armor": 0, "effect": "sacrifice",       "desc": "杀一单位,令另一单位获其点数"},
]

# 主动指令:gate=充能/冷却;target=目标类型
const COMMANDS := {
	"archer":         {"gate": "charge",   "target": "enemy"},
	"storm_mage":     {"gate": "charge",   "target": "enemy"},
	"rain_priest":    {"gate": "charge",   "target": "cell"},
	"statue":         {"gate": "charge",   "target": "self"},
	"superconductor": {"gate": "charge",   "target": "self"},
	"mage":           {"gate": "cooldown", "target": "friendly"},
	"sponge":         {"gate": "cooldown", "target": "cell"},
	"charge_station": {"gate": "cooldown", "target": "friendly"},
	"wolf":           {"gate": "cooldown", "target": "friendly"},
}

# 法术需要的目标数(0=全局,无需指定格子)
const SPELL_TARGETS := {
	"mirror": 1, "shield": 1, "restore": 0, "knockback": 1, "hurricane": 0,
	"absolute_zero": 0, "frost_embrace": 1, "rain": 1, "lightning_bolt": 1,
	"thunder_judgment": 1, "sacrifice": 2,
}

# 起始备战席(自走棋:全为单位,运行时由 Game 解析为 CARD_DEFS 下标)
const STARTER_NAMES := ["新兵", "新兵", "新兵", "弓箭手", "弓箭手", "拒马", "法师", "水电法师", "祈雨祭祀"]

# 遗物(整备回合可购买;被动效果在 Game 的对应钩子里按 id 生效)
const RELIC_DEFS := [
	{"id": "horn",  "name": "丰饶之角", "price": 5, "desc": "整备收入 +2"},
	{"id": "crown", "name": "鲜血王冠", "price": 5, "desc": "每击杀额外 +1 金"},
	{"id": "totem", "name": "壁垒图腾", "price": 6, "desc": "单位上场/重置时 +1 护甲"},
]
