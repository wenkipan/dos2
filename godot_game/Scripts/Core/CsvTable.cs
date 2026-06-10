using System.Text;

namespace BreakerGrid.Core;

public static class CsvTable
{
	public static List<string[]> Read(string path)
	{
		var rows = new List<string[]>();
		foreach (var line in File.ReadLines(path, Encoding.UTF8))
		{
			rows.Add(ParseLine(line).ToArray());
		}
		return rows;
	}

	private static List<string> ParseLine(string line)
	{
		var cells = new List<string>();
		var cell = new StringBuilder();
		var inQuote = false;

		for (var i = 0; i < line.Length; i++)
		{
			var ch = line[i];
			if (ch == '"')
			{
				if (inQuote && i + 1 < line.Length && line[i + 1] == '"')
				{
					cell.Append('"');
					i++;
				}
				else
				{
					inQuote = !inQuote;
				}
				continue;
			}

			if (ch == ',' && !inQuote)
			{
				cells.Add(cell.ToString());
				cell.Clear();
			}
			else
			{
				cell.Append(ch);
			}
		}

		cells.Add(cell.ToString());
		return cells;
	}
}
