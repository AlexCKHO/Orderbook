use crate::models::events::MatchEvent;
use std::fs::File;
use std::io::Write;
use tokio::sync::mpsc;
use tokio::sync::mpsc::Sender;
use tokio::sync::mpsc::error::TrySendError;
use tokio::time::{Duration, sleep};
pub struct DispatcherService {
    kafka_tx: mpsc::Sender<Vec<(MatchEvent, u64, u64)>>,
}

impl DispatcherService {
    pub fn new(kafka_tx: Sender<Vec<(MatchEvent, u64, u64)>>) -> Self {
        Self { kafka_tx }
    }

    pub async fn run(self, mut dispatcher_rx: mpsc::Receiver<Vec<(MatchEvent, u64, u64)>>) {
        let mut batches_forwarded: u64 = 0;
        let mut batches_blocked: u64 = 0;

        let mut smart_buffer: Vec<(MatchEvent, u64, u64)> = Vec::with_capacity(5000);
        let linger_time = Duration::from_millis(10);

        loop {
            tokio::select! {
                // Scenario A: receiving incoming batch
                Some(mut incoming_batch) = dispatcher_rx.recv() => {
                    smart_buffer.append(&mut incoming_batch);

                    if smart_buffer.len() >= 2000 {
                        self.flush_to_kafka(
                            &mut smart_buffer,
                            &mut batches_forwarded,
                            &mut batches_blocked,
                        ).await;
                    }
                }

                // Scenario C: 10ms time's up
                _ = sleep(linger_time), if !smart_buffer.is_empty() => {
                    self.flush_to_kafka(
                        &mut smart_buffer,
                        &mut batches_forwarded,
                        &mut batches_blocked,
                    ).await;
                }

                // Scenario B: Channel closed
                else => {
                    if !smart_buffer.is_empty() {
                        self.flush_to_kafka(
                            &mut smart_buffer,
                            &mut batches_forwarded,
                            &mut batches_blocked,
                        ).await;
                    }
                    break;
                }
            }
        }
    }

    async fn flush_to_kafka(
        &self,
        buffer: &mut Vec<(MatchEvent, u64, u64)>,
        forwarded: &mut u64,
        blocked: &mut u64,
    ) {
        // ⚡ Zero-Copy replace all the data and reuse the empty buffer
        let payload = std::mem::replace(buffer, Vec::with_capacity(5000));

        match self.kafka_tx.try_send(payload) {
            Ok(()) => {
                *forwarded += 1;
            }
            Err(TrySendError::Full(failed_payload)) => {
                *blocked += 1;
                eprintln!(
                    "[dispatcher] Kafka queue full! Spilling {} events to disk.",
                    failed_payload.len()
                );

                // ⚡ Non-blocking Spill。
                tokio::spawn(async move {
                    if let Err(e) = Self::emergency_log_to_disk(failed_payload) {
                        eprintln!("[dispatcher] CRITICAL: Disk log failed: {}", e);
                    }
                });
            }
            Err(TrySendError::Closed(_)) => {
                eprintln!("[dispatcher] Kafka channel closed, cannot send.");
            }
        }
    }

    pub async fn read_all_from_binary() -> bincode::Result<Vec<(MatchEvent, u64, u64)>> {
        let mut file = File::open("TempEventResult.bin").map_err(bincode::Error::from)?;
        let mut all_events = Vec::new();

        while let Ok(batch) = bincode::deserialize_from::<&File, Vec<(MatchEvent, u64, u64)>>(&file)
        {
            all_events.extend(batch);
        }
        Ok(all_events)
    }

    pub fn emergency_log_to_disk(match_event: Vec<(MatchEvent, u64, u64)>) -> bincode::Result<()> {
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
