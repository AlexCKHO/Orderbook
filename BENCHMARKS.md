
| **Iteration** | **Architecture / Change**           | **TPS (Orders/sec)** | **Latency (p50)** | **Key Bottleneck Identified**          | **Solution**                                     |
| ------------- | ----------------------------------- | -------------------- | ----------------- | -------------------------------------- | ------------------------------------------------ |
| **v1.0**      | Parallel.ForEach (Multi-thread)     | ~18,000              | <1ms              | **GC Pressure** (High Allocation)      | Implemented **Object Pooling / Zero-allocation** |
| **v2.0**      | **gRPC Streaming** (Bi-directional) | ~50,000              | 0ms               | Connection Overhead & Handshakes       | Switched from Unary to Persistent Stream         |
| **v3.0**      | **Endurance Test** (300k orders)    | ~99,500              | 185ms             | TCP Buffer Saturation / System Calls   | (Current Status)                                 |
