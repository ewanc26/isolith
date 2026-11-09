using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Isolith.Core;
using Isolith.Gameplay;
using Isolith.Level;

namespace Isolith.UI;

/// <summary>
/// Information about a course for display purposes.
/// </summary>
public readonly struct CourseInfo
{
    public string Id { get; }
    public string Name { get; }
    public string Author { get; }
    public string Path { get; }
    public string Hash { get; }
    public RunStats? BestRun { get; }

    public CourseInfo(string id, string name, string author, string path, string hash, RunStats? bestRun)
    {
        Id = id;
        Name = name;
        Author = author;
        Path = path;
        Hash = hash;
        BestRun = bestRun;
    }
}

/// <summary>
/// A panel that lists all available courses and lets the player choose one.
/// </summary>
[GlobalClass]
public partial class CourseSelectionPanel : CenterContainer
{
    private VBoxContainer _courseList = null!;
    private Button _backButton = null!;
    private Button _randomButton = null!;
    private List<Button> _courseButtons = new();
    private Action _onClosed = null!;

    public event Action<CourseInfo>? CourseSelected;

    public override void _Ready()
    {
        AnchorRight = 1;
        AnchorBottom = 1;
        ProcessMode = ProcessModeEnum.Always;

        // Build and populate at construction so the focus chain exists even
        // while hidden — the smoke test checks every menu control is reachable,
        // and a panel that only wires its buttons when opened would fail that.
        // Focusing stays in Open(); a deferred grab-focus on a hidden panel is
        // what produced "not inside tree" errors before.
        Build();
        RefreshCourseList();
    }

    private void Build()
    {
        PanelContainer card = MenuKit.Card(520f);
        AddChild(card);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        card.AddChild(column);

        column.AddChild(MenuKit.Title("SELECT COURSE"));

        var scrollContainer = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 300),
        };
        column.AddChild(scrollContainer);

        _courseList = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(400, 0),
        };
        _courseList.AddThemeConstantOverride("separation", 8);
        scrollContainer.AddChild(_courseList);

        var buttonRow = new HBoxContainer
        {
            AnchorRight = 1,
        };
        buttonRow.AddThemeConstantOverride("separation", 12);
        column.AddChild(buttonRow);

        _backButton = MenuKit.MenuButton("Back", () => _onClosed());
        buttonRow.AddChild(_backButton);

        buttonRow.AddChild(new Control { CustomMinimumSize = new Vector2(0, 0), MouseFilter = Control.MouseFilterEnum.Ignore });

        _randomButton = MenuKit.MenuButton("Random", () => SelectRandomCourse());
        buttonRow.AddChild(_randomButton);

        column.AddChild(MenuKit.Caption(
            GameInput.UsingGamepad
                ? "Left stick to choose  ·  A to select  ·  B to go back"
                : "Arrows to choose  ·  Enter to select  ·  Escape to go back"));
    }

    private void RefreshCourseList()
    {
        foreach (Node child in _courseList.GetChildren())
        {
            _courseList.RemoveChild(child);
            child.QueueFree();
        }
        _courseButtons.Clear();

        List<CourseInfo> courses = LoadAllCourses();

        if (courses.Count == 0)
        {
            _courseList.AddChild(MenuKit.Caption("No courses found."));
            return;
        }

        foreach (CourseInfo course in courses.OrderBy(c => c.Name))
        {
            AddCourseRow(course);
        }

        // Wire focus chain
        var allControls = _courseButtons.Cast<Control>().Concat(new Control[] { _backButton, _randomButton }).ToArray();
        MenuKit.FocusChain(allControls);
    }

    private void AddCourseRow(CourseInfo course)
    {
        var row = new VBoxContainer();
        row.AddThemeConstantOverride("separation", 2);

        var button = new Button
        {
            Text = course.Name,
            CustomMinimumSize = new Vector2(0, 44),
            FocusMode = Control.FocusModeEnum.All,
        };

        button.AddThemeFontSizeOverride("font_size", 16);
        button.AddThemeColorOverride("font_color", MenuKit.Ink);
        button.AddThemeColorOverride("font_hover_color", MenuKit.Accent);
        button.AddThemeColorOverride("font_focus_color", MenuKit.Accent);

        button.AddThemeStyleboxOverride("normal", MenuKit.ButtonBox(new Color(1, 1, 1, 0.05f), Colors.Transparent));
        button.AddThemeStyleboxOverride("hover", MenuKit.ButtonBox(new Color(1, 1, 1, 0.10f), Colors.Transparent));
        button.AddThemeStyleboxOverride("pressed", MenuKit.ButtonBox(new Color(1, 1, 1, 0.16f), Colors.Transparent));
        button.AddThemeStyleboxOverride("focus", MenuKit.ButtonBox(new Color(1, 1, 1, 0.10f), MenuKit.Accent));

        button.Pressed += () => CourseSelected?.Invoke(course);

        row.AddChild(button);
        row.AddChild(MenuKit.Caption(GetInfoText(course), 12));

        _courseList.AddChild(row);
        _courseButtons.Add(button);
    }

    private static string GetInfoText(CourseInfo course)
    {
        string author = string.IsNullOrEmpty(course.Author) ? "Unknown" : course.Author;
        string bestTime = course.BestRun is { TimeMs: > 0 } run ? $"Best: {run.TimeText}" : "Not completed";
        return $"{author}  ·  {bestTime}";
    }

    private List<CourseInfo> LoadAllCourses()
    {
        return CourseCatalog.LoadAll()
            .Select(entry => new CourseInfo(
                entry.Course.Id,
                entry.Course.Name,
                entry.Course.Author,
                entry.Path,
                entry.Course.Hash,
                RunHistory.BestFor(entry.Course.Id, entry.Course.Hash)))
            .ToList();
    }

    private void SelectRandomCourse()
    {
        if (_courseButtons.Count == 0)
            return;

        int index = (int)(GD.Randi() % (uint)_courseButtons.Count);
        List<CourseInfo> courses = LoadAllCourses().OrderBy(c => c.Name).ToList();
        if (index >= 0 && index < courses.Count)
            CourseSelected?.Invoke(courses[index]);
    }

    public void Open(Action onClosed)
    {
        _onClosed = onClosed;
        Visible = true;
        RefreshCourseList();
        MenuKit.Focus(_courseButtons.FirstOrDefault() ?? _backButton);
    }

    public override void _Input(InputEvent @event)
    {
        // Hidden panels keep receiving _input (only Control gui input respects
        // visibility), and _onClosed is not assigned until Open(), so this guard
        // is what stops Escape on the title screen hitting a null delegate.
        if (!Visible)
            return;

        // ui_cancel is Escape on keyboard and B on a pad, so this backs out of
        // the panel on either without a second binding.
        if (@event.IsActionPressed("ui_cancel"))
        {
            _onClosed();
            GetViewport().SetInputAsHandled();
        }
        else
        {
            GameInput.Observe(@event);
        }
    }
}
