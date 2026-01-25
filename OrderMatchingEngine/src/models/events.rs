use crate::models::order::Side;

#[derive(Debug, Clone, Copy, PartialEq)]
pub enum MatchEvent {
    OrderPlaced {id: u64, price: u64, qty: u64, side: Side},
    TradeExecuted {maker_id: u64, taker_id: u64, price: u64, qty: u64, timestamp: i64}, //Timestamp Unix Timestamp in Nanoseconds for record purpose
    OrderCancelled {id: u64, cancelled_qty: u64},
    OrderKilled {id: u64, killed_qty: u64},
}
