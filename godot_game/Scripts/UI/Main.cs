using BreakerGrid.Core;
using Godot;

namespace BreakerGrid.UI;

public partial class Main : Control
{
	private enum SelectionKind
	{
		None,
		Bench,
		MoveUnit,
		Command,
		Spell
	}

	private static readonly Color Bg = new(0.075f, 0.083f, 0.095f);
	private static readonly Color Panel = new(0.115f, 0.128f, 0.145f);
	private static readonly Color PanelHi = new(0.15f, 0.17f, 0.19f);
	private static readonly Color Line = new(0.27f, 0.31f, 0.34f);
	private static readonly Color Text = new(0.86f, 0.89f, 0.89f);
	private static readonly Color Muted = new(0.55f, 0.61f, 0.64f);
	private static readonly Color Player = new(0.13f, 0.34f, 0.31f);
	private static readonly Color PlayerHi = new(0.18f, 0.48f, 0.43f);
	private static readonly Color Enemy = new(0.36f, 0.15f, 0.14f);
	private static readonly Color EnemyHi = new(0.52f, 0.2f, 0.17f);
	private static readonly Color Spike = new(0.74f, 0.49f, 0.18f);
	private static readonly Color Accent = new(0.12f, 0.58f, 0.66f);
	private static readonly Color Danger = new(0.78f, 0.25f, 0.18f);

	private GameData _data = null!;
	private GameSession _game = null!;
	private string _savePath = "";
	private SelectionKind _selection = SelectionKind.None;
	private int _selectionIndex = -1;
	private int _extraCell = -1;

	private Label _status = null!;
	private Label _hint = null!;
	private GridContainer _board = null!;
	private VBoxContainer _benchList = null!;
	private VBoxContainer _spellList = null!;
	private VBoxContainer _shopList = null!;
	private VBoxContainer _relicList = null!;
	private Label _selectionLabel = null!;
	private Button _startButton = null!;
	private Button _endButton = null!;
	private Button _disbandButton = null!;

	public override void _Ready()
	{
		_data = LoadData();
		_savePath = ProjectSettings.GlobalizePath("user://breaker_state.json");
		var smokeUi = HasUserArg("--ui-smoke");
		var command = ReadCommandLine();
		if (!string.IsNullOrWhiteSpace(command))
		{
			GD.Print(CommandDriver.Execute(_data, _savePath, command));
			GetTree().Quit();
			return;
		}

		_game = GameSerializer.Load(_data, _savePath) ?? GameSession.New(_data, 123);
		BuildUi();
		Refresh();
		if (smokeUi)
		{
			GameSerializer.Save(_game, _savePath);
			GD.Print("[ui-smoke ok]");
			CallDeferred(MethodName.QuitAfterSmoke);
		}
	}

	private void QuitAfterSmoke()
	{
		GetTree().Quit();
	}

	private void BuildUi()
	{
		var root = new VBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill
		};
		root.SetAnchorsPreset(LayoutPreset.FullRect);
		root.AddThemeConstantOverride("separation", 10);
		AddChild(root);

		AddChildBackground();
		root.AddChild(BuildTopBar());

