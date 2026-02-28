#[derive(Debug, Clone, Copy, PartialEq)]
pub enum Side {
    Bid,
    Ask,
}

#[derive(Debug, Clone, Copy, PartialEq)]
pub enum OrderType {
    Limit,
    Market,
}

#[derive(Debug, Clone)]
pub enum EngineAction {
    Create(OrderEntry),
    Cancel(CancelEntry),
}

#[derive(Debug, Clone)]
pub struct OrderEntry {
    pub id: u64,
    pub price: u64,
    pub qty: u64,
    pub side: Side,
    pub order_type: OrderType,
    // Notes: Why timestamp:

    // 1. Audit Trail & Dispute Resolution
    // If two orders are submitted at the same price (e.g., both at $100), why was Order A filled while Order B remained unfilled? A timestamp is essential to prove that Order A arrived before Order B, thereby validating the integrity of the FIFO (First-In-First-Out) / Price-Time Priority logic.
    //
    // 2. Latency Analysis & Performance Metrics
    // In High-Frequency Trading (HFT), benchmarking performance is critical. You need to answer: "How much time did the engine take to match this order?"
    //
    // Formula: Time(Trade Execution Event) - Time(Order Arrival) = System Latency. Without an input timestamp, it is impossible to calculate this delta or determine if the engine is performing efficiently.
    //
    // 3. Debugging & Replay
    // During periods of high market volatility or extreme throughput, logs can become chaotic. Without precise timestamps, it is nearly impossible to reconstruct the exact sequence of events or determine which specific order triggered a trade.
    pub timestamp: i64
}

#[derive(Debug, Clone)]
pub struct CancelEntry {
    pub id: u64,
}
