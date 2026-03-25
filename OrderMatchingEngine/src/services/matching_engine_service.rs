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
        // 1. ✅ Change the receiver type to accept the full Batch (Vec)
        mut inbound_rx: mpsc::Receiver<Vec<EngineAction>>,
        counter: Arc<AtomicU64>,
    ) {
        let mut order_book = OrderBook::new(0, 0);

        // Increase capacity a bit since we are handling 5000+ orders at a time
        let mut reusable_events_buffer: Vec<MatchEvent> = Vec::with_capacity(8192);

        // `cmd_batch` is now a Vec<EngineAction>
        while let Some(cmd_batch) = inbound_rx.recv().await {
            let batch_size = cmd_batch.len() as u64;

            // 2. ✅ Unleash the CPU: Process the entire vector synchronously
            order_book.process_batch(cmd_batch, &mut reusable_events_buffer);

            // 3. Send the generated events to Redpanda/Kafka
            for event in reusable_events_buffer.iter() {
                if let Err(e) = self.outbound_tx.send(event.clone()).await {
                    eprintln!("Critical Error: Outbound channel closed: {}", e);
                    break;
                }
            }
            reusable_events_buffer.clear();

            // 4. ✅ Add the whole batch size to the TPS counter at once!
            counter.fetch_add(batch_size, Ordering::Relaxed);
        }
    }
}
