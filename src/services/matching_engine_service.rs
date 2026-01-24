use crate::models::order_book::OrderBook;
use std::sync::Arc;
use tokio::sync::Mutex;

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


