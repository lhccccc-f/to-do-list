namespace ToDoList.Core.Models;

public sealed record CalendarBook(int Id, string Name, DateOnly? SelectedDate);

public sealed record PlanBatch(int Id, string Content, string DateSummary, int RemainingCount);
