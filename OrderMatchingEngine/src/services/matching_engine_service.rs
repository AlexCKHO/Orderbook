use crate::models::order_book::OrderBook;

use tokio::sync::mpsc;

// Internal Models
use crate::models::events::MatchEvent;
use crate::models::order::EngineAction;

pub struct MatchingEngineService {
    outbound_tx: mpsc::Sender<Vec<MatchEvent>>,
}

impl MatchingEngineService {
    pub fn new(outbound_tx: mpsc::Sender<Vec<MatchEvent>>) -> Self {
        Self { outbound_tx }
    }

    pub fn run_matching_actor(self, mut inbound_rx: mpsc::Receiver<EngineAction>) {
        tokio::spawn(async move {
            let mut order_book = OrderBook::new();

            while let Some(cmd) = inbound_rx.recv().await {
                let events = order_book.process_single(cmd);

                if !events.is_empty() {
                    let _ = self.outbound_tx.try_send(events);
                }
            }
        });
    }
}
