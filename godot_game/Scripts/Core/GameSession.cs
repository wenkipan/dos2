namespace BreakerGrid.Core;

public sealed class GameSession
{
	public const int CityHpMax = 10;
	public const int WinWave = 10;
	public const int GoldStart = 8;
	public const int PrepIncome = 4;
	public const int ShopSize = 5;
	public const int FallbackCardPrice = 6;

	public GameData Data { get; }
	public Board Grid { get; } = new();
	public ActionQueue Queue { get; } = new();
	public List<string> Bench { get; } = new();
	public List<SpellSlot> SpellSlots { get; } = new();
	public List<string> SpellInventory { get; } = new();
	public List<string> Relics { get; } = new();
	public List<ShopOffer> ShopOffers { get; } = new();
	public List<RelicOffer> RelicOffers { get; } = new();
	public List<SpawnEntry> SpawnPlan { get; } = new();
	public List<string> LogLines { get; } = new();

	public Phase Phase { get; set; } = Phase.Prep;
	public int Seed { get; set; }
	public int Gold { get; set; } = GoldStart;
	public int CityHp { get; set; } = CityHpMax;
	public int Wave { get; set; } = 1;
	public int BattleTurn { get; set; } = 1;
	public int NextInstanceId { get; set; } = 1;
	public bool GameOver { get; set; }
	public bool Won { get; set; }
	public GameRng ShopRng { get; set; }
	public GameRng WaveRng { get; set; }
	public GameRng EffectRng { get; set; }

	public GameSession(GameData data, int seed)
	{
		Data = data;
		Seed = seed;
		ShopRng = new GameRng((ulong)seed ^ 0xA11CEUL);
		WaveRng = new GameRng((ulong)seed ^ 0xB047DUL);
		EffectRng = new GameRng((ulong)seed ^ 0xEFFE17UL);
	}

	public static GameSession New(GameData data, int seed)
	{
		var game = new GameSession(data, seed);
		game.AddStarterCards();
		game.Log("Breaker 正式核心:破甲、尖刺地表与法术冷却已启用。");
		game.EnterPrep(giveIncome: false);
		return game;
	}

	public void Submit(string name, Action<GameSession> execute)
	{
		if (GameOver)
		{
			Log("游戏已结束。");
			return;
		}

		Queue.Enqueue(new DelegateAction(name, execute));
		Queue.ResolveAll(this);
	}

	public void BuyCard(int offerIndex) => Submit("购买卡牌", g => g.DoBuyCard(offerIndex));
	public void BuyRelic(int offerIndex) => Submit("购买遗物", g => g.DoBuyRelic(offerIndex));
	public void DeployUnit(int benchIndex, int cellIndex) => Submit("部署单位", g => g.DoDeployUnit(benchIndex, cellIndex));
	public void MoveUnit(int src, int dst) => Submit("移动单位", g => g.DoMoveUnit(src, dst, prepMove: true));
	public void DisbandUnit(int cellIndex) => Submit("撤回单位", g => g.DoDisbandUnit(cellIndex));
	public void StartBattle() => Submit("开始小局", g => g.DoStartBattle());
	public void UseCommand(int sourceCell, int? targetCell) => Submit("使用指令", g => g.DoUseCommand(sourceCell, targetCell));
	public void CastSpell(int spellIndex, int? targetCell, int? extraCell) => Submit("释放法术", g => g.DoCastSpell(spellIndex, targetCell, extraCell));
	public void EndTurn() => Submit("结束回合", g => g.DoEndTurn());

	public CardDef Card(string name) => Data.CardsByName[name];

	public bool HasRelic(string name) => Relics.Contains(name);

	public IEnumerable<UnitInstance> FriendlyUnits()
	{
		for (var i = 0; i < Grid.Cells.Length; i++)
		{
			var unit = Grid.Cells[i].Unit;
			if (unit != null && !unit.IsDead)
			{
				yield return unit;
			}
		}
	}

	public IEnumerable<MonsterInstance> EnemyMonsters()
	{
		for (var i = 0; i < Grid.Cells.Length; i++)
		{
			var monster = Grid.Cells[i].Monster;
			if (monster != null && !monster.IsDead)
			{
				yield return monster;
			}
		}
	}

	public Combatant? EntityAt(int cell)
	{
		if (!Board.IsValidCell(cell))
		{
			return null;
		}

		return Grid.Cells[cell].Monster as Combatant ?? Grid.Cells[cell].Unit;
	}

	public void Log(string line)
	{
		LogLines.Add(line);
		if (LogLines.Count > 18)
		{
			LogLines.RemoveAt(0);
		}
	}

