use crate::models::order_book::OrderBook;
use std::sync::Arc;
use std::sync::atomic::{AtomicU64, Ordering};
use tokio::sync::mpsc;
use tokio::sync::mpsc::error::TrySendError;

// Internal Models
use crate::models::events::MatchEvent;
use crate::models::order::EngineAction;

pub struct MatchingEngineService {
    dispatcher_tx: mpsc::Sender<Vec<MatchEvent>>,
}

impl MatchingEngineService {
    pub fn new(dispatcher_tx: mpsc::Sender<Vec<MatchEvent>>) -> Self {
        Self { dispatcher_tx }
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

            if !reusable_events_buffer.is_empty() {
                // Ship the whole event batch in one go so the hot path only pays
                // a single channel handoff instead of N async sends.
                let event_batch = std::mem::take(&mut reusable_events_buffer);

                match self.dispatcher_tx.try_send(event_batch) {
                    Ok(()) => {}
                    Err(TrySendError::Full(event_batch)) => {
                        if let Err(e) = self.dispatcher_tx.send(event_batch).await {
                            eprintln!("Critical Error: Dispatcher channel closed: {}", e);
                            break;
                        }
                    }
                    Err(TrySendError::Closed(_)) => {
                        eprintln!("Critical Error: Dispatcher channel closed");
                        break;
                    }
                }

                reusable_events_buffer = Vec::with_capacity(8192);
            }

            // 4. ✅ Add the whole batch size to the TPS counter at once!
            counter.fetch_add(batch_size, Ordering::Relaxed);
        }
    }
}
