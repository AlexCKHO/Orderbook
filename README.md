# High-Frequency Trading Orderbook (Rust & C#)

This project is a high-performance, low-latency electronic trading system consisting of a matching engine written in Rust and a market simulator client in C#. It is designed to handle extreme throughput, reaching milestones of **over 9.5 million transactions per second (TPS)** using gRPC bidirectional streaming, batching, and zero-allocation techniques.

##  Architecture Overview

The system follows a distributed, actor-based model to ensure maximum CPU utilization and mechanical sympathy. The architecture is deliberately designed to isolate the synchronous matching core from asynchronous networking I/O.

```text
[ Simulator / Client ]
         |
         | HTTP
         v
     [ OMS (C#) ]
         |
         | gRPC
         v
[ Matching Engine (Rust) ]
         |
         | Domain Events
         v
    [ Kafka / Redpanda ]
      |           |           |
      |           |           |
      v           v           v
 [ Projector ] [ Market Data ] [ Notification ]
      |
      | SQL
      v
 [ Read Model DB ]
```

## Components
Market Simulator (C#): A high-throughput benchmarking tool that replays real Binance market data or generates high-density random order flow.

gRPC Gateway (Rust): An asynchronous network layer that receives Protobuf batches, parses them into internal engine actions, and manages backpressure.

Matching Engine (Rust): The core matching logic. It runs synchronously on a dedicated Tokio worker, utilizing BTreeMap structures for price-time priority (FIFO) execution.

Event Publisher: Batches matched trades and order events, routing them to a Kafka/Redpanda cluster for persistence and downstream consumption.

## Key Features
Limit Order Book (LOB): Full implementation with support for Limit and Market (IOC) orders.

Extreme Performance Engineering: Achieved ~9.5M TPS by optimizing memory layouts, avoiding C# Garbage Collection (GC) pauses, and eliminating heap allocations in the Rust hot path.

Zero-Allocation Pipeline: Customized gRPC stream handling utilizing object reuse (.Clear() over reallocation) to prevent serialization-induced stalls and memory fragmentation.

Historical Replay: Built-in parser for Binance aggregated trade (aggTrade) and depth (depth) data to simulate real-world market volatility and complex order flow.

Mechanical Backpressure: Intelligent channel buffering to naturally throttle producers and maintain system stability under massive load without causing Out-Of-Memory (OOM) crashes.

## Tech Stack
Backend: Rust (Tokio, Tonic/gRPC, Prost)

Benchmark Client: .NET 9 (C#, gRPC Client, SocketsHttpHandler tuning)

Messaging: Kafka / Redpanda (confluent-kafka-dotnet, rdkafka)

Data Format: Protocol Buffers (Protobuf)

##  Performance Benchmarks
The system is optimized for continuous "Hot Path" execution. By passing batches of data between threads and minimizing context switching, the engine maintains high CPU saturation.

Unbatched gRPC (Sequential): ~1M TPS

Batched gRPC (4 Multiplexed Streams): ~9.5M TPS

Latency: Sub-millisecond p99 tail latencies under sustained load.


## 🚧 Development Focus
The current development cycle is focused on hardening the Kafka event pipeline to match the ingestion throughput, preventing producer deadlocks, and exploring shared memory (IPC) for co-located deployments to completely bypass network serialization overhead.
