namespace BreakerGrid.Core;

public sealed class GameRng
{
	public ulong State { get; set; }

	public GameRng(ulong seed)
	{
		State = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
	}

	public int NextInt(int maxExclusive)
	{
		if (maxExclusive <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(maxExclusive));
		}

		return (int)(NextUInt64() % (uint)maxExclusive);
	}

	public T Pick<T>(IReadOnlyList<T> items)
	{
		if (items.Count == 0)
		{
			throw new InvalidOperationException("无法从空列表随机。");
		}

		return items[NextInt(items.Count)];
	}

	private ulong NextUInt64()
	{
		var x = State;
		x ^= x << 7;
		x ^= x >> 9;
		x ^= x << 8;
		State = x;
		return x;
	}
}
