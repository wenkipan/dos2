using System.Text;

namespace BreakerGrid.Core;

public static class GameRenderer
{
	public static string Render(GameSession game)
	{
		var sb = new StringBuilder();
		var phase = game.Phase switch
		{
			Phase.Prep => "整备",
			Phase.Battle => $"小局·回合{game.BattleTurn}",
			_ => game.Won ? "已胜利" : "已失败"
		};

		sb.AppendLine("================ Breaker ================");
		sb.AppendLine($"城市 {game.CityHp}/{GameSession.CityHpMax} | 波次 {game.Wave}/{GameSession.WinWave} | 金币 {game.Gold} | {phase}");
		RenderBoard(game, sb);
		RenderSpawnPlan(game, sb);
		RenderBench(game, sb);
		RenderSpells(game, sb);
		RenderShop(game, sb);
		RenderRelics(game, sb);
		sb.AppendLine("-- 日志 --");
		foreach (var line in game.LogLines)
		{
			sb.AppendLine($"  {line}");
		}
		sb.AppendLine("=========================================");
		return sb.ToString();
	}

	private static void RenderBoard(GameSession game, StringBuilder sb)
	{
		sb.AppendLine("-- 棋盘 --");
		sb.Append("       ");
		for (var col = 0; col < Board.Cols; col++)
		{
			sb.Append($"c{col,-15}");
		}
		sb.AppendLine();

		for (var row = 0; row < Board.Rows; row++)
		{
			var tag = Board.IsPlayerRow(row) ? "玩" : "敌";
			sb.Append($"r{row} {tag} | ");
			for (var col = 0; col < Board.Cols; col++)
			{
				var idx = Board.IndexOf(row, col);
				sb.Append($"{CellString(game, idx),-17}");
			}
			sb.AppendLine();
		}
	}

	private static string CellString(GameSession game, int idx)
	{
		var cell = game.Grid.Cells[idx];
		var text = $"#{idx}:";
		if (cell.Monster != null)
		{
			text += $"怪{cell.Monster.Power}";
			if (cell.Monster.Armor > 0)
			{
				text += $"甲{cell.Monster.Armor}";
			}
			if (cell.Monster.Brittle > 0)
			{
				text += $"破{cell.Monster.Brittle}";
			}
		}
		else if (cell.Unit != null)
		{
			var unit = cell.Unit;
			text += $"{Short(unit.Name)}{unit.Power}";
			if (unit.Armor > 0)
			{
				text += $"甲{unit.Armor}";
			}
			if (unit.ChargeCost > 0)
			{
				text += $"充{unit.Charge}";
			}
			if (unit.Brittle > 0)
			{
				text += $"破{unit.Brittle}";
			}
		}

		if (game.Grid.Terrain[idx].SpikeTurns > 0)
		{
			text += $"{{刺{game.Grid.Terrain[idx].SpikeTurns}}}";
		}

		return text;
	}

	private static string Short(string name)
	{
		return name.Length <= 2 ? name : name[..2];
	}

	private static void RenderSpawnPlan(GameSession game, StringBuilder sb)
	{
		if (game.SpawnPlan.Count == 0)
		{
			sb.AppendLine("-- 剩余生成:(无) --");
			return;
		}

		var plan = string.Join(" -> ", game.SpawnPlan.Select(s => $"c{s.Col}/战{s.Power}"));
		sb.AppendLine($"-- 剩余生成:{plan} --");
	}

	private static void RenderBench(GameSession game, StringBuilder sb)
	{
		sb.AppendLine("-- 预部署区: deploy <序号> <格号> --");
		for (var i = 0; i < game.Bench.Count; i++)
		{
			var card = game.Card(game.Bench[i]);
			sb.AppendLine($"  {i}: {card.Name} 战{card.Power} 甲{card.Armor} {card.Text}");
		}
	}

	private static void RenderSpells(GameSession game, StringBuilder sb)
	{
		sb.AppendLine("-- 携带法术: spell <槽位> <目标格> [额外格] --");
		for (var i = 0; i < game.SpellSlots.Count; i++)
		{
			var slot = game.SpellSlots[i];
			var card = game.Card(slot.CardName);
			var cd = slot.CooldownRemaining > 0 ? $"冷却{slot.CooldownRemaining}" : "就绪";
			sb.AppendLine($"  {i}: {card.Name} [{cd}] {card.Text}");
		}
	}

	private static void RenderShop(GameSession game, StringBuilder sb)
	{
		sb.AppendLine("-- 商店: buy <序号> --");
		for (var i = 0; i < game.ShopOffers.Count; i++)
		{
			var offer = game.ShopOffers[i];
			var card = game.Card(offer.CardName);
			var price = card.Value > 0 ? card.Value : GameSession.FallbackCardPrice;
			var sold = offer.Sold ? "已售" : $"{price}金";
			sb.AppendLine($"  {i}: {card.Name} ({sold}) {card.Text}");
		}
	}

	private static void RenderRelics(GameSession game, StringBuilder sb)
	{
		sb.AppendLine($"-- 已有遗物: {(game.Relics.Count == 0 ? "无" : string.Join(", ", game.Relics))} --");
		if (game.RelicOffers.Count == 0)
		{
			return;
		}

		sb.AppendLine("-- 遗物商店: relic <序号> --");
		for (var i = 0; i < game.RelicOffers.Count; i++)
		{
			var offer = game.RelicOffers[i];
			var relic = game.Data.RelicsByName[offer.RelicName];
			var sold = offer.Sold ? "已拥有" : $"{relic.Price}金";
			sb.AppendLine($"  {i}: {relic.Name} ({sold}) {relic.Text}");
		}
	}
}