	private void AddStarterCards()
	{
		AddBench("新兵", 3);
		AddBench("弓箭手", 2);
		AddBench("拒马", 1);
		AddBench("矮人", 1);
		AddBench("地刺", 1);
		AddBench("工兵", 1);
		AddSpellSlot("尖刺诅咒");
		AddSpellSlot("岩突");
		AddSpellSlot("举盾");
	}

	private void AddBench(string name, int count)
	{
		if (!Data.CardsByName.ContainsKey(name))
		{
			return;
		}

		for (var i = 0; i < count; i++)
		{
			Bench.Add(name);
		}
	}

	private void AddSpellSlot(string name)
	{
		if (Data.CardsByName.ContainsKey(name) && SpellSlots.Count < 3)
		{
			SpellSlots.Add(new SpellSlot { CardName = name });
		}
	}

	private void EnterPrep(bool giveIncome)
	{
		if (Wave > WinWave)
		{
			EndGame(won: true);
			return;
		}

		Phase = Phase.Prep;
		BattleTurn = 1;
		if (giveIncome)
		{
			Gold += PrepIncome;
			Log($"【整备】收入 +{PrepIncome} 金，现有 {Gold} 金。");
		}

		foreach (var slot in SpellSlots)
		{
			slot.CooldownRemaining = 0;
		}

		ResetUnitsToBase();
		RollShop();
		RollRelics();
		RollSpawnPlan();
	}

	private void RollShop()
	{
		ShopOffers.Clear();
		var pool = Data.Cards;
		for (var i = 0; i < ShopSize; i++)
		{
			ShopOffers.Add(new ShopOffer { CardName = ShopRng.Pick(pool).Name });
		}
	}

	private void RollRelics()
	{
		RelicOffers.Clear();
		foreach (var relic in Data.Relics)
		{
			RelicOffers.Add(new RelicOffer { RelicName = relic.Name, Sold = HasRelic(relic.Name) });
		}
	}

	private void RollSpawnPlan()
	{
		SpawnPlan.Clear();
		var count = Math.Min(7, 2 + Wave);
		var power = 3 + (Wave / 2);
		for (var i = 0; i < count; i++)
		{
			SpawnPlan.Add(new SpawnEntry
			{
				Col = WaveRng.NextInt(Board.Cols),
				Power = power,
				Armor = Wave >= 4 && i % 3 == 0 ? 1 : 0
			});
		}
	}

	private void DoBuyCard(int offerIndex)
	{
		if (Phase != Phase.Prep || offerIndex < 0 || offerIndex >= ShopOffers.Count)
		{
			Log("当前不能购买该卡牌。");
			return;
		}

		var offer = ShopOffers[offerIndex];
		if (offer.Sold)
		{
			return;
		}

		var card = Card(offer.CardName);
		var price = card.Value > 0 ? card.Value : FallbackCardPrice;
		if (Gold < price)
		{
			Log($"金币不足，购买 {card.Name} 需要 {price} 金。");
			return;
		}

		Gold -= price;
		offer.Sold = true;
		if (card.Kind == CardKind.Spell)
		{
			SpellInventory.Add(card.Name);
			if (SpellSlots.Count < 3)
			{
				SpellSlots.Add(new SpellSlot { CardName = card.Name });
				Log($"购买并携带法术 {card.Name}，-{price} 金。");
			}
			else
			{
				Log($"购买法术 {card.Name}，已加入法术库，-{price} 金。");
			}
		}
		else
		{
			Bench.Add(card.Name);
			Log($"购买单位 {card.Name}，加入预部署区，-{price} 金。");
		}
	}

	private void DoBuyRelic(int offerIndex)
	{
		if (Phase != Phase.Prep || offerIndex < 0 || offerIndex >= RelicOffers.Count)
		{
			Log("当前不能购买该遗物。");
			return;
		}

		var offer = RelicOffers[offerIndex];
		if (offer.Sold)
		{
			return;
		}

		var relic = Data.RelicsByName[offer.RelicName];
		if (Gold < relic.Price)
		{
			Log($"金币不足，购买 {relic.Name} 需要 {relic.Price} 金。");
			return;
		}

		Gold -= relic.Price;
		Relics.Add(relic.Name);
		offer.Sold = true;
		Log($"获得遗物 {relic.Name}: {relic.Text}");
	}

	private void DoDeployUnit(int benchIndex, int cellIndex)
	{
		if (Phase != Phase.Prep || benchIndex < 0 || benchIndex >= Bench.Count || !Board.IsValidCell(cellIndex))
		{
			Log("部署参数无效。");
			return;
		}

		var card = Card(Bench[benchIndex]);
		var row = Board.RowOf(cellIndex);
		if (!card.CanDeployAnywhere && !Board.IsPlayerRow(row))
		{
			Log("该单位只能部署到己方下两行。");
			return;
		}

		if (!Grid.IsEmpty(cellIndex))
		{
			Log("目标格已被占据。");
			return;
		}

		var unit = CreateUnit(card, cellIndex);
		Grid.Cells[cellIndex].Unit = unit;
		Bench.RemoveAt(benchIndex);
		Log($"部署 {unit.Name} 到 #{cellIndex}。");
		OnDeploy(unit);
	}

