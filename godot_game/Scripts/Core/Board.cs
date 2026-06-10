namespace BreakerGrid.Core;

public sealed class Board
{
	public const int Rows = 5;
	public const int Cols = 6;
	public const int CellCount = Rows * Cols;
	public static readonly int[] PlayerRows = [3, 4];

	public CellState[] Cells { get; } = new CellState[CellCount];
	public TerrainState[] Terrain { get; } = new TerrainState[CellCount];

	public Board()
	{
		for (var i = 0; i < CellCount; i++)
		{
			Cells[i] = new CellState();
			Terrain[i] = new TerrainState();
		}
	}

	public static bool IsValidCell(int idx) => idx >= 0 && idx < CellCount;
	public static int RowOf(int idx) => idx / Cols;
	public static int ColOf(int idx) => idx % Cols;
	public static int IndexOf(int row, int col) => row * Cols + col;
	public static bool IsPlayerRow(int row) => PlayerRows.Contains(row);

	public bool IsEmpty(int idx)
	{
		return IsValidCell(idx) && Cells[idx].Unit == null && Cells[idx].Monster == null;
	}

	public bool IsAdjacent(int a, int b)
	{
		if (!IsValidCell(a) || !IsValidCell(b))
		{
			return false;
		}

		var ar = RowOf(a);
		var ac = ColOf(a);
		var br = RowOf(b);
		var bc = ColOf(b);
		return Math.Abs(ar - br) + Math.Abs(ac - bc) == 1;
	}

	public IEnumerable<int> AdjacentCells(int idx)
	{
		var row = RowOf(idx);
		var col = ColOf(idx);
		if (row > 0)
		{
			yield return IndexOf(row - 1, col);
		}
		if (row < Rows - 1)
		{
			yield return IndexOf(row + 1, col);
		}
		if (col > 0)
		{
			yield return IndexOf(row, col - 1);
		}
		if (col < Cols - 1)
		{
			yield return IndexOf(row, col + 1);
		}
	}

	public IEnumerable<int> RowCells(int row)
	{
		for (var col = 0; col < Cols; col++)
		{
			yield return IndexOf(row, col);
		}
	}

	public IEnumerable<int> FrontCellsInColumn(int idx)
	{
		var col = ColOf(idx);
		for (var row = RowOf(idx) - 1; row >= 0; row--)
		{
			yield return IndexOf(row, col);
		}
	}

	public int? FrontCell(int idx)
	{
		var row = RowOf(idx);
		if (row <= 0)
		{
			return null;
		}
		return IndexOf(row - 1, ColOf(idx));
	}

	public int? BehindCell(int idx)
	{
		var row = RowOf(idx);
		if (row >= Rows - 1)
		{
			return null;
		}
		return IndexOf(row + 1, ColOf(idx));
	}

	public int? LeftCell(int idx)
	{
		var col = ColOf(idx);
		if (col <= 0)
		{
			return null;
		}
		return idx - 1;
	}
}
