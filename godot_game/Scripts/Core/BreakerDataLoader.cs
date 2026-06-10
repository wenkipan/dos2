using System.Text.RegularExpressions;

namespace BreakerGrid.Core;

public static partial class BreakerDataLoader
{
	private static readonly HashSet<string> IgnoredTerms = new(StringComparer.Ordinal)
	{
		"破甲X",
		"尖刺地表",
		"遗物"
	};

	public static GameData Load(string csvPath)
	{
		var rows = CsvTable.Read(csvPath);
		if (rows.Count == 0)
		{
			throw new InvalidOperationException($"空数据表:{csvPath}");
		}

		var header = rows[0];
		var termIndex = Array.FindIndex(header, h => h == "词条");
		var descIndex = Array.FindIndex(header, h => h == "描述");
		var data = new GameData();

		foreach (var row in rows.Skip(1))
		{
			var name = Get(row, 5);
			if (!string.IsNullOrWhiteSpace(name))
			{
				var power = ParseInt(Get(row, 6));
				var type = Get(row, 4);
				var text = Get(row, 8);
				var kind = power > 0 ? CardKind.Unit : CardKind.Spell;
				var card = new CardDef
				{
					Id = name,
					Name = name,
					Kind = kind,
					Type = type,
					Rarity = Get(row, 2),
					Value = ParseInt(Get(row, 3)),
					Power = power,
					Armor = ParseInt(Get(row, 7)),
					ChargeCost = ParseChargeCost(text),
					Cooldown = ParseCooldown(text),
					CanDeployAnywhere = text.Contains("任意一格", StringComparison.Ordinal),
					Text = text
				};
				data.Cards.Add(card);
			}

			if (termIndex >= 0 && descIndex >= 0)
			{
				var term = Get(row, termIndex);
				var desc = Get(row, descIndex);
				if (!string.IsNullOrWhiteSpace(term) && !string.IsNullOrWhiteSpace(desc) && !IgnoredTerms.Contains(term))
				{
					data.Relics.Add(new RelicDef
					{
						Id = term,
						Name = term,
						Text = desc,
						Price = term == "厄运" ? 7 : 6
					});
				}
			}
		}

		data.Index();
		return data;
	}

	private static string Get(string[] row, int index)
	{
		return index >= 0 && index < row.Length ? row[index].Trim() : "";
	}

	private static int ParseInt(string raw)
	{
		return int.TryParse(raw, out var value) ? value : 0;
	}

	private static int ParseCooldown(string text)
	{
		var match = CooldownRegex().Match(text);
		return match.Success ? int.Parse(match.Groups[1].Value) : 0;
	}

	private static int ParseChargeCost(string text)
	{
		var match = ChargeRegex().Match(text);
		if (!match.Success)
		{
			return 0;
		}

		var amount = match.Groups[1].Value;
		return string.IsNullOrWhiteSpace(amount) ? 1 : int.Parse(amount);
	}

	[GeneratedRegex("冷却(\\d+)")]
	private static partial Regex CooldownRegex();

	[GeneratedRegex("充能(\\d*)[:：]")]
	private static partial Regex ChargeRegex();
}
