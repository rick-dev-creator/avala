using Mediator;

namespace Avala.Server.Testing;

public sealed class FakeSender(Func<object, object> answer) : ISender
{
    public List<object> Sent { get; } = [];

    public ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
        AnswerAsync<TResponse>(request);

    public ValueTask<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default) =>
        AnswerAsync<TResponse>(command);

    public ValueTask<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default) =>
        AnswerAsync<TResponse>(query);

    public ValueTask<object?> Send(object message, CancellationToken cancellationToken = default) =>
        AnswerAsync<object?>(message);

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamQuery<TResponse> query, CancellationToken cancellationToken = default) =>
        AsyncEnumerable.Empty<TResponse>();

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
        AsyncEnumerable.Empty<TResponse>();

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamCommand<TResponse> command, CancellationToken cancellationToken = default) =>
        AsyncEnumerable.Empty<TResponse>();

    public IAsyncEnumerable<object?> CreateStream(object message, CancellationToken cancellationToken = default) =>
        AsyncEnumerable.Empty<object?>();

    private ValueTask<TResponse> AnswerAsync<TResponse>(object message)
    {
        Sent.Add(message);

        return ValueTask.FromResult((TResponse)answer(message));
    }
}
