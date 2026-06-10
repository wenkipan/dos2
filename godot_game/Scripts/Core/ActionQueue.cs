namespace BreakerGrid.Core;

public interface IGameAction
{
	string Name { get; }
	void Execute(GameSession game);
}

public sealed class DelegateAction : IGameAction
{
	private readonly Action<GameSession> _execute;

	public string Name { get; }

	public DelegateAction(string name, Action<GameSession> execute)
	{
		Name = name;
		_execute = execute;
	}

	public void Execute(GameSession game)
	{
		_execute(game);
	}
}

public sealed class ActionQueue
{
	private readonly Queue<IGameAction> _actions = new();

	public int Count => _actions.Count;

	public void Enqueue(IGameAction action)
	{
		_actions.Enqueue(action);
	}

	public void ResolveAll(GameSession game)
	{
		while (_actions.Count > 0 && !game.GameOver)
		{
			var action = _actions.Dequeue();
			action.Execute(game);
		}
	}
}
