using Avala.Sdk;

namespace Avala.Autopilot.Backlogs;

internal sealed record BacklogTask(string Key, string Instruction);

internal sealed record RecurringTask(string Key, string Instruction, TimeSpan Every);

internal sealed record BacklogDeclaration(IReadOnlyList<BacklogTask> Tasks, IReadOnlyList<RecurringTask> Recurring)
{
    public static BacklogDeclaration Empty { get; } = new([], []);
}

internal sealed record RecurringTurn(Option<RecurringTask> Due, Option<DateTimeOffset> NextDue);

internal static class RecurringSchedule
{
    extension(IReadOnlyList<RecurringTask> recurring)
    {
        public RecurringTurn At(DateTimeOffset now, IReadOnlyDictionary<string, DateTimeOffset> lastTaken)
        {
            var dues = recurring
                .Select(task => (Task: task, Due: lastTaken.TryGetValue(task.Key, out var taken) ? taken + task.Every : DateTimeOffset.MinValue))
                .ToList();
            var due = dues.Where(entry => entry.Due <= now).OrderBy(entry => entry.Due).Select(entry => entry.Task).FirstOrDefault();
            var next = dues.Where(entry => entry.Due > now).Select(entry => entry.Due).Order().FirstOrDefault();

            return new RecurringTurn(due.ToOption(), next == default ? Option<DateTimeOffset>.None : next);
        }
    }
}