	private UnitInstance CreateUnit(CardDef card, int cellIndex)
	{
		var bonusPower = card.Name == "巫师" ? SpellSlots.Count : 0;
		return new UnitInstance
		{
			InstanceId = NextInstanceId++,
			CardName = card.Name,
			Name = card.Name,
			Side = Side.Player,
			CellIndex = cellIndex,
			BasePower = card.Power,
			BaseArmor = card.Armor,
			MaxPower = card.Power + bonusPower,
			Power = card.Power + bonusPower,
			Armor = card.Armor,
			ChargeCost = card.ChargeCost,
			Charge = card.ChargeCost > 0 ? card.ChargeCost : 0,
			CommandCooldown = 0,
			CooldownRemaining = 0
		};
	}

	private void ResetUnitsToBase()
	{
		foreach (var unit in FriendlyUnits().ToList())
		{
			var card = Card(unit.CardName);
			var bonusPower = unit.Name == "巫师" ? SpellSlots.Count : 0;
			unit.BasePower = card.Power;
			unit.BaseArmor = card.Armor;
			unit.MaxPower = card.Power + bonusPower;
			unit.Power = unit.MaxPower;
			unit.Armor = card.Armor;
			unit.Brittle = 0;
			unit.ChargeCost = card.ChargeCost;
			unit.Charge = card.ChargeCost > 0 ? card.ChargeCost : 0;
			unit.CooldownRemaining = 0;
		}
	}

	private void OnDeploy(UnitInstance unit)
	{
		switch (unit.Name)
		{
			case "浪人":
				if (Grid.AdjacentCells(unit.CellIndex).Any(i => Grid.Cells[i].Unit != null))
				{
					Log("浪人周围已有友军，部署后自毁。");
					KillCombatant(unit, "浪人部署");
				}
				break;
			case "志愿军":
				var allies = FriendlyUnits().Count(u => u.InstanceId != unit.InstanceId);
				if (allies > 0)
				{
					BuffUnit(unit, allies, "志愿军");
				}
				break;
		}
	}

	private void DoMoveUnit(int src, int dst, bool prepMove)
	{
		if (prepMove && Phase != Phase.Prep)
		{
			Log("只有整备回合可以调位。");
			return;
		}

		if (!Board.IsValidCell(src) || !Board.IsValidCell(dst))
		{
			Log("移动参数无效。");
			return;
		}

		var unit = Grid.Cells[src].Unit;
		if (unit == null)
		{
			Log("源格没有己方单位。");
			return;
		}

		if (!Grid.IsEmpty(dst))
		{
			Log("目标格已被占据。");
			return;
		}

		if (prepMove && !Card(unit.CardName).CanDeployAnywhere && !Board.IsPlayerRow(Board.RowOf(dst)))
		{
			Log("该单位只能移动到己方下两行。");
			return;
		}

		Grid.Cells[src].Unit = null;
		Grid.Cells[dst].Unit = unit;
		unit.CellIndex = dst;
		Log($"移动 {unit.Name}: #{src} -> #{dst}。");
	}

	private void DoDisbandUnit(int cellIndex)
	{
		if (Phase != Phase.Prep || !Board.IsValidCell(cellIndex))
		{
			Log("当前不能撤回单位。");
			return;
		}

		var unit = Grid.Cells[cellIndex].Unit;
		if (unit == null)
		{
			Log("该格没有己方单位。");
			return;
		}

		Grid.Cells[cellIndex].Unit = null;
		Bench.Add(unit.CardName);
		Log($"撤回 {unit.Name} 到预部署区。");
	}

	private void DoStartBattle()
	{
		if (Phase != Phase.Prep)
		{
			Log("当前不能开始小局。");
			return;
		}

		Phase = Phase.Battle;
		BattleTurn = 1;
		ResetUnitsToBase();
		foreach (var unit in FriendlyUnits().ToList())
		{
			OnDeploy(unit);
		}

		SpawnNext();
		SpawnNext();
		Log($"【第 {Wave} 波】小局开始。");
	}

