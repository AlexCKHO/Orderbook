use crate::models::engine_payload::EnginePayload;
use crate::models::order_book::OrderBook;
use std::fs::File;
use std::io::Write;
use std::sync::Arc;
use std::sync::atomic::{AtomicU64, Ordering};
use tokio::sync::mpsc;
use tokio::sync::mpsc::error::TrySendError;
// Internal Models
use crate::models::events::MatchEvent;
use crate::models::order::EngineAction;
use crate::orderbook_grpc::OrderAck;
use crate::system::thread::set_core;

pub struct MatchingEngineService {
    dispatcher_tx: mpsc::Sender<Vec<(MatchEvent, u64, u64)>>,
}
const OMS_MASK: u64 = 1 << 63;

impl MatchingEngineService {
    pub fn new(dispatcher_tx: mpsc::Sender<Vec<(MatchEvent, u64, u64)>>) -> Self {
        Self { dispatcher_tx }
    }

    pub fn run_matching_actor(
        self,
        mut inbound_rx: mpsc::Receiver<EnginePayload>,
        counter: Arc<AtomicU64>,
    ) {
        if set_core(0) {
            println!("✅ Matcher Actor pinned to Core 0");
        } else {
            eprintln!("❌ Failed to pin Matcher Actor");
        }

        let mut oms_engine_order_id_counter: u64 = 0;
        let mut last_event_sequence: u64 = 0;
        let mut order_book = OrderBook::new(0);
        let event_buffer_size: usize = 8092;

        let mut reusable_events_buffer: Vec<MatchEvent> = Vec::with_capacity(event_buffer_size);
        let mut output_buffer: Vec<(MatchEvent, u64, u64)> = Vec::with_capacity(8092);

        while let Some(payload) = inbound_rx.blocking_recv() {
            let batch_size = payload.actions.len() as u64;
            let batch_timestamp = payload.ingress_timestamp as u64;
            let mut acks: Vec<OrderAck> = Vec::with_capacity(payload.actions.len());

            for mut action in payload.actions {
                let mut pending_ack = None;

                if let EngineAction::Create(ref mut order) = action {
                    let is_oms_order = (order.client_order_id & OMS_MASK) != 0;

                    if is_oms_order {
                        oms_engine_order_id_counter += 1;
                        order.engine_order_id = oms_engine_order_id_counter | OMS_MASK;
                    } else {
                        order.engine_order_id = order.client_order_id;
                    }
                    pending_ack = Some(OrderAck {
                        client_order_id: order.client_order_id,
                        engine_order_id: order.engine_order_id,
                    });
                } else if let EngineAction::Cancel(ref cancel) = action {
                    pending_ack = Some(OrderAck {
                        client_order_id: cancel.client_order_id,
                        engine_order_id: cancel.engine_order_id,
                    });
                }

                order_book.process_single(action, &mut reusable_events_buffer);

                if let Some(ack) = pending_ack {
                    acks.push(ack);
                }
            }

            // ==========================================
            // Action for each batch
            // 1. Return OrderBatchResponse to gRPC
            // 2. Send result to dispatcher
            // ==========================================

            // Section 1:
            // Call reply_tx to send OrderAck
            // To form the OrderBatchResponse
            // last part of the gRPC
            // rpc PlaceBatchStream (stream EngineBatchCommand) returns (stream OrderBatchResponse);
            if let Some(tx) = payload.reply_tx {
                let _ = tx.send(acks);
            }

            // Section 2.
            // Send result to dispatcher
            if !reusable_events_buffer.is_empty() {
                output_buffer.clear();

                for evt in reusable_events_buffer.drain(..) {
                    last_event_sequence += 1;
                    output_buffer.push((evt, last_event_sequence, batch_timestamp));
                }

                match self.dispatcher_tx.try_send(output_buffer.clone()) {
                    Ok(()) => {}
                    Err(TrySendError::Full(recovered_batch)) => {
                        eprintln!("[Backpressure] Dispatcher full, journaling batch to disk...");
                        let _ = emergency_log_to_disk(recovered_batch);
                    }
                    Err(TrySendError::Closed(recovered_batch)) => {
                        eprintln!("Critical: Dispatcher closed!");
                        let _ = emergency_log_to_disk(recovered_batch);
                        break;
                    }
                }
            }

            counter.fetch_add(batch_size, Ordering::Relaxed);
        }
    }
}

pub async fn read_all_from_binary() -> bincode::Result<Vec<(MatchEvent, u64)>> {
    let file = File::open("TempEventResult.bin").map_err(bincode::Error::from)?;
    let mut all_events = Vec::new();
    while let Ok(batch) = bincode::deserialize_from::<&File, Vec<(MatchEvent, u64)>>(&file) {
        all_events.extend(batch);
    }
    Ok(all_events)
}
fn emergency_log_to_disk(match_event: Vec<(MatchEvent, u64, u64)>) -> bincode::Result<()> {
    use std::fs::OpenOptions;

    let mut file = OpenOptions::new()
        .create(true)
        .append(true)
        .open("TempEventResult.bin")
        .map_err(bincode::Error::from)?;

    let encoded: Vec<u8> = bincode::serialize(&match_event)?;
    file.write_all(&encoded).map_err(bincode::Error::from)?;
    Ok(())
}