		var body = new HBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill
		};
		body.AddThemeConstantOverride("separation", 10);
		root.AddChild(body);

		body.AddChild(BuildLeftPanel());
		body.AddChild(BuildBoardPanel());
		body.AddChild(BuildRightPanel());
	}

	private void AddChildBackground()
	{
		var bg = new ColorRect { Color = Bg };
		bg.SetAnchorsPreset(LayoutPreset.FullRect);
		bg.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(bg);
		MoveChild(bg, 0);
	}

	private Control BuildTopBar()
	{
		var top = new HBoxContainer
		{
			CustomMinimumSize = new Vector2(0, 58),
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		top.AddThemeConstantOverride("separation", 10);
		top.AddThemeConstantOverride("margin_left", 12);

		_status = new Label
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			VerticalAlignment = VerticalAlignment.Center
		};
		_status.AddThemeColorOverride("font_color", Text);
		_status.AddThemeFontSizeOverride("font_size", 18);
		top.AddChild(_status);

		_startButton = ActionButton("开始小局", () =>
		{
			_game.StartBattle();
			ClearSelection();
			CommitAndRefresh();
		}, Accent);
		top.AddChild(_startButton);

		_endButton = ActionButton("结束回合", () =>
		{
			_game.EndTurn();
			ClearSelection();
			CommitAndRefresh();
		}, Danger);
		top.AddChild(_endButton);

		top.AddChild(ActionButton("新局", () =>
		{
			_game = GameSession.New(_data, 123);
			ClearSelection();
			CommitAndRefresh();
		}, PanelHi));

		return Pad(top, 12, 8);
	}

	private Control BuildLeftPanel()
	{
		var panel = new VBoxContainer
		{
			CustomMinimumSize = new Vector2(270, 0),
			SizeFlagsVertical = SizeFlags.ExpandFill
		};
		panel.AddThemeConstantOverride("separation", 10);

		panel.AddChild(SectionLabel("预部署"));
		_benchList = new VBoxContainer();
		_benchList.AddThemeConstantOverride("separation", 6);
		panel.AddChild(ScrollWrap(_benchList, 250));

		panel.AddChild(SectionLabel("携带法术"));
		_spellList = new VBoxContainer();
		_spellList.AddThemeConstantOverride("separation", 6);
		panel.AddChild(ScrollWrap(_spellList, 170));

		return PanelWrap(panel, new Vector2(286, 0));
	}

	private Control BuildBoardPanel()
	{
		var panel = new VBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill
		};
		panel.AddThemeConstantOverride("separation", 10);

		_hint = new Label
		{
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(0, 34)
		};
		_hint.AddThemeColorOverride("font_color", Text);
		panel.AddChild(_hint);

		_board = new GridContainer
		{
			Columns = Board.Cols,
			SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
			SizeFlagsVertical = SizeFlags.ShrinkCenter
		};
		_board.AddThemeConstantOverride("h_separation", 8);
		_board.AddThemeConstantOverride("v_separation", 8);
		panel.AddChild(_board);

		return PanelWrap(panel, new Vector2(620, 0));
	}

	private Control BuildRightPanel()
	{
		var panel = new VBoxContainer
		{
			CustomMinimumSize = new Vector2(310, 0),
			SizeFlagsVertical = SizeFlags.ExpandFill
		};
		panel.AddThemeConstantOverride("separation", 10);

		panel.AddChild(SectionLabel("当前选择"));
		_selectionLabel = BodyLabel("");
		panel.AddChild(PanelWrap(_selectionLabel, new Vector2(0, 76), PanelHi));
		_disbandButton = ActionButton("撤回选中单位", DisbandSelected, Danger);
		panel.AddChild(_disbandButton);

		panel.AddChild(SectionLabel("商店"));
		_shopList = new VBoxContainer();
		_shopList.AddThemeConstantOverride("separation", 6);
		panel.AddChild(ScrollWrap(_shopList, 250));

		panel.AddChild(SectionLabel("遗物"));
		_relicList = new VBoxContainer();
		_relicList.AddThemeConstantOverride("separation", 6);
		panel.AddChild(ScrollWrap(_relicList, 120));

		return PanelWrap(panel, new Vector2(326, 0));
	}

	private void Refresh()
	{
		_status.Text = StatusText();
		_startButton.Disabled = _game.Phase != Phase.Prep || _game.GameOver;
		_endButton.Disabled = _game.Phase != Phase.Battle || _game.GameOver;
		_disbandButton.Disabled = _selection != SelectionKind.MoveUnit || _game.Phase != Phase.Prep;
		_hint.Text = HintText();
		_selectionLabel.Text = SelectionText();
		RefreshBoard();
		RefreshBench();
		RefreshSpells();
		RefreshShop();
		RefreshRelics();
	}

	private string StatusText()
	{
		var phase = _game.Phase switch
		{
			Phase.Prep => "整备",
			Phase.Battle => $"小局 回合 {_game.BattleTurn}",
			_ => _game.Won ? "胜利" : "失败"
		};
		return $"Breaker | 城市 {_game.CityHp}/{GameSession.CityHpMax} | 波次 {_game.Wave}/{GameSession.WinWave} | 金币 {_game.Gold} | {phase}";
	}

	private string HintText()
	{
		return _selection switch
		{
			SelectionKind.Bench => "点己方下两行空格部署选中的预部署单位。",
			SelectionKind.MoveUnit => "点一个空格调位，或点右侧撤回选中单位。",
			SelectionKind.Command => "点一个目标格释放该单位的充能指令。",
			SelectionKind.Spell => "点目标格释放选中法术。",
			_ => _game.Phase == Phase.Prep
				? "整备：购买、部署、调位。点预部署单位后点棋盘部署。"
				: "小局：点有充能的己方单位选择指令，或点法术后选目标。"
		};
	}

	private string SelectionText()
	{
		return _selection switch
		{
			SelectionKind.Bench => _selectionIndex >= 0 && _selectionIndex < _game.Bench.Count
				? $"预部署：{_game.Bench[_selectionIndex]}"
				: "无",
			SelectionKind.MoveUnit => DescribeCellSelection("调位单位", _selectionIndex),
			SelectionKind.Command => DescribeCellSelection("指令来源", _selectionIndex),
			SelectionKind.Spell => _selectionIndex >= 0 && _selectionIndex < _game.SpellSlots.Count
				? $"法术：{_game.SpellSlots[_selectionIndex].CardName}"
				: "无",
			_ => "无"
		};
	}

	private string DescribeCellSelection(string prefix, int cell)
	{
		var entity = Board.IsValidCell(cell) ? _game.EntityAt(cell) : null;
		return entity == null ? "无" : $"{prefix}：#{cell} {entity.Name}";
	}

	private void RefreshBoard()
	{
		ClearChildren(_board);
		for (var i = 0; i < Board.CellCount; i++)
		{
			var idx = i;
			var button = new Button
			{
				Text = CellText(idx),
				CustomMinimumSize = new Vector2(92, 92),
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				SizeFlagsVertical = SizeFlags.ExpandFill,
				TooltipText = CellTooltip(idx),
				ClipText = true,
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
			};
			button.Pressed += () => OnCellPressed(idx);
			ApplyButtonStyle(button, CellColor(idx), IsCellSelected(idx) ? Accent : Line, 2);
			_board.AddChild(button);
		}
	}

	private string CellText(int idx)
	{
		var cell = _game.Grid.Cells[idx];
		var terrain = _game.Grid.Terrain[idx];
		var lines = new List<string> { $"#{idx}" };
		if (cell.Monster != null)
		{
			lines.Add($"敌 {cell.Monster.Power}");
			if (cell.Monster.Armor > 0)
			{
				lines.Add($"甲{cell.Monster.Armor}");
			}
			if (cell.Monster.Brittle > 0)
			{
				lines.Add($"破{cell.Monster.Brittle}");
			}
		}
		else if (cell.Unit != null)
		{
			lines.Add(ShortName(cell.Unit.Name));
			lines.Add($"战{cell.Unit.Power}");
			if (cell.Unit.Armor > 0)
			{
				lines.Add($"甲{cell.Unit.Armor}");
			}
			if (cell.Unit.ChargeCost > 0)
			{
				lines.Add($"充{cell.Unit.Charge}");
			}
			if (cell.Unit.Brittle > 0)
			{
				lines.Add($"破{cell.Unit.Brittle}");
			}
		}
		if (terrain.SpikeTurns > 0)
		{
			lines.Add($"刺{terrain.SpikeTurns}");
		}
		return string.Join("\n", lines);
	}

	private string CellTooltip(int idx)
	{
		var cell = _game.Grid.Cells[idx];
		var terrain = _game.Grid.Terrain[idx];
		var parts = new List<string> { $"格 #{idx}，r{Board.RowOf(idx)} c{Board.ColOf(idx)}" };
		if (cell.Unit != null)
		{
			parts.Add($"{cell.Unit.Name}: 战力 {cell.Unit.Power}, 护甲 {cell.Unit.Armor}, 充能 {cell.Unit.Charge}");
		}
		if (cell.Monster != null)
		{
			parts.Add($"{cell.Monster.Name}: 战力 {cell.Monster.Power}, 护甲 {cell.Monster.Armor}, 破甲 {cell.Monster.Brittle}");
		}
		if (terrain.SpikeTurns > 0)
		{
			parts.Add($"尖刺地表 {terrain.SpikeTurns} 回合");
		}
		return string.Join("\n", parts);
	}

	private Color CellColor(int idx)
	{
		var cell = _game.Grid.Cells[idx];
		if (cell.Monster != null)
		{
			return cell.Monster.Brittle > 0 ? EnemyHi : Enemy;
		}
		if (cell.Unit != null)
		{
			return cell.Unit.ChargeCost > 0 && cell.Unit.Charge >= cell.Unit.ChargeCost ? PlayerHi : Player;
		}
		if (_game.Grid.Terrain[idx].SpikeTurns > 0)
		{
			return new Color(0.32f, 0.23f, 0.12f);
		}
		return Board.IsPlayerRow(Board.RowOf(idx)) ? new Color(0.105f, 0.13f, 0.13f) : new Color(0.105f, 0.105f, 0.118f);
	}

	private bool IsCellSelected(int idx)
	{
		return (_selection == SelectionKind.MoveUnit || _selection == SelectionKind.Command) && _selectionIndex == idx;
	}

	private void RefreshBench()
	{
		ClearChildren(_benchList);
		if (_game.Bench.Count == 0)
		{
			_benchList.AddChild(BodyLabel("预部署区为空"));
			return;
		}

		for (var i = 0; i < _game.Bench.Count; i++)
		{
			var index = i;
			var card = _game.Card(_game.Bench[i]);
			var button = ListButton($"{card.Name}  战{card.Power} 甲{card.Armor}\n{card.Text}", () =>
			{
				_selection = SelectionKind.Bench;
				_selectionIndex = index;
				Refresh();
			});
			button.Disabled = _game.Phase != Phase.Prep;
			ApplyButtonStyle(button, _selection == SelectionKind.Bench && _selectionIndex == index ? PanelHi : Panel, Line, 1);
			_benchList.AddChild(button);
		}
	}

	private void RefreshSpells()
	{
		ClearChildren(_spellList);
		for (var i = 0; i < _game.SpellSlots.Count; i++)
		{
			var index = i;
			var slot = _game.SpellSlots[i];
			var card = _game.Card(slot.CardName);
			var state = slot.CooldownRemaining > 0 ? $"冷却 {slot.CooldownRemaining}" : "就绪";
			var button = ListButton($"{card.Name}  [{state}]\n{card.Text}", () =>
			{
				_selection = SelectionKind.Spell;
				_selectionIndex = index;
				Refresh();
			});
			button.Disabled = _game.Phase != Phase.Battle || slot.CooldownRemaining > 0;
			ApplyButtonStyle(button, _selection == SelectionKind.Spell && _selectionIndex == index ? PanelHi : Panel, Line, 1);
			_spellList.AddChild(button);
		}
	}

	private void RefreshShop()
	{
		ClearChildren(_shopList);
		for (var i = 0; i < _game.ShopOffers.Count; i++)
		{
			var index = i;
			var offer = _game.ShopOffers[i];
			var card = _game.Card(offer.CardName);
			var price = card.Value > 0 ? card.Value : GameSession.FallbackCardPrice;
			var sold = offer.Sold ? "已售" : $"{price}金";
			var button = ListButton($"{card.Name}  {sold}\n{card.Text}", () =>
			{
				_game.BuyCard(index);
				ClearSelection();
				CommitAndRefresh();
			});
			button.Disabled = _game.Phase != Phase.Prep || offer.Sold || _game.Gold < price;
			_shopList.AddChild(button);
		}
	}

	private void RefreshRelics()
	{
		ClearChildren(_relicList);
		if (_game.RelicOffers.Count == 0)
		{
			_relicList.AddChild(BodyLabel("暂无遗物报价"));
		}
		foreach (var owned in _game.Relics)
		{
			_relicList.AddChild(BodyLabel($"已拥有：{owned}"));
		}
		for (var i = 0; i < _game.RelicOffers.Count; i++)
		{
			var index = i;
			var offer = _game.RelicOffers[i];
			var relic = _game.Data.RelicsByName[offer.RelicName];
			var state = offer.Sold ? "已拥有" : $"{relic.Price}金";
			var button = ListButton($"{relic.Name}  {state}\n{relic.Text}", () =>
			{
				_game.BuyRelic(index);
				ClearSelection();
				CommitAndRefresh();
			});
			button.Disabled = _game.Phase != Phase.Prep || offer.Sold || _game.Gold < relic.Price;
			_relicList.AddChild(button);
		}
	}

	private void OnCellPressed(int idx)
	{
		switch (_selection)
		{
			case SelectionKind.Bench:
				_game.DeployUnit(_selectionIndex, idx);
				ClearSelection();
				CommitAndRefresh();
				return;
			case SelectionKind.MoveUnit:
				if (idx != _selectionIndex)
				{
					_game.MoveUnit(_selectionIndex, idx);
				}
				ClearSelection();
				CommitAndRefresh();
				return;
			case SelectionKind.Command:
				_game.UseCommand(_selectionIndex, idx);
				ClearSelection();
				CommitAndRefresh();
				return;
			case SelectionKind.Spell:
				_game.CastSpell(_selectionIndex, idx, _extraCell >= 0 ? _extraCell : null);
				ClearSelection();
				CommitAndRefresh();
				return;
		}

		var cell = _game.Grid.Cells[idx];
		if (_game.Phase == Phase.Prep && cell.Unit != null)
		{
			_selection = SelectionKind.MoveUnit;
			_selectionIndex = idx;
			Refresh();
			return;
		}

		if (_game.Phase == Phase.Battle && cell.Unit != null)
		{
			if (cell.Unit.ChargeCost > 0 && cell.Unit.Charge >= cell.Unit.ChargeCost)
			{
				_selection = SelectionKind.Command;
				_selectionIndex = idx;
			}
			else
			{
				_game.Log($"{cell.Unit.Name} 当前没有可用指令。");
			}
			Refresh();
		}
	}

	private void DisbandSelected()
	{
		if (_selection == SelectionKind.MoveUnit && _game.Phase == Phase.Prep)
		{
			_game.DisbandUnit(_selectionIndex);
			ClearSelection();
			CommitAndRefresh();
		}
	}

	private void ClearSelection()
	{
		_selection = SelectionKind.None;
		_selectionIndex = -1;
		_extraCell = -1;
	}

	private void CommitAndRefresh()
	{
		GameSerializer.Save(_game, _savePath);
		Refresh();
	}

	private static GameData LoadData()
	{
		var csvPath = ProjectSettings.GlobalizePath("res://../csvs/breaker.csv");
		return BreakerDataLoader.Load(csvPath);
	}

	private static string ReadCommandLine()
	{
		var args = OS.GetCmdlineUserArgs();
		for (var i = 0; i < args.Length; i++)
		{
			if (args[i] == "--cmd" && i + 1 < args.Length)
			{
				return args[i + 1];
			}
		}

		return "";
	}

	private static bool HasUserArg(string expected)
	{
		return OS.GetCmdlineUserArgs().Any(arg => arg == expected)
			|| OS.GetCmdlineArgs().Any(arg => arg == expected);
	}

	private static Button ActionButton(string text, Action onPressed, Color color)
	{
		var button = new Button
		{
			Text = text,
			CustomMinimumSize = new Vector2(110, 36)
		};
		button.Pressed += onPressed;
		ApplyButtonStyle(button, color, Line, 1);
		return button;
	}

	private static Button ListButton(string text, Action onPressed)
	{
		var button = new Button
		{
			Text = text,
			CustomMinimumSize = new Vector2(0, 58),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			Alignment = HorizontalAlignment.Left
		};
		button.Pressed += onPressed;
		ApplyButtonStyle(button, Panel, Line, 1);
		return button;
	}

	private static Label SectionLabel(string text)
	{
		var label = new Label { Text = text };
		label.AddThemeColorOverride("font_color", Text);
		label.AddThemeFontSizeOverride("font_size", 15);
		return label;
	}

	private static Label BodyLabel(string text)
	{
		var label = new Label
		{
			Text = text,
			AutowrapMode = TextServer.AutowrapMode.WordSmart
		};
		label.AddThemeColorOverride("font_color", Text);
		label.AddThemeFontSizeOverride("font_size", 13);
		return label;
	}

	private static Control ScrollWrap(Control child, int height)
	{
		var scroll = new ScrollContainer
		{
			CustomMinimumSize = new Vector2(0, height),
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		child.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.AddChild(child);
		return scroll;
	}

	private static Control PanelWrap(Control child, Vector2 minSize, Color? color = null)
	{
		var panel = new PanelContainer
		{
			CustomMinimumSize = minSize,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill
		};
		panel.AddThemeStyleboxOverride("panel", Box(color ?? Panel, Line, 1, 6));
		panel.AddChild(Pad(child, 10, 10));
		return panel;
	}

	private static MarginContainer Pad(Control child, int horizontal, int vertical)
	{
		var pad = new MarginContainer();
		pad.AddThemeConstantOverride("margin_left", horizontal);
		pad.AddThemeConstantOverride("margin_right", horizontal);
		pad.AddThemeConstantOverride("margin_top", vertical);
		pad.AddThemeConstantOverride("margin_bottom", vertical);
		pad.AddChild(child);
		return pad;
	}

	private static void ApplyButtonStyle(Button button, Color bg, Color border, int width)
	{
		button.AddThemeStyleboxOverride("normal", Box(bg, border, width, 5));
		button.AddThemeStyleboxOverride("hover", Box(bg.Lightened(0.08f), border.Lightened(0.1f), width, 5));
		button.AddThemeStyleboxOverride("pressed", Box(bg.Darkened(0.08f), Accent, Math.Max(1, width), 5));
		button.AddThemeStyleboxOverride("disabled", Box(bg.Darkened(0.18f), border.Darkened(0.2f), width, 5));
		button.AddThemeColorOverride("font_color", Text);
		button.AddThemeColorOverride("font_disabled_color", Muted);
		button.AddThemeFontSizeOverride("font_size", 13);
	}

	private static StyleBoxFlat Box(Color bg, Color border, int width, int radius)
	{
		return new StyleBoxFlat
		{
			BgColor = bg,
			BorderColor = border,
			BorderWidthLeft = width,
			BorderWidthRight = width,
			BorderWidthTop = width,
			BorderWidthBottom = width,
			CornerRadiusTopLeft = radius,
			CornerRadiusTopRight = radius,
			CornerRadiusBottomLeft = radius,
			CornerRadiusBottomRight = radius,
			ContentMarginLeft = 8,
			ContentMarginRight = 8,
			ContentMarginTop = 6,
			ContentMarginBottom = 6
		};
	}

	private static void ClearChildren(Node node)
	{
		foreach (var child in node.GetChildren())
		{
			child.QueueFree();
		}
	}

	private static string ShortName(string name)
	{
		return name.Length <= 3 ? name : name[..3];
	}
}
