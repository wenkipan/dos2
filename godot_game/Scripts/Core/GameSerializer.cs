using System.Text.Json;

namespace BreakerGrid.Core;

public static class GameSerializer
{
	private static readonly JsonSerializerOptions Options = new()
	{
		WriteIndented = true,
		IncludeFields = true
	};

	public static void Save(GameSession game, string path)
	{
		var save = ToSave(game);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, JsonSerializer.Serialize(save, Options));
	}

	public static GameSession? Load(GameData data, string path)
	{
		if (!File.Exists(path))
		{
			return null;
		}

		var save = JsonSerializer.Deserialize<GameSave>(File.ReadAllText(path), Options);
		return save == null ? null : FromSave(data, save);
	}

	public static GameSave ToSave(GameSession game)
	{
		var save = new GameSave
		{
			Phase = game.Phase,
			Seed = game.Seed,
			Gold = game.Gold,
			CityHp = game.CityHp,
			Wave = game.Wave,
			BattleTurn = game.BattleTurn,
			NextInstanceId = game.NextInstanceId,
			GameOver = game.GameOver,
			Won = game.Won,
			ShopRngState = game.ShopRng.State,
			WaveRngState = game.WaveRng.State,
			EffectRngState = game.EffectRng.State,
			Bench = game.Bench.ToList(),
			SpellInventory = game.SpellInventory.ToList(),
			Relics = game.Relics.ToList(),
			SpellSlots = game.SpellSlots.Select(s => new SpellSlotSave
			{
				CardName = s.CardName,
				CooldownRemaining = s.CooldownRemaining
			}).ToList(),
			ShopOffers = game.ShopOffers.Select(s => new ShopOfferSave
			{
				CardName = s.CardName,
				Sold = s.Sold
			}).ToList(),
			RelicOffers = game.RelicOffers.Select(r => new RelicOfferSave
			{
				RelicName = r.RelicName,
				Sold = r.Sold
			}).ToList(),
			SpawnPlan = game.SpawnPlan.Select(s => new SpawnEntrySave
			{
				Col = s.Col,
				Power = s.Power,
				Armor = s.Armor
			}).ToList(),
			LogLines = game.LogLines.ToList()
		};

		for (var i = 0; i < Board.CellCount; i++)
		{
			var cell = game.Grid.Cells[i];
			save.Cells.Add(new CellSave
			{
				Unit = cell.Unit == null ? null : UnitToSave(cell.Unit),
				Monster = cell.Monster == null ? null : MonsterToSave(cell.Monster),
				SpikeTurns = game.Grid.Terrain[i].SpikeTurns
			});
		}

		return save;
	}

	public static GameSession FromSave(GameData data, GameSave save)
	{
		var game = new GameSession(data, save.Seed)
		{
			Phase = save.Phase,
			Gold = save.Gold,
			CityHp = save.CityHp,
			Wave = save.Wave,
			BattleTurn = save.BattleTurn,
			NextInstanceId = save.NextInstanceId,
			GameOver = save.GameOver,
			Won = save.Won,
			ShopRng = new GameRng(save.ShopRngState),
			WaveRng = new GameRng(save.WaveRngState),
			EffectRng = new GameRng(save.EffectRngState)
		};

		game.Bench.AddRange(save.Bench);
		game.SpellInventory.AddRange(save.SpellInventory);
		game.Relics.AddRange(save.Relics);
		game.LogLines.AddRange(save.LogLines);
		foreach (var slot in save.SpellSlots)
		{
			game.SpellSlots.Add(new SpellSlot
			{
				CardName = slot.CardName,
				CooldownRemaining = slot.CooldownRemaining
			});
		}
		foreach (var offer in save.ShopOffers)
		{
			game.ShopOffers.Add(new ShopOffer
			{
				CardName = offer.CardName,
				Sold = offer.Sold
			});
		}
		foreach (var offer in save.RelicOffers)
		{
			game.RelicOffers.Add(new RelicOffer
			{
				RelicName = offer.RelicName,
				Sold = offer.Sold
			});
		}
		foreach (var spawn in save.SpawnPlan)
		{
			game.SpawnPlan.Add(new SpawnEntry
			{
				Col = spawn.Col,
				Power = spawn.Power,
				Armor = spawn.Armor
			});
		}

		for (var i = 0; i < Math.Min(Board.CellCount, save.Cells.Count); i++)
		{
			var cell = save.Cells[i];
			game.Grid.Terrain[i].SpikeTurns = cell.SpikeTurns;
			if (cell.Unit != null)
			{
				game.Grid.Cells[i].Unit = UnitFromSave(cell.Unit);
			}
			if (cell.Monster != null)
			{
				game.Grid.Cells[i].Monster = MonsterFromSave(cell.Monster);
			}
		}

		return game;
	}

	private static UnitSave UnitToSave(UnitInstance unit)
	{
		return new UnitSave
		{
			InstanceId = unit.InstanceId,
			CardName = unit.CardName,
			Name = unit.Name,
			Power = unit.Power,
			MaxPower = unit.MaxPower,
			BasePower = unit.BasePower,
			BaseArmor = unit.BaseArmor,
			Armor = unit.Armor,
			Brittle = unit.Brittle,
			CellIndex = unit.CellIndex,
			Charge = unit.Charge,
			ChargeCost = unit.ChargeCost,
			CommandCooldown = unit.CommandCooldown,
			CooldownRemaining = unit.CooldownRemaining
		};
	}

	private static MonsterSave MonsterToSave(MonsterInstance monster)
	{
		return new MonsterSave
		{
			InstanceId = monster.InstanceId,
			Name = monster.Name,
			Power = monster.Power,
			MaxPower = monster.MaxPower,
			Armor = monster.Armor,
			Brittle = monster.Brittle,
			CellIndex = monster.CellIndex,
			MonsterId = monster.MonsterId
		};
	}

	private static UnitInstance UnitFromSave(UnitSave save)
	{
		return new UnitInstance
		{
			InstanceId = save.InstanceId,
			CardName = save.CardName,
			Name = save.Name,
			Side = Side.Player,
			Power = save.Power,
			MaxPower = save.MaxPower,
			BasePower = save.BasePower,
			BaseArmor = save.BaseArmor,
			Armor = save.Armor,
			Brittle = save.Brittle,
			CellIndex = save.CellIndex,
			Charge = save.Charge,
			ChargeCost = save.ChargeCost,
			CommandCooldown = save.CommandCooldown,
			CooldownRemaining = save.CooldownRemaining
		};
	}

	private static MonsterInstance MonsterFromSave(MonsterSave save)
	{
		return new MonsterInstance
		{
			InstanceId = save.InstanceId,
			Name = save.Name,
			Side = Side.Enemy,
			Power = save.Power,
			MaxPower = save.MaxPower,
			Armor = save.Armor,
			Brittle = save.Brittle,
			CellIndex = save.CellIndex,
			MonsterId = save.MonsterId
		};
	}
}