	private void DoUseCommand(int sourceCell, int? targetCell)
	{
		if (Phase != Phase.Battle || !Board.IsValidCell(sourceCell))
		{
			Log("当前不能使用单位指令。");
			return;
		}

		var unit = Grid.Cells[sourceCell].Unit;
		if (unit == null)
		{
			Log("源格没有己方单位。");
			return;
		}

		if (!unit.HasChargeCommand)
		{
			Log($"{unit.Name} 没有可用的充能指令。");
			return;
		}

		if (unit.Charge < unit.ChargeCost)
		{
			Log($"{unit.Name} 充能不足。");
			return;
		}

		unit.Charge -= unit.ChargeCost;
		var repeat = HasActiveUnit("汇流") ? 2 : 1;
		for (var i = 0; i < repeat; i++)
		{
			ExecuteUnitCommand(unit, sourceCell, targetCell);
			if (unit.IsDead)
			{
				break;
			}
		}
	}

	private void ExecuteUnitCommand(UnitInstance unit, int sourceCell, int? targetCell)
	{
		switch (unit.Name)
		{
			case "弓箭手":
				DamageTargetCell(targetCell, 1, new DamageSource { Side = Side.Player, SourceUnit = unit, Name = "弓箭手" });
				break;
			case "石像":
				unit.Armor += 3;
				Log("石像获得 3 护甲。");
				break;
			case "计时器":
				foreach (var slot in SpellSlots)
				{
					if (slot.CooldownRemaining > 0)
					{
						slot.CooldownRemaining--;
					}
				}
				Log("计时器令所有法术冷却 -1。");
				break;
			case "稻草人":
				var friendly = GetFriendlyAt(targetCell);
				if (friendly != null && friendly.InstanceId != unit.InstanceId)
				{
					friendly.Charge++;
					Log($"稻草人为 {friendly.Name} 提供 1 充能。");
				}
				break;
			case "地刺":
				var enemy = GetMonsterAt(targetCell);
				if (enemy != null)
				{
					ApplyBrittle(enemy, 3, "地刺");
				}
				break;
			case "工兵":
				if (targetCell.HasValue && Board.IsValidCell(targetCell.Value))
				{
					SetSpike(targetCell.Value, 2, "工兵");
				}
				break;
			case "投石机":
				for (var i = 0; i < 2; i++)
				{
					if (!DamageTargetCell(targetCell, 1, new DamageSource { Side = Side.Player, SourceUnit = unit, Name = "投石机" }))
					{
						break;
					}
				}
				break;
			case "撞角兵":
				PushFrontUnit(sourceCell);
				break;
			case "地裂术士":
				TriggerAllSpikeCells();
				break;
			default:
				Log($"{unit.Name} 的主动指令尚未实现或无需主动使用。");
				break;
		}
	}

	private void DoCastSpell(int spellIndex, int? targetCell, int? extraCell)
	{
		if (Phase != Phase.Battle || spellIndex < 0 || spellIndex >= SpellSlots.Count)
		{
			Log("当前不能释放该法术。");
			return;
		}

		var slot = SpellSlots[spellIndex];
		var spell = Card(slot.CardName);
		if (slot.CooldownRemaining > 0)
		{
			Log($"{spell.Name} 仍在冷却:{slot.CooldownRemaining}。");
			return;
		}

		var cast = ExecuteSpell(spell, targetCell, extraCell);
		if (cast)
		{
			slot.CooldownRemaining = spell.Cooldown;
		}
	}

	private bool ExecuteSpell(CardDef spell, int? targetCell, int? extraCell)
	{
		switch (spell.Name)
		{
			case "镜像":
				return CastMirror(targetCell);
			case "调防":
				return CastReposition(targetCell, extraCell);
			case "撤编":
				return CastWithdraw(targetCell);
			case "尖刺阵":
				return CastSpikeLine(targetCell);
			case "尖刺诅咒":
				var enemy = GetMonsterAt(targetCell);
				if (enemy == null)
				{
					Log("尖刺诅咒需要敌方目标。");
					return false;
				}
				ApplyBrittle(enemy, 3, spell.Name);
				return true;
			case "岩突":
				var target = GetEntityAtRequired(targetCell, spell.Name);
				if (target == null)
				{
					return false;
				}
				var oldCell = target.CellIndex;
				var killed = DealDamage(target, 2, new DamageSource { Side = Side.Player, Name = spell.Name });
				if (killed)
				{
					foreach (var idx in Grid.RowCells(Board.RowOf(oldCell)))
					{
						SetSpike(idx, 1, "岩突致死");
					}
				}
				return true;
			case "举盾":
				var unit = GetFriendlyAt(targetCell);
				if (unit == null)
				{
					Log("举盾需要己方单位目标。");
					return false;
				}
				unit.Armor += 6;
				Log($"举盾令 {unit.Name} 获得 6 护甲。");
				var front = Grid.FrontCell(unit.CellIndex);
				if (front.HasValue)
				{
					var frontEntity = EntityAt(front.Value);
					if (frontEntity != null)
					{
						ApplyBrittle(frontEntity, 2, "举盾");
					}
				}
				return true;
			case "突刺魔法":
				var pierceTarget = GetEntityAtRequired(targetCell, spell.Name);
				if (pierceTarget == null)
				{
					return false;
				}
				var hits = Math.Min(20, pierceTarget.Brittle);
				for (var i = 0; i < hits && !pierceTarget.IsDead; i++)
				{
					DealDamage(pierceTarget, 0, new DamageSource { Side = Side.Player, Name = spell.Name });
				}
				Log($"突刺魔法结算 {hits} 次。");
				return true;
			case "碎骨收割者":
				var harvestTarget = GetEntityAtRequired(targetCell, spell.Name);
				if (harvestTarget == null || harvestTarget.Brittle <= 0)
				{
					Log("碎骨收割者需要带破甲的目标。");
					return false;
				}
				var brittle = harvestTarget.Brittle;
				var cell = harvestTarget.CellIndex;
				KillCombatant(harvestTarget, spell.Name);
				SetSpike(cell, brittle, "碎骨收割者");
				return true;
			default:
				Log($"{spell.Name} 暂无可执行效果。");
				return false;
		}
	}

