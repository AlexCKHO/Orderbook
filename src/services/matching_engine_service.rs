use crate::models::order_book::OrderBook;
use std::sync::Arc;
use tokio::sync::Mutex;
use tonic::{Request, Response, Status};
use std::time::{SystemTime, UNIX_EPOCH};

// Ref to Generated Code
use crate::orderbook_grpc::{self, OrderRequest, OrderResponse};
use crate::orderbook_grpc::match_event::EventData;

// Ref to Internal Model
use crate::models::order::{Order, Side, OrderType};
use crate::models::events::MatchEvent as InternalEvent;

// Note: Orderbook on Tokio Integrating Sync Logic with Async Runtime
pub struct MatchingEngineService {
    engine: Arc<Mutex<OrderBook>>,
}

impl MatchingEngineService {
    pub fn new() -> Self {
        let order_book = OrderBook::new();
        Self {
            engine: Arc::new(Mutex::new(order_book))
        }
    }


}