public sealed class GameSave
{
	public Phase Phase { get; set; }
	public int Seed { get; set; }
	public int Gold { get; set; }
	public int CityHp { get; set; }
	public int Wave { get; set; }
	public int BattleTurn { get; set; }
	public int NextInstanceId { get; set; }
	public bool GameOver { get; set; }
	public bool Won { get; set; }
	public ulong ShopRngState { get; set; }
	public ulong WaveRngState { get; set; }
	public ulong EffectRngState { get; set; }
	public List<string> Bench { get; set; } = new();
	public List<string> SpellInventory { get; set; } = new();
	public List<string> Relics { get; set; } = new();
	public List<SpellSlotSave> SpellSlots { get; set; } = new();
	public List<ShopOfferSave> ShopOffers { get; set; } = new();
	public List<RelicOfferSave> RelicOffers { get; set; } = new();
	public List<SpawnEntrySave> SpawnPlan { get; set; } = new();
	public List<CellSave> Cells { get; set; } = new();
	public List<string> LogLines { get; set; } = new();
}

public sealed class CellSave
{
	public UnitSave? Unit { get; set; }
	public MonsterSave? Monster { get; set; }
	public int SpikeTurns { get; set; }
}

public sealed class UnitSave
{
	public int InstanceId { get; set; }
	public string CardName { get; set; } = "";
	public string Name { get; set; } = "";
	public int Power { get; set; }
	public int MaxPower { get; set; }
	public int BasePower { get; set; }
	public int BaseArmor { get; set; }
	public int Armor { get; set; }
	public int Brittle { get; set; }
	public int CellIndex { get; set; }
	public int Charge { get; set; }
	public int ChargeCost { get; set; }
	public int CommandCooldown { get; set; }
	public int CooldownRemaining { get; set; }
}

public sealed class MonsterSave
{
	public int InstanceId { get; set; }
	public string Name { get; set; } = "";
	public int Power { get; set; }
	public int MaxPower { get; set; }
	public int Armor { get; set; }
	public int Brittle { get; set; }
	public int CellIndex { get; set; }
	public string MonsterId { get; set; } = "";
}

public sealed class SpellSlotSave
{
	public string CardName { get; set; } = "";
	public int CooldownRemaining { get; set; }
}

public sealed class ShopOfferSave
{
	public string CardName { get; set; } = "";
	public bool Sold { get; set; }
}

public sealed class RelicOfferSave
{
	public string RelicName { get; set; } = "";
	public bool Sold { get; set; }
}

public sealed class SpawnEntrySave
{
	public int Col { get; set; }
	public int Power { get; set; }
	public int Armor { get; set; }
}
