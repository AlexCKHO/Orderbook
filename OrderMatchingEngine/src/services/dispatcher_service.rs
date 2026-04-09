use crate::models::events::MatchEvent;
use tokio::sync::mpsc;
use tokio::sync::mpsc::error::TrySendError;
use tokio::time::Instant;

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

        while let Some(event_batch) = dispatcher_rx.recv().await {
            let batch_len = event_batch.len();

            // The dispatcher is the boundary between the matcher and slower I/O.
            // Matching stays focused on book updates while downstream sinks absorb backpressure here.
            match self.kafka_tx.try_send(event_batch) {
                Ok(()) => {
                    batches_forwarded += 1;
                }
                Err(TrySendError::Full(event_batch)) => {
                    batches_blocked += 1;
                    let wait_started_at = Instant::now();
                    eprintln!(
                        "[dispatcher] kafka queue full; blocking to preserve events. batch_size={}; blocked_batches={}; forwarded_batches={}",
                        batch_len, batches_blocked, batches_forwarded
                    );

                    if let Err(e) = self.kafka_tx.send(event_batch).await {
                        eprintln!(
                            "[dispatcher] kafka channel closed after forwarding {} batches: {}",
                            batches_forwarded, e
                        );
                        break;
                    }

                    batches_forwarded += 1;
                    eprintln!(
                        "[dispatcher] kafka queue recovered after {}ms; batch_size={}",
                        wait_started_at.elapsed().as_millis(),
                        batch_len
                    );
                }
                Err(TrySendError::Closed(_)) => {
                    eprintln!(
                        "[dispatcher] kafka channel closed after forwarding {} batches",
                        batches_forwarded
                    );
                    break;
                }
            }
        }
    }
}
