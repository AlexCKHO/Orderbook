use std::fs::File;
use std::io::Write;
use crate::models::events::MatchEvent;
use tokio::sync::mpsc;
use tokio::sync::mpsc::error::TrySendError;
use tokio::time::{Duration, Instant, timeout};



pub struct DispatcherService {
    kafka_tx: mpsc::Sender<Vec<(MatchEvent, u64)>>,
}

impl DispatcherService {
    pub fn new(kafka_tx: mpsc::Sender<Vec<(MatchEvent, u64)>>) -> Self {
        Self { kafka_tx }
    }

    pub async fn run(self, mut dispatcher_rx: mpsc::Receiver<Vec<(MatchEvent, u64)>>) {
        let mut batches_forwarded: u64 = 0;
        let mut batches_blocked: u64 = 0;

        // Initialize a local accumulator to aggregate small incoming batches into larger ones.
        let mut smart_buffer: Vec<(MatchEvent, u64)> = Vec::with_capacity(5000);

        // Define the maximum linger time (10ms).
        // This ensures events are dispatched within a deterministic timeframe even if the buffer isn't full.
        let linger_time = Duration::from_millis(10);

        loop {
            // Use a timeout to wait for incoming events to prevent the loop from idling indefinitely.
            match timeout(linger_time, dispatcher_rx.recv()).await {
                // Scenario A: Events received within the linger window.
                Ok(Some(mut incoming_batch)) => {
                    // Accumulate micro-batches into the main local buffer.
                    smart_buffer.append(&mut incoming_batch);

                    // Threshold-based flush: If the buffer size exceeds 2000,
                    // dispatch immediately without waiting for the timeout.
                    if smart_buffer.len() >= 2000 {
                        self.flush_to_kafka(
                            &mut smart_buffer,
                            &mut batches_forwarded,
                            &mut batches_blocked,
                        )
                        .await;
                    }
                }

                // Scenario B: The incoming channel has been closed (graceful shutdown).
                Ok(None) => {
                    eprintln!("[dispatcher] incoming channel closed. Flushing remaining events...");
                    if !smart_buffer.is_empty() {
                        self.flush_to_kafka(
                            &mut smart_buffer,
                            &mut batches_forwarded,
                            &mut batches_blocked,
                        )
                        .await;
                    }
                    break;
                }

                // Scenario C: Linger timeout reached.
                // Dispatches whatever is in the buffer to maintain latency requirements.
                Err(_) => {
                    if !smart_buffer.is_empty() {
                        eprintln!(
                            "[dispatcher] Linger timeout reached.  Flushing remaining events..."
                        );
                        self.flush_to_kafka(
                            &mut smart_buffer,
                            &mut batches_forwarded,
                            &mut batches_blocked,
                        )
                        .await;
                    }
                }
            }
        }
    }

    /// Dispatches the accumulated "Mega Batch" to the Kafka sender.
    async fn flush_to_kafka(
        &self,
        buffer: &mut Vec<(MatchEvent, u64)>,
        forwarded: &mut u64,
        blocked: &mut u64,
    ) {
        let batch_len = buffer.len();

        // Note: Currently cloning the buffer for dispatch.
        // For performance-critical paths, consider using Arc<Vec<...>> or mem::take to avoid allocations.
        let payload = buffer.clone();

        match self.kafka_tx.try_send(payload) {
            Ok(()) => {
                *forwarded += 1;
                // Clear the buffer for the next aggregation cycle upon success.
                buffer.clear();
            }
            Err(TrySendError::Full(failed_payload)) => {
                *blocked += 1;
                let wait_started_at = Instant::now();
                eprintln!(
                    "[dispatcher] kafka queue full; blocking. MEGA_BATCH_SIZE={}; total_blocked={}",
                    batch_len, blocked
                );

                // Fallback to asynchronous wait if the downstream channel is saturated.
                // Since we are sending high-density batches, this backpressure should trigger rarely.
                if let Err(e) = self.kafka_tx.send(failed_payload).await {
                    eprintln!("[dispatcher] kafka channel closed during block: {}", e);
                    return;
                }

                *forwarded += 1;
                eprintln!(
                    "[dispatcher] queue recovered after {}ms; MEGA_BATCH_SIZE={}",
                    wait_started_at.elapsed().as_millis(),
                    batch_len
                );

                buffer.clear();
            }
            Err(TrySendError::Closed(_)) => {
                eprintln!("[dispatcher] kafka channel closed, cannot send.");
            }
        }
    }

    pub async fn read_all_from_binary() -> bincode::Result<Vec<(MatchEvent, u64)>> {
        let mut file = File::open("TempEventResult.bin").map_err(bincode::Error::from)?;
        let mut all_events = Vec::new();

        while let Ok(batch) = bincode::deserialize_from::<&File, Vec<(MatchEvent, u64)>>(&file) {
            all_events.extend(batch);
        }
        Ok(all_events)
    }
    fn emergency_log_to_disk(match_event: Vec<(MatchEvent, u64)>) -> bincode::Result<()> {
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
}