	private bool CastMirror(int? targetCell)
	{
		var unit = GetFriendlyAt(targetCell);
		if (unit == null)
		{
			Log("镜像需要己方单位目标。");
			return false;
		}

		var left = Grid.LeftCell(unit.CellIndex);
		if (!left.HasValue || !Grid.IsEmpty(left.Value))
		{
			Log("目标左侧没有空格。");
			return false;
		}

		var clone = CreateUnit(Card(unit.CardName), left.Value);
		Grid.Cells[left.Value].Unit = clone;
		Log($"镜像在 #{left.Value} 生成 {clone.Name}。");
		return true;
	}

	private bool CastReposition(int? targetCell, int? extraCell)
	{
		if (!targetCell.HasValue || !extraCell.HasValue)
		{
			Log("调防需要源格和目标格。");
			return false;
		}

		if (!Grid.IsAdjacent(targetCell.Value, extraCell.Value))
		{
			Log("调防只能移动到相邻空格。");
			return false;
		}

		DoMoveUnit(targetCell.Value, extraCell.Value, prepMove: false);
		return true;
	}

	private bool CastWithdraw(int? targetCell)
	{
		var unit = GetFriendlyAt(targetCell);
		if (unit == null)
		{
			Log("撤编需要己方单位目标。");
			return false;
		}

		Grid.Cells[unit.CellIndex].Unit = null;
		Bench.Add(unit.CardName);
		Log($"撤编将 {unit.Name} 收回预部署区。");
		return true;
	}

	private bool CastSpikeLine(int? targetCell)
	{
		if (!targetCell.HasValue || !Board.IsValidCell(targetCell.Value))
		{
			Log("尖刺阵需要目标格。");
			return false;
		}

		var row = Board.RowOf(targetCell.Value);
		var col = Board.ColOf(targetCell.Value);
		for (var c = Math.Max(0, col - 1); c <= Math.Min(Board.Cols - 1, col + 1); c++)
		{
			SetSpike(Board.IndexOf(row, c), 2, "尖刺阵");
		}
		return true;
	}

	private void DoEndTurn()
	{
		if (Phase != Phase.Battle)
		{
			Log("当前不是小局。");
			return;
		}

		RunFriendlyEndTurnTriggers();
		ResolveSpikeTerrain(decrementTurns: true);
		TickCooldowns();
		EnemyPhase();
		SpawnNext();
		if (GameOver)
		{
			return;
		}

		if (!EnemyMonsters().Any() && SpawnPlan.Count == 0)
		{
			ClearWave();
			return;
		}

		BattleTurn++;
		Log($"进入小局回合 {BattleTurn}。");
	}

	private void RunFriendlyEndTurnTriggers()
	{
		foreach (var unit in FriendlyUnits().ToList())
		{
			switch (unit.Name)
			{
				case "拒马" when unit.Armor > 0:
					foreach (var idx in Grid.AdjacentCells(unit.CellIndex))
					{
						var ally = Grid.Cells[idx].Unit;
						if (ally != null)
						{
							ally.Armor++;
						}
					}
					Log("拒马为相邻单位提供护甲。");
					break;
				case "锤兵":
					var front = Grid.FrontCell(unit.CellIndex);
					if (front.HasValue)
					{
						SetSpike(front.Value, 1, "锤兵");
					}
					break;
			}
		}
	}

	private void ResolveSpikeTerrain(bool decrementTurns)
	{
		for (var idx = 0; idx < Grid.Terrain.Length; idx++)
		{
			var terrain = Grid.Terrain[idx];
			if (!terrain.HasSpike)
			{
				continue;
			}

			ApplySpikeAt(idx);
			if (decrementTurns && terrain.SpikeTurns > 0)
			{
				terrain.SpikeTurns--;
			}
		}
	}

