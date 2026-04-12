using System.Collections.Concurrent;
using System.Threading.Channels;
using Grpc.Core;
using Orderbook;
using Trading.Oms.Application.Commands;
using Trading.Oms.Application.Interfaces;
using Trading.Oms.Application.Models;


namespace Trading.Oms.Infrastructure.Grpc;

// ==================================================================================
// ARCHITECTURAL FLOW: ASYNCHRONOUS COORDINATION PATTERN
// ==================================================================================
// Write Flow:
// [PlaceOrderCommand] --(Enqueue/Park)--> [_commandChannel] --(Batch/Dequeue)--> [WriteLoopAsync] --(WriteAsync)--> [gRPC Stream]
//
// Read Flow
// [gRPC Stream] --(ReadAll)--> [ReadLoopAsync] --(Match ID)--> [Dictionary/TCS] --(SetResult)--> [Original Task Wakes Up]
//
// 1. WRITING FLOW (Producer):
//    - PlaceOrderCommand: Enqueues an 'EngineCommand' into the internal '_commandChannel'
//      and registers a 'TaskCompletionSource' (TCS) in the pending tracking dictionary.
//      It then awaits the TCS.Task, effectively "parking" the caller thread.
//    - WriteLoopAsync: Acts as a dedicated consumer that drains the '_commandChannel',
//      performs micro-batching, and flushes the commands to the gRPC request stream.
//
// 2. READING FLOW (Consumer & Matcher):
//    - ReadLoopAsync: Continuously monitors the incoming gRPC response stream.
//    - Reconciliation: Upon receiving an Ack, it uses the 'client_order_id' to perform
//      an atomic 'TryRemove' from the tracking dictionaries (_pendingPlaceOrders/_pendingCancelOrders).
//    - Resolution: If a matching TCS is found, it manually transitions the Task to a
//      completed state by setting the result. This "wakes up" the original caller thread.
//
// 3. TASKCOMPLETIONSOURCE (The Bridge):
//    - Unlike standard async methods (like DB calls) where the runtime manages completion,
//      TCS gives us manual control over the Task's lifecycle. 
//    - It bridges the gap between the decoupled Request-Stream and Response-Stream,
//      allowing a multi-threaded system to reconcile asynchronous results.
// ==================================================================================

/*
   [ 1. USER CALL ]
            |
    PlaceOrderCommand() 
            |
   (A) Create TCS & Add to        [ 5. TASK COMPLETION ]
       _pendingPlaceOrders  <---------- TryRemove TCS & 
            |                           SetResult(ack)
            |                                 ^
   (B) Write to channel                       |
       _commandChannel                        |
            |                                 |
            v                         [ 4. READ LOOP ]
    [ 2. WRITE LOOP ]                 ReadLoopAsync()
     WriteLoopAsync()                         ^
            |                                 |
    (C) Micro-batching                        |
        & WriteAsync                          |
            |                                 |
            v                                 |
     +================================================+
     |             3. gRPC STREAM (The Border)        |
     |  Outbound Request  ---------->  Inbound Ack    |
     +================================================+
*/



public class GrpcMatchingEngineClient : IMatchingEngineClient, IDisposable
{
    private readonly AsyncDuplexStreamingCall<EngineBatchCommand, OrderBatchResponse> _stream;
     
    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<EnginePlaceOrderResult>> _pendingPlaceOrders =
        new();

    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<EngineCancelOrderResult>> _pendingCancelOrders =
        new();


    // MPSC like channel for sending commands from api to gRPC 
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
                        placeTcs.TrySetResult(new EnginePlaceOrderResult(status, ack.ClientOrderId, ack.EngineOrderId));
                    }

                    else if (_pendingCancelOrders.TryRemove(ack.EngineOrderId, out var cancelTcs))
                    {
                        var status = response.Success
                            ? Domain.Enums.Status.Submitted
                            : Domain.Enums.Status.Rejected;
                        cancelTcs.TrySetResult(
                            new EngineCancelOrderResult(status, ack.ClientOrderId));
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
            tcs.TrySetResult(new EnginePlaceOrderResult(Domain.Enums.Status.Failed, null, null));

        _pendingPlaceOrders.Clear();
    }

    // Helper Mappers
    private Side MapSide(Domain.Enums.Side side) =>
        side == Domain.Enums.Side.Bid ? Side.Bid : Side.Ask;

    private OrderType MapOrderType(Domain.Enums.OrderType type) =>
        type == Domain.Enums.OrderType.Limit ? OrderType.Limit : OrderType.Market;

    public void Dispose()
    {
        _cts.Cancel();
        _stream.Dispose();
        _cts.Dispose();
    }
}