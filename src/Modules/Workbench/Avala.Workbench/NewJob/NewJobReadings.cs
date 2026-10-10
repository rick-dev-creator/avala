using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Spending;

namespace Avala.Workbench.NewJob;

internal sealed class NewJobReadings(JobBoard board, LimitReadings readings)
{
    public IEnumerable<string> Repositories() =>
        board.Jobs.Values.OrderByDescending(job => job.Summary.Submitted).Select(job => job.Summary.Repository).Distinct();

    public string Reading(Option<Result<ConnectionPreview, JobRejection>> preview, ConnectionName name) =>
        NewJobPhrases.Compared(preview, name).Match(
            NewJobPhrases.Reading,
            () =>
            {
                var current = readings.Judged([.. readings.ByConnection().Where(used => used.Connection == name).SelectMany(used => used.Usage.Limits)])
                    .Where(reading => !reading.Expired)
                    .OrderByDescending(reading => reading.Used)
                    .Select(reading => Option<LimitReading>.Some(reading))
                    .FirstOrDefault();

                return NewJobPhrases.Reading(current.Map(reading => reading.Limit), current.Match(reading => reading.Used, () => 0));
            });
}