	private void ApplySpikeAt(int idx)
	{
		var unit = Grid.Cells[idx].Unit;
		var monster = Grid.Cells[idx].Monster;
		if (unit != null)
		{
			if (HasActiveUnit("活化荆棘"))
			{
				unit.Armor++;
				Log($"{unit.Name} 在尖刺地表上获得 1 护甲。");
			}
			ApplyBrittle(unit, 1, "尖刺地表");
		}
		if (monster != null)
		{
			ApplyBrittle(monster, 1, "尖刺地表", fromSpike: true);
			if (FriendlyUnits().Any(u => u.Name == "石匠"))
			{
				DealDamage(monster, 1, new DamageSource { Side = Side.Player, Name = "石匠", IsTerrain = true, CountsForThorns = false });
			}
		}
	}

	private void TriggerAllSpikeCells()
	{
		foreach (var idx in Enumerable.Range(0, Board.CellCount).Where(i => Grid.Terrain[i].HasSpike).ToList())
		{
			ApplySpikeAt(idx);
		}
		Log("地裂术士立即结算所有尖刺地表。");
	}

	private void TickCooldowns()
	{
		foreach (var unit in FriendlyUnits())
		{
			if (unit.CooldownRemaining > 0)
			{
				unit.CooldownRemaining--;
			}
		}

		foreach (var slot in SpellSlots)
		{
			if (slot.CooldownRemaining > 0)
			{
				slot.CooldownRemaining--;
			}
		}
	}

	private void EnemyPhase()
	{
		for (var row = Board.Rows - 1; row >= 0; row--)
		{
			for (var col = 0; col < Board.Cols; col++)
			{
				var idx = Board.IndexOf(row, col);
				var monster = Grid.Cells[idx].Monster;
				if (monster != null && monster.CellIndex == idx)
				{
					AdvanceMonster(idx);
				}
			}
		}
	}

	private void AdvanceMonster(int idx)
	{
		var monster = Grid.Cells[idx].Monster;
		if (monster == null)
		{
			return;
		}

		var dst = idx + Board.Cols;
		if (dst >= Board.CellCount)
		{
			Grid.Cells[idx].Monster = null;
			CityHp--;
			Log($"怪物 {monster.Name} 入侵城市，城市完整度 -1。");
			if (CityHp <= 0)
			{
				EndGame(won: false);
			}
			return;
		}

		var blocker = Grid.Cells[dst].Unit;
		if (blocker != null)
		{
			ResolveClash(blocker, monster);
			if (!monster.IsDead && Grid.Cells[dst].Unit == null && Grid.Cells[idx].Monster == monster)
			{
				Grid.Cells[idx].Monster = null;
				Grid.Cells[dst].Monster = monster;
				monster.CellIndex = dst;
				Log($"{monster.Name} 击破阻挡后推进到 #{dst}。");
			}
			return;
		}

		if (Grid.Cells[dst].Monster == null)
		{
			Grid.Cells[idx].Monster = null;
			Grid.Cells[dst].Monster = monster;
			monster.CellIndex = dst;
			Log($"{monster.Name} 推进到 #{dst}。");
		}
	}

	private void ResolveClash(UnitInstance unit, MonsterInstance monster)
	{
		Log($"{unit.Name} 与 {monster.Name} 交锋。");
		var unitPower = Math.Max(0, unit.Power);
		var monsterPower = Math.Max(0, monster.Power);

		if (unit.Name == "双斧兵")
		{
			var first = Math.Max(1, unitPower / 2);
			var second = Math.Max(0, unitPower - first);
			DealDamage(monster, first, new DamageSource { Side = Side.Player, SourceUnit = unit, Name = unit.Name });
			if (!monster.IsDead && second > 0)
			{
				DealDamage(monster, second, new DamageSource { Side = Side.Player, SourceUnit = unit, Name = unit.Name });
			}
		}
		else
		{
			DealDamage(monster, unitPower, new DamageSource { Side = Side.Player, SourceUnit = unit, Name = unit.Name });
		}

		if (!unit.IsDead && monsterPower > 0)
		{
			DealDamage(unit, monsterPower, new DamageSource { Side = Side.Enemy, Name = monster.Name });
		}
	}

	private void SpawnNext()
	{
		if (SpawnPlan.Count == 0)
		{
			return;
		}

		var entry = SpawnPlan[0];
		SpawnPlan.RemoveAt(0);
		var cell = FindSpawnCell(entry.Col);
		if (!cell.HasValue)
		{
			Log("生成行已满，本只怪物延后失败。");
			return;
		}

		var monster = new MonsterInstance
		{
			InstanceId = NextInstanceId++,
			Name = $"裂隙怪{entry.Power}",
			Side = Side.Enemy,
			Power = entry.Power,
			MaxPower = entry.Power,
			Armor = entry.Armor,
			CellIndex = cell.Value
		};
		Grid.Cells[cell.Value].Monster = monster;
		Log($"生成 {monster.Name} 到 #{cell.Value}。");
	}

