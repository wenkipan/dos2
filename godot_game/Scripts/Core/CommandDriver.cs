namespace BreakerGrid.Core;

public static class CommandDriver
{
	public static string Execute(GameData data, string savePath, string commandText)
	{
		var commands = commandText.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		GameSession? game = null;
		var output = new List<string>();

		foreach (var command in commands)
		{
			var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			if (parts.Length == 0)
			{
				continue;
			}

			var op = parts[0];
			if (op == "new")
			{
				var seed = parts.Length > 1 ? int.Parse(parts[1]) : (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
				game = GameSession.New(data, seed);
				output.Add($"[新开一局 seed={seed}]");
				continue;
			}

			game ??= GameSerializer.Load(data, savePath);
			if (game == null)
			{
				return "没有存档，请先执行: new [seed]";
			}

			switch (op)
			{
				case "view":
					break;
				case "buy":
					game.BuyCard(Int(parts, 1));
					break;
				case "relic":
					game.BuyRelic(Int(parts, 1));
					break;
				case "deploy":
					game.DeployUnit(Int(parts, 1), Int(parts, 2));
					break;
				case "move":
					game.MoveUnit(Int(parts, 1), Int(parts, 2));
					break;
				case "disband":
					game.DisbandUnit(Int(parts, 1));
					break;
				case "start":
					game.StartBattle();
					break;
				case "cmd":
					game.UseCommand(Int(parts, 1), MaybeInt(parts, 2));
					break;
				case "spell":
					game.CastSpell(Int(parts, 1), MaybeInt(parts, 2), MaybeInt(parts, 3));
					break;
				case "end":
					game.EndTurn();
					break;
				case "pass":
					Pass(game, parts.Length > 1 ? int.Parse(parts[1]) : 99);
					break;
				default:
					output.Add($"未知命令:{command}");
					break;
			}
		}

		game ??= GameSerializer.Load(data, savePath);
		if (game == null)
		{
			return string.Join('\n', output);
		}

		GameSerializer.Save(game, savePath);
		output.Add(GameRenderer.Render(game));
		return string.Join('\n', output);
	}

	private static void Pass(GameSession game, int turns)
	{
		var did = 0;
		for (; did < turns && game.Phase == Phase.Battle && !game.GameOver; did++)
		{
			var hp = game.CityHp;
			var wave = game.Wave;
			game.EndTurn();
			if (game.CityHp < hp || game.Wave != wave || game.GameOver)
			{
				break;
			}
		}
		game.Log($"pass 自动结束 {did + 1} 个回合。");
	}

	private static int Int(string[] parts, int index)
	{
		if (index >= parts.Length)
		{
			throw new ArgumentException("命令参数不足。");
		}
		return int.Parse(parts[index]);
	}

	private static int? MaybeInt(string[] parts, int index)
	{
		return index < parts.Length ? int.Parse(parts[index]) : null;
	}
}
