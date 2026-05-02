use crate::models::order::Side;
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub enum CancelRejectReason {
    OrderNotFound,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub enum MatchEvent {
    OrderPlaced {
        client_order_id: u64,
        engine_order_id: u64,
        price: u64,
        qty: u64,
        side: Side,
    },
    TradeExecuted {
        maker_client_order_id: u64,
        taker_client_order_id: u64,
        maker_engine_order_id: u64,
        taker_engine_order_id: u64,
        price: u64,
        qty: u64,
        timestamp: i64,
        taker_side: Side,
        trade_id: u64,
    },
    OrderCancelled {
        client_order_id: u64,
        engine_order_id: u64,
        cancelled_qty: u64,
    },
    CancelRejected {
        client_order_id: u64,
        reason: CancelRejectReason,
    },
    OrderKilled {
        client_order_id: u64,
        killed_qty: u64,
    },
    PublicTrade {
        price: u64,
        qty: u64,
        taker_side: Side,
        trade_id: u64,
    },
}
