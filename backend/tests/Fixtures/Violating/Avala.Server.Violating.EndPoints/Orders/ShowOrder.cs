using Avala.Server.Violating.Domain;
using FastEndpoints;
using Order = Avala.Server.Violating.Domain.Order;

namespace Avala.Server.Violating.EndPoints.Orders;

internal sealed class ShowOrderRequest
{
    public Guid Id { get; init; }
}

internal static class ShowOrderMapper
{
    public static Order ToOrder(ShowOrderRequest request) => new() { Id = request.Id };
}

internal sealed class ShowOrderEndpoint : Endpoint<ShowOrderRequest, Order>
{
    public override void Configure() => Get("/orders/{id}");

    public override Task HandleAsync(ShowOrderRequest req, CancellationToken ct) => Send.OkAsync(ShowOrderMapper.ToOrder(req), ct);
}

internal sealed record OrderListingResponse(int Count);

internal sealed class OrderListing : EndpointWithoutRequest<OrderListingResponse>
{
    public override void Configure() => Get("/orders");

    public override Task HandleAsync(CancellationToken ct) => Send.OkAsync(new OrderListingResponse(0), ct);
}
