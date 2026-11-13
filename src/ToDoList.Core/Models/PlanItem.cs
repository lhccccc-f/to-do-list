using CommunityToolkit.Mvvm.ComponentModel;

namespace ToDoList.Core.Models;

public sealed class PlanItem : ObservableObject
{
    private string _content = "";
    private string _duration = "";
    private int _number;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Content { get => _content; set => SetProperty(ref _content, value ?? ""); }
    public string Duration { get => _duration; set => SetProperty(ref _duration, value ?? ""); }
    public int Number { get => _number; set => SetProperty(ref _number, value); }
    public bool IsBlank => string.IsNullOrWhiteSpace(Content) && string.IsNullOrWhiteSpace(Duration);

    public PlanItem Snapshot() => new() { Id = Id, Content = Content, Duration = Duration, Number = Number };
}

public sealed record DayPlan(DateOnly Date, IReadOnlyList<PlanItem> Items);
