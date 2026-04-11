use crate::models::order_book::OrderBook;
use std::sync::Arc;
use std::sync::atomic::{AtomicU64, Ordering};
use tokio::sync::mpsc;
use tokio::sync::mpsc::error::TrySendError;

// Internal Models
use crate::models::events::MatchEvent;
use crate::models::order::EngineAction;

pub struct MatchingEngineService {
    dispatcher_tx: mpsc::Sender<Vec<(MatchEvent, u64)>>,
}
const OMS_MASK: u64 = 1 << 63;

impl MatchingEngineService {
    pub fn new(dispatcher_tx: mpsc::Sender<Vec<(MatchEvent, u64)>>) -> Self {
        Self { dispatcher_tx }
    }

    pub async fn run_matching_actor(
        mut self,

        mut inbound_rx: mpsc::Receiver<Vec<EngineAction>>,
        counter: Arc<AtomicU64>,
    ) {
        let mut oms_engine_order_id_counter: u64 = 0;
        let mut last_event_sequence: u64 = 0;
        let mut order_book = OrderBook::new(0);

        let mut reusable_events_buffer: Vec<MatchEvent> = Vec::with_capacity(8192);

        while let Some(mut cmd_batch) = inbound_rx.recv().await {
            let batch_size = cmd_batch.len() as u64;

            // Implement Engine Order ID
            for mut cmd in cmd_batch {
                if let EngineAction::Create(ref mut order) = cmd {
                    let is_oms_order = (order.client_order_id & OMS_MASK) != 0;

                    if is_oms_order {
                        oms_engine_order_id_counter += 1;
                        order.engine_order_id = oms_engine_order_id_counter | OMS_MASK;
                    } else {
                        order.engine_order_id = order.client_order_id;
                    }
                }
                // Add order id to orderAck
                order_book.process_single(cmd, &mut reusable_events_buffer);

                if !reusable_events_buffer.is_empty() {
                    let event_batch = std::mem::take(&mut reusable_events_buffer);

                    let sequenced_batch: Vec<(MatchEvent, u64)> = event_batch
                        .into_iter()
                        .map(|evt| {
                            last_event_sequence += 1;
                            (evt, last_event_sequence)
                        })
                        .collect();

                    match self.dispatcher_tx.try_send(sequenced_batch) {
                        Ok(()) => {}
                        Err(TrySendError::Full(batch)) => {
                            if let Err(e) = self.dispatcher_tx.send(batch).await {
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

                counter.fetch_add(batch_size, Ordering::Relaxed);
            }
        }
    }
}
