use crate::models::order_book::OrderBook;
use std::sync::Arc;
use tokio::sync::Mutex;


pub struct MatchingEngineService {
    engine: Arc<Mutex<OrderBook>>,
}

impl MatchingEngineService {
    pub fn new() -> Self {
        let order_book = OrderBook{
            bids: Vec::new(),
            asks: Vec::new(),
        };
        
        Self {
            engine: Arc::new(Mutex::new(order_book))
        }
    }
}
