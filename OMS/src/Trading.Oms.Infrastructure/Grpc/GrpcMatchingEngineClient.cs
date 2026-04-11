using System.Collections.Concurrent;
using System.Threading.Channels;
using Grpc.Core;
using Orderbook;
using Trading.Oms.Application.Commands;
using Trading.Oms.Application.Interfaces;
using Trading.Oms.Application.Models;

namespace Trading.Oms.Infrastructure.Grpc;

public class GrpcMatchingEngineClient : IMatchingEngineClient
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
        // 1. 初始化時就開啟持久 Stream
        _stream = client.PlaceBatchStream(cancellationToken: _cts.Token);

        // 2. 啟動背景任務
        _ = Task.Run(WriteLoopAsync);
        _ = Task.Run(ReadLoopAsync);
    }

    private async Task WriteLoopAsync()
    {
        await foreach (var command in _commandChannel.Reader.ReadAllAsync(_cts.Token))
        {
            var batch = new EngineBatchCommand();
            batch.Commands.Add(command);

            // 這裡可以做 Micro-batching: 趁現在還有單，一次過掃走
            while (batch.Commands.Count < 100 && _commandChannel.Reader.TryRead(out var next))
            {
                batch.Commands.Add(next);
            }

            await _stream.RequestStream.WriteAsync(batch);
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            await foreach (var response in _stream.ResponseStream.ReadAllAsync(_cts.Token))
            {
                // 根據你改好的 Proto，現在 Response 裡面應該有 acked_client_ids
                foreach (var clientId in response.Message.)
                {
                    if (_pendingPlaceOrders.TryRemove(clientId, out var tcs))
                    {
                        var status = response.Success ? Status.Submitted : Status.Rejected;
                        tcs.TrySetResult(new EnginePlaceOrderResult(status, null, response.Message));
                    }
                    // 同樣處理 Cancel 邏輯...
                }
            }
        }
        catch (RpcException ex)
        {
            Console.WriteLine($"Stream broke: {ex.Status.Detail}");
            FailAllPending();
        }
    }

    public Task<EnginePlaceOrderResult> PlaceOrderCommand(PlaceOrderCommand cmd, ulong orderId)
    {
        using var call = client.PlaceBatchStream();

        // Reading responses

        var readTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var response in call.ResponseStream.ReadAllAsync())
                {
                    if (!response.Success)
                        continue;
                }
            }
            catch (RpcException rpcException)
            {
                Console.WriteLine($"\n❌ Historical batch reader broke: {rpcException.Status.Detail}");
            }
        });
        try
        {
            var batch = new EngineBatchCommand();
            EngineCommand command = new EngineCommand();

            command.PlaceOrder = cmd;

            batch.Commands.Add(command);
        }
        catch (RpcException rpcException)
        {
        }
    }

    public Task<EngineCancelOrderResult> CancelOrderCommand(CancelOrderCommand cmd)
    {
        throw new NotImplementedException();
    }
}