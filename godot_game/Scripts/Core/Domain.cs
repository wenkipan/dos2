namespace BreakerGrid.Core;

public enum Phase
{
	Prep,
	Battle,
	Ended
}

public enum CardKind
{
	Unit,
	Spell
}

public enum Side
{
	Player,
	Enemy
}

public sealed class CardDef
{
	public string Id { get; init; } = "";
	public string Name { get; init; } = "";
	public CardKind Kind { get; init; }
	public string Type { get; init; } = "";
	public string Rarity { get; init; } = "";
	public int Value { get; init; }
	public int Power { get; init; }
	public int Armor { get; init; }
	public int ChargeCost { get; init; }
	public int Cooldown { get; init; }
	public bool CanDeployAnywhere { get; init; }
	public string Text { get; init; } = "";
}

public sealed class RelicDef
{
	public string Id { get; init; } = "";
	public string Name { get; init; } = "";
	public string Text { get; init; } = "";
	public int Price { get; init; } = 6;
}

public sealed class GameData
{
	public List<CardDef> Cards { get; } = new();
	public List<RelicDef> Relics { get; } = new();
	public Dictionary<string, CardDef> CardsByName { get; } = new();
	public Dictionary<string, RelicDef> RelicsByName { get; } = new();

	public IReadOnlyList<CardDef> UnitCards => Cards.Where(c => c.Kind == CardKind.Unit).ToList();
	public IReadOnlyList<CardDef> SpellCards => Cards.Where(c => c.Kind == CardKind.Spell).ToList();

	public void Index()
	{
		CardsByName.Clear();
		foreach (var card in Cards)
		{
			CardsByName[card.Name] = card;
		}

		RelicsByName.Clear();
		foreach (var relic in Relics)
		{
			RelicsByName[relic.Name] = relic;
		}
	}
}

public sealed class TerrainState
{
	public int SpikeTurns { get; set; }

	public bool HasSpike => SpikeTurns > 0;
}

public sealed class CellState
{
	public UnitInstance? Unit { get; set; }
	public MonsterInstance? Monster { get; set; }
}

public abstract class Combatant
{
	public int InstanceId { get; set; }
	public string Name { get; set; } = "";
	public Side Side { get; set; }
	public int Power { get; set; }
	public int MaxPower { get; set; }
	public int Armor { get; set; }
	public int Brittle { get; set; }
	public int CellIndex { get; set; }
	public bool IsDead => Power <= 0;
}

public sealed class UnitInstance : Combatant
{
	public string CardName { get; set; } = "";
	public int BasePower { get; set; }
	public int BaseArmor { get; set; }
	public int Charge { get; set; }
	public int ChargeCost { get; set; }
	public int CommandCooldown { get; set; }
	public int CooldownRemaining { get; set; }

	public bool HasChargeCommand => ChargeCost > 0;
	public bool HasCooldownCommand => CommandCooldown > 0;
}

public sealed class MonsterInstance : Combatant
{
	public string MonsterId { get; set; } = "breaker_basic";
}

public sealed class SpellSlot
{
	public string CardName { get; set; } = "";
	public int CooldownRemaining { get; set; }
}

public sealed class ShopOffer
{
	public string CardName { get; set; } = "";
	public bool Sold { get; set; }
}

public sealed class RelicOffer
{
	public string RelicName { get; set; } = "";
	public bool Sold { get; set; }
}

public sealed class SpawnEntry
{
	public int Col { get; set; }
	public int Power { get; set; }
	public int Armor { get; set; }
}

public sealed class DamageSource
{
	public Side Side { get; init; }
	public UnitInstance? SourceUnit { get; init; }
	public string Name { get; init; } = "";
	public bool IsTerrain { get; init; }
	public bool CountsForThorns { get; init; } = true;
}
