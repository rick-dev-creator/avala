using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Avala.Observability.Tests.Metrics;

internal sealed class MeterRecorder : IDisposable
{
    private readonly MeterListener listener = new();
    private readonly ConcurrentQueue<Measured> measured = new();

    public MeterRecorder(Meter meter)
    {
        listener.InstrumentPublished = (instrument, subscription) =>
        {
            if (instrument.Meter == meter)
            {
                subscription.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        listener.Start();
    }

    public IReadOnlyList<Measured> Of(string instrument) => [.. measured.Where(measurement => measurement.Instrument == instrument)];

    public void Dispose() => listener.Dispose();

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
        measured.Enqueue(new Measured(
            instrument.Name,
            value,
            string.Join(",", tags.ToArray().OrderBy(tag => tag.Key, StringComparer.Ordinal).Select(tag => $"{tag.Key}={tag.Value}"))));
}

internal sealed record Measured(string Instrument, double Value, string Tags);
