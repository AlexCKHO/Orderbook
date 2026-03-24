use crate::models::order_book::OrderBook;
use std::sync::Arc;
use std::sync::atomic::{AtomicU64, Ordering};
use tokio::sync::mpsc;

// Internal Models
use crate::models::events::MatchEvent;
use crate::models::order::EngineAction;

pub struct MatchingEngineService {
    outbound_tx: mpsc::Sender<MatchEvent>,
}

impl MatchingEngineService {
    pub fn new(outbound_tx: mpsc::Sender<MatchEvent>) -> Self {
        Self { outbound_tx }
    }

    pub async fn run_matching_actor(
        mut self,
        mut inbound_rx: mpsc::Receiver<EngineAction>,
        counter: Arc<AtomicU64>,
    ) {
        let mut order_book = OrderBook::new(0, 0);
        let mut reusable_events_buffer: Vec<MatchEvent> = Vec::with_capacity(1024);

        while let Some(cmd) = inbound_rx.recv().await {
            order_book.process_single(cmd, &mut reusable_events_buffer);

            for vec_of_events in reusable_events_buffer.iter() {
                if let Err(e) = self.outbound_tx.send(vec_of_events.clone()).await {
                    eprintln!("Critical Error: Outbound channel closed: {}", e);
                    break;
                }
            }

            counter.fetch_add(1, Ordering::Relaxed);
        }
    }
}
