using System.Collections.Concurrent;
using System.Threading.Channels;
using Grpc.Core;
using Orderbook;
using Trading.Oms.Application.Commands;
using Trading.Oms.Application.Interfaces;
using Trading.Oms.Application.Models;


namespace Trading.Oms.Infrastructure.Grpc;

public class GrpcMatchingEngineClient : IMatchingEngineClient, IDisposable
{
    private readonly AsyncDuplexStreamingCall<EngineBatchCommand, OrderBatchResponse> _stream;

    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<EnginePlaceOrderResult>> _pendingPlaceOrders =
        new();

    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<EngineCancelOrderResult>> _pendingCancelOrders =
        new();


    private readonly Channel<EngineCommand> _commandChannel =
        System.Threading.Channels.Channel.CreateUnbounded<EngineCommand>();

    private readonly CancellationTokenSource _cts = new();

    public GrpcMatchingEngineClient(MatchingEngine.MatchingEngineClient client)
    {
        // 1. Initiating gRPC connection
        _stream = client.PlaceBatchStream(cancellationToken: _cts.Token);

        // 2. Initiating pipe for writing and reading
        _ = Task.Run(WriteLoopAsync);
        _ = Task.Run(ReadLoopAsync);
    }

    // API Entry: Place
    public async Task<EnginePlaceOrderResult> PlaceOrderCommand(PlaceOrderCommand cmd, ulong orderId)
    {
        var tcs = new TaskCompletionSource<EnginePlaceOrderResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingPlaceOrders.TryAdd(orderId, tcs);

        var protoOrder = new OrderRequest
        {
            ClientOrderId = orderId,
            Price = cmd.Price ?? 0,
            Qty = cmd.Quantity,
            Side = MapSide(cmd.Side),
            OrderType = MapOrderType(cmd.OrderType),
            Timestamp = cmd.SubmittedAtUtc.ToUnixTimeMilliseconds()
        };


        await _commandChannel.Writer.WriteAsync(new EngineCommand { PlaceOrder = protoOrder });

        return await tcs.Task;
    }

    // API Entry: Cancel
    public async Task<EngineCancelOrderResult> CancelOrderCommand(CancelOrderCommand cmd)
    {
        var tcs = new TaskCompletionSource<EngineCancelOrderResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        _pendingCancelOrders.TryAdd(cmd.EngineOrderId, tcs);

        var protoCancel = new CancelRequest
        {
            EngineOrderId = cmd.EngineOrderId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await _commandChannel.Writer.WriteAsync(new EngineCommand { CancelOrder = protoCancel });

        return await tcs.Task;
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (var command in _commandChannel.Reader.ReadAllAsync(_cts.Token))
            {
                var batch = new EngineBatchCommand();
                batch.Commands.Add(command);

                while (batch.Commands.Count < 100 && _commandChannel.Reader.TryRead(out var next))
                {
                    batch.Commands.Add(next);
                }

                await _stream.RequestStream.WriteAsync(batch);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Write Loop Error: {ex.Message}");
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            await foreach (var response in _stream.ResponseStream.ReadAllAsync(_cts.Token))
            {
                foreach (var ack in response.Acks)
                {
                    if (_pendingPlaceOrders.TryRemove(ack.ClientOrderId, out var placeTcs))
                    {
                        var status = response.Success
                            ? Domain.Enums.Status.Submitted
                            : Domain.Enums.Status.Rejected;
                        placeTcs.TrySetResult(new EnginePlaceOrderResult(status, ack.ClientOrderId, null, null));
                    }

                    else if (_pendingCancelOrders.TryRemove(ack.EngineOrderId, out var cancelTcs))
                    {
                        var status = response.Success
                            ? Domain.Enums.Status.Submitted
                            : Domain.Enums.Status.Rejected;
                        cancelTcs.TrySetResult(
                            new EngineCancelOrderResult(status, ack.ClientOrderId, null, null));
                    }
                }
            }
        }
        catch (RpcException ex)
        {
            Console.WriteLine($"Stream broke: {ex.Status.Detail}");
            FailAllPending();
        }
    }

    private void FailAllPending()
    {
        foreach (var tcs in _pendingPlaceOrders.Values)
            tcs.TrySetResult(new EnginePlaceOrderResult(Domain.Enums.Status.Failed, null,
                null));

        _pendingPlaceOrders.Clear();
    }

    // Helper Mappers
    private Orderbook.Side MapSide(Domain.Enums.Side side) =>
        side == Domain.Enums.Side.Bid ? Orderbook.Side.Bid : Orderbook.Side.Ask;

    private Orderbook.OrderType MapOrderType(Domain.Enums.OrderType type) =>
        type == Domain.Enums.OrderType.Limit ? Orderbook.OrderType.Limit : Orderbook.OrderType.Market;

    public void Dispose()
    {
        _cts.Cancel();
        _stream?.Dispose();
        _cts.Dispose();
    }
}