	private int? FindSpawnCell(int preferredCol)
	{
		for (var offset = 0; offset < Board.Cols; offset++)
		{
			var left = preferredCol - offset;
			if (left >= 0)
			{
				var idx = Board.IndexOf(0, left);
				if (Grid.IsEmpty(idx))
				{
					return idx;
				}
			}

			var right = preferredCol + offset;
			if (right < Board.Cols)
			{
				var idx = Board.IndexOf(0, right);
				if (Grid.IsEmpty(idx))
				{
					return idx;
				}
			}
		}

		return null;
	}

	private bool DamageTargetCell(int? targetCell, int amount, DamageSource source)
	{
		var target = GetEntityAtRequired(targetCell, source.Name);
		return target != null && !DealDamage(target, amount, source);
	}

	private Combatant? GetEntityAtRequired(int? targetCell, string sourceName)
	{
		if (!targetCell.HasValue || !Board.IsValidCell(targetCell.Value))
		{
			Log($"{sourceName} 需要有效目标格。");
			return null;
		}

		var target = EntityAt(targetCell.Value);
		if (target == null)
		{
			Log($"{sourceName} 的目标格没有单位。");
			return null;
		}

		return target;
	}

	private UnitInstance? GetFriendlyAt(int? cell)
	{
		return cell.HasValue && Board.IsValidCell(cell.Value) ? Grid.Cells[cell.Value].Unit : null;
	}

	private MonsterInstance? GetMonsterAt(int? cell)
	{
		return cell.HasValue && Board.IsValidCell(cell.Value) ? Grid.Cells[cell.Value].Monster : null;
	}

	private bool DealDamage(Combatant target, int amount, DamageSource source)
	{
		if (target.IsDead)
		{
			return false;
		}

		var brittleBefore = target.Brittle;
		if (source.SourceUnit?.Name == "碎颅者" && brittleBefore > 0)
		{
			BuffUnit(source.SourceUnit, 1, "碎颅者");
		}

		var bonus = brittleBefore;
		var rawDamage = Math.Max(0, amount + bonus);
		if (brittleBefore > 0)
		{
			Log($"{target.Name} 的破甲追加 {bonus} 伤害。");
			DecayBrittleOnHit(target);
		}

		var armorBefore = target.Armor;
		var absorbed = Math.Min(target.Armor, rawDamage);
		target.Armor -= absorbed;
		var hpDamage = rawDamage - absorbed;
		if (hpDamage > 0)
		{
			target.Power -= hpDamage;
		}

		if (target is UnitInstance unitTarget)
		{
			var lostArmor = Math.Max(0, armorBefore - target.Armor);
			if (unitTarget.Name == "矮人" && lostArmor > 0)
			{
				BuffUnit(unitTarget, lostArmor, "矮人失甲");
			}
			if (unitTarget.Name == "龙龟" && hpDamage > 0)
			{
				unitTarget.Armor += hpDamage;
				Log($"龙龟受伤后获得 {hpDamage} 护甲。");
			}
			if (unitTarget.Name == "稻草人" && rawDamage > 0)
			{
				unitTarget.Charge += rawDamage;
				Log($"稻草人受击获得 {rawDamage} 充能。");
			}
		}

		Log($"{source.Name} 对 {target.Name} 造成 {rawDamage} 伤害。");

		if (source.SourceUnit?.Name == "斗士" && target.Side == Side.Enemy)
		{
			ApplyBrittle(target, 2, "斗士");
		}

		if (source.Side == Side.Player && target.Side == Side.Enemy && source.CountsForThorns && HasRelic("荆棘"))
		{
			ApplyBrittle(target, 1, "荆棘");
		}

		if (target.Power <= 0)
		{
			KillCombatant(target, source.Name);
			return true;
		}

		return false;
	}

	private void ApplyBrittle(Combatant target, int amount, string source, bool fromSpike = false)
	{
		if (amount <= 0 || target.IsDead)
		{
			return;
		}

		target.Brittle += amount;
		Log($"{source} 对 {target.Name} 施加破甲 {amount}，现为 {target.Brittle}。");
		foreach (var unit in FriendlyUnits().Where(u => u.Name == "裂纹术士").ToList())
		{
			BuffUnit(unit, 1, "裂纹术士");
		}
	}

