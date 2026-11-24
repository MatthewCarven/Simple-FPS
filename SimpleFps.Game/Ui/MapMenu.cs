using System;
using System.Collections.Generic;
using Godot;
using SimpleFps.Core.Bake;

namespace SimpleFps.Game.Ui;

/// <summary>
/// The map list at start: one row per map, sorted by title, and a search box that keeps the
/// keyboard. Typing filters, Up and Down move, Enter or a double-click plays, Esc clears the
/// search. Which maps are listed, and in what order, is worked out in Core by <see cref="MapList"/>.
/// </summary>
public partial class MapMenu : CanvasLayer
{
    private const int Margin = 32;
    private const int PageStep = 10;

    private static readonly Color Background = new(0.08f, 0.09f, 0.11f);
    private static readonly Color Dim = new(0.6f, 0.62f, 0.66f);
    private static readonly Color Warning = new(1f, 0.7f, 0.4f);
    private const string Hint = "Type to search titles, authors and file names. Up and Down to move, Enter or double-click to play.";

    private readonly LineEdit _search = new()
    {
        PlaceholderText = "Search",
        ClearButtonEnabled = true,
        KeepEditingOnTextSubmit = true,
    };

    private readonly Tree _tree = new()
    {
        Columns = 3,
        HideRoot = true,
        ColumnTitlesVisible = false,
        SelectMode = Tree.SelectModeEnum.Row,
        SizeFlagsVertical = Control.SizeFlags.ExpandFill,

        // The search box keeps the keyboard, so the list never takes focus away from it.
        FocusMode = Control.FocusModeEnum.None,
    };

    private readonly Label _count = new();
    private readonly Label _footer = new() { Text = Hint };
    private IReadOnlyList<MapListEntry> _maps = Array.Empty<MapListEntry>();

    /// <summary>Raised with the map's name when one is picked.</summary>
    public event Action<string>? MapChosen;

    public bool IsOpen => Visible;

    /// <summary>The name of the highlighted map, or null if the search matched nothing.</summary>
    public string? Selected => _tree.GetSelected()?.GetMetadata(0).AsString();

    public void Build()
    {
        // Above the status text and the tuning panel, which are on layer 1.
        Layer = 3;
        Visible = false;

        var background = new ColorRect { Color = Background };
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(background);

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        foreach (string side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
        {
            margin.AddThemeConstantOverride(side, Margin);
        }

        AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        margin.AddChild(column);

        var title = new Label { Text = "Simple FPS", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 28);
        _count.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
        _count.AddThemeColorOverride("font_color", Dim);
        var header = new HBoxContainer();
        header.AddChild(title);
        header.AddChild(_count);
        column.AddChild(header);

        _search.TextChanged += _ => Refresh(Selected);
        _search.TextSubmitted += _ => ChooseSelected();
        _search.GuiInput += OnSearchKey;
        column.AddChild(_search);

        _tree.SetColumnExpandRatio(0, 5);
        _tree.SetColumnExpandRatio(1, 3);
        _tree.SetColumnExpandRatio(2, 2);
        for (int i = 0; i < _tree.Columns; i++)
        {
            _tree.SetColumnClipContent(i, true);
        }

        _tree.ItemActivated += ChooseSelected;
        column.AddChild(_tree);

        _footer.AddThemeColorOverride("font_color", Dim);
        column.AddChild(_footer);
    }

    public void SetMaps(IReadOnlyList<MapListEntry> maps) => _maps = maps;

    /// <summary>Shows the list with a map highlighted, keeping the last search. A message replaces the hint.</summary>
    public void Open(string? select, string? message = null)
    {
        Visible = true;
        ShowMessage(message);
        Refresh(select);
        _search.GrabFocus();
        _search.Edit();
    }

    public void Close() => Visible = false;

    /// <summary>Shows a message under the list, or the usual hint when it's null.</summary>
    public void ShowMessage(string? message)
    {
        _footer.Text = message ?? Hint;
        _footer.AddThemeColorOverride("font_color", message is null ? Dim : Warning);
    }

    /// <summary>Types a search, as if at the keyboard, and highlights a map if it's among those found.</summary>
    public void Search(string text, string? select = null)
    {
        _search.Text = text;
        Refresh(select);
    }

    public void ClearSearch() => Search("", Selected);

    /// <summary>Plays the highlighted map, if the search left one.</summary>
    public void ChooseSelected()
    {
        if (Selected is { } name)
        {
            MapChosen?.Invoke(name);
        }
    }

    /// <summary>Lists the maps matching the search, highlighting the given one if it's there, otherwise the first.</summary>
    private void Refresh(string? select)
    {
        _tree.Clear();
        TreeItem root = _tree.CreateItem();
        TreeItem? first = null;
        TreeItem? wanted = null;
        IReadOnlyList<MapListEntry> found = MapList.Filter(_maps, _search.Text);
        foreach (MapListEntry map in found)
        {
            TreeItem row = _tree.CreateItem(root);
            row.SetText(0, map.DisplayTitle);
            row.SetText(1, map.Author is { } author ? $"by {author}" : "");
            row.SetText(2, map.Name);
            row.SetCustomColor(2, Dim);
            row.SetMetadata(0, map.Name);
            first ??= row;
            if (map.Name == select)
            {
                wanted = row;
            }
        }

        _count.Text = found.Count == _maps.Count ? $"{_maps.Count} maps" : $"{found.Count} of {_maps.Count} maps";
        if ((wanted ?? first) is { } highlight)
        {
            _tree.SetSelected(highlight, 0);
            _tree.ScrollToItem(highlight);
        }
    }

    private void OnSearchKey(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } key)
        {
            return;
        }

        if (key.Keycode == Key.Escape)
        {
            ClearSearch();
            _search.AcceptEvent();
            return;
        }

        int step = key.Keycode switch
        {
            Key.Down => 1,
            Key.Up => -1,
            Key.Pagedown => PageStep,
            Key.Pageup => -PageStep,
            _ => 0,
        };

        if (step != 0)
        {
            Move(step);
            _search.AcceptEvent();
        }
    }

    private void Move(int step)
    {
        TreeItem? item = _tree.GetSelected();
        if (item is null)
        {
            return;
        }

        for (int i = 0; i < Math.Abs(step); i++)
        {
            TreeItem? next = step > 0 ? item.GetNextVisible() : item.GetPrevVisible();
            if (next is null || next == _tree.GetRoot())
            {
                break;
            }

            item = next;
        }

        _tree.SetSelected(item, 0);
        _tree.ScrollToItem(item);
    }
}