	private void DecayBrittleOnHit(Combatant target)
	{
		if (target.Brittle <= 0 || HasActiveUnit("不灭尖锥"))
		{
			return;
		}

		target.Brittle--;
		if (target.Brittle == 0)
		{
			Log($"{target.Name} 的破甲被消耗完。");
			OnBrittleLost(target);
		}
	}

	private void OnBrittleLost(Combatant target)
	{
		var row = Board.RowOf(target.CellIndex);
		foreach (var guard in FriendlyUnits().Where(u => u.Name == "坚守").ToList())
		{
			var guardRow = Board.RowOf(guard.CellIndex);
			if (guardRow != row)
			{
				continue;
			}

			foreach (var ally in FriendlyUnits().Where(u => Board.RowOf(u.CellIndex) == guardRow))
			{
				ally.Armor++;
			}
			Log("坚守因破甲消失为同行提供护甲。");
		}
	}

	private void KillCombatant(Combatant target, string source)
	{
		if (target.CellIndex < 0)
		{
			return;
		}

		var cell = target.CellIndex;
		var brittle = target.Brittle;
		if (target is UnitInstance unit)
		{
			Grid.Cells[cell].Unit = null;
			Log($"{unit.Name} 死亡。");
			if (unit.Name == "尖石魔像")
			{
				foreach (var idx in Grid.FrontCellsInColumn(cell))
				{
					SetSpike(idx, 3, "尖石魔像亡语");
				}
			}
		}
		else
		{
			Grid.Cells[cell].Monster = null;
			Gold++;
			Log($"{target.Name} 死亡，金币 +1。");
		}

		target.CellIndex = -1;
		if (brittle > 0)
		{
			foreach (var spike in FriendlyUnits().Where(u => u.Name == "地刺").ToList())
			{
				spike.Charge++;
				Log("地刺因破甲单位死亡获得 1 充能。");
			}

			foreach (var totem in FriendlyUnits().Where(u => u.Name == "碎骨图腾").ToList())
			{
				SetSpike(cell, 2, "碎骨图腾");
			}

			if (HasRelic("厄运"))
			{
				var enemies = EnemyMonsters().ToList();
				if (enemies.Count > 0)
				{
					var receiver = EffectRng.Pick(enemies);
					ApplyBrittle(receiver, brittle, "厄运");
				}
			}
		}

		foreach (var comrade in FriendlyUnits().Where(u => u.Name == "同袍").ToList())
		{
			var row = Board.RowOf(comrade.CellIndex);
			foreach (var ally in FriendlyUnits().Where(u => Board.RowOf(u.CellIndex) == row))
			{
				ally.Armor++;
			}
			Log("同袍因单位死亡为同行提供护甲。");
		}
	}

	private void BuffUnit(UnitInstance unit, int amount, string source)
	{
		if (amount <= 0 || unit.IsDead)
		{
			return;
		}

		unit.MaxPower += amount;
		unit.Power += amount;
		Log($"{source} 令 {unit.Name} 增益 {amount}。");
	}

	private void SetSpike(int idx, int turns, string source)
	{
		if (!Board.IsValidCell(idx) || turns <= 0)
		{
			return;
		}

		Grid.Terrain[idx].SpikeTurns = Math.Max(Grid.Terrain[idx].SpikeTurns, turns);
		Log($"{source} 在 #{idx} 生成尖刺地表 {turns} 回合。");
	}

	private bool HasActiveUnit(string name) => FriendlyUnits().Any(u => u.Name == name);

	private void PushFrontUnit(int sourceCell)
	{
		var front = Grid.FrontCell(sourceCell);
		if (!front.HasValue)
		{
			Log("撞角兵面前没有格子。");
			return;
		}

		var target = EntityAt(front.Value);
		if (target == null)
		{
			Log("撞角兵面前没有单位。");
			return;
		}

		var dst = Grid.FrontCell(front.Value);
		if (!dst.HasValue || !Grid.IsEmpty(dst.Value))
		{
			Log("目标无法被击退。");
			return;
		}

		if (target is UnitInstance unit)
		{
			Grid.Cells[front.Value].Unit = null;
			Grid.Cells[dst.Value].Unit = unit;
			unit.CellIndex = dst.Value;
		}
		else if (target is MonsterInstance monster)
		{
			Grid.Cells[front.Value].Monster = null;
			Grid.Cells[dst.Value].Monster = monster;
			monster.CellIndex = dst.Value;
		}

		Log($"撞角兵将 {target.Name} 击退到 #{dst.Value}。");
	}

	private void ClearWave()
	{
		Log($"第 {Wave} 波清空。");
		Wave++;
		EnterPrep(giveIncome: true);
	}

	private void EndGame(bool won)
	{
		GameOver = true;
		Won = won;
		Phase = Phase.Ended;
		Log(won ? "胜利:成功抵御所有波次。" : "失败:城市完整度归零。");
	}
}
