use crate::models::engine_payload::EnginePayload;
use crate::models::events::MatchEvent;
use crate::models::order::EngineAction;
use crate::orderbook_grpc::{EngineBatchCommand, MatchEvent as ProtoMatchEvent};
use prost::Message as ProstMessage;
use rdkafka::Message;
use rdkafka::config::ClientConfig;
use rdkafka::consumer::{CommitMode, Consumer, StreamConsumer};
use rdkafka::producer::{FutureProducer, FutureRecord};
use std::sync::Arc;
use std::time::Duration;
use tokio::sync::mpsc;

pub struct RedpandaConsumer {
    brokers: String,
    group_id: String,
    topic: String,
    inbound_tx: mpsc::Sender<EnginePayload>,
}

impl RedpandaConsumer {
    pub fn new(
        brokers: &str,
        group_id: &str,
        topic: &str,
        inbound_tx: mpsc::Sender<EnginePayload>,
    ) -> Self {
        Self {
            brokers: brokers.to_string(),
            group_id: group_id.to_string(),
            topic: topic.to_string(),
            inbound_tx,
        }
    }

    pub async fn start_event_consumer(self: Arc<Self>, idx: usize) {
        let topic = self.topic.clone();
        let brokers = self.brokers.clone();
        let group_id = self.group_id.clone();
        let inbound_tx = self.inbound_tx.clone();

        println!("Started Redpanda worker for topic: {}", topic);

        // `tokio::spawn` runs this block as an asynchronous background task.
        // `move` transfers ownership of 'consumer' to the background task,
        // ensuring it lives as long as the task runs without lifetime issues.

        println!("[Worker {}] Starting consumer for {}", idx, topic);

        let consumer: StreamConsumer = ClientConfig::new()
            .set("bootstrap.servers", brokers)
            // Group ID evenly distributes partitions across all consumer instances
            // subscribed to the same topic.
            .set("group.id", group_id)
            // Disable auto-commit to take manual control over when a message
            // is marked as fully processed.
            .set("enable.auto.commit", "false")
            // Start reading from the earliest available message if no prior
            // offset exists for this group.
            .set("auto.offset.reset", "earliest")
            .create()
            .expect("Consumer creation failed");

        // Subscribe the consumer to the specified topic.
        consumer
            .subscribe(&[&topic])
            .expect("Failed to subscribe to topic");

        // Infinite loop to continuously process incoming messages.
        loop {
            // recv() polls the broker for new messages.
            // .await yields execution to the Tokio runtime while waiting,
            // freeing up the thread for other tasks.
            match consumer.recv().await {
                Err(e) => eprint!("Kafka error: {}", e),
                Ok(msg) => {
                    // Unwrap the incoming Kafka message as array of u8
                    if let Some(bytes) = msg.payload() {
                        match EngineBatchCommand::decode(bytes) {
                            Ok(engine_batch_command) => {
                                for engine_command in engine_batch_command.commands {
                                    if let Ok(engine_action) =
                                        EngineAction::try_from(engine_command)
                                    {

                                        // todo!
                                        // if let Err(e) = inbound_tx.send(engine_action).await {
                                        //     eprint!("Channel Closed {}", e)
                                        // }
                                    }
                                }
                            }
                            Err(e) => {
                                eprint!(
                                    "Failed to convert incoming Kafka message to EngineCommand {}",
                                    e
                                )
                            }
                        }
                    } else {
                        eprint!("Received empty payload, skipping...")
                    }

                    // Manually commit the offset, telling Redpanda this
                    // specific message is "Done".
                    if let Err(e) = consumer.commit_message(&msg, CommitMode::Async) {
                        eprint!("Failed to commit offset: {}", e)
                    }
                }
            }
        }
    }
}

// --- LIFECYCLE SUMMARY ---
// THE WAIT: Hits .recv().await and yields. The task pauses without consuming CPU cycles until a message is available.
// THE CATCH: The broker responds and the match block resolves the message into Ok(msg).
// THE WORK: Extracts the payload as a borrowed &str, referencing existing memory rather than copying data.
// THE RECEIPT: Calls commit_message to update the offset. This tells Redpanda: "Message #10 is finished; send #11 next."
// THE JUMP & REPEAT: The loop restarts instantly. It either grabs the next queued message or returns to The Wait state.

pub struct RedpandaProducer {
    brokers: String,
    group_id: String,
    topic: String,
}

impl RedpandaProducer {
    pub fn new(brokers: &str, group_id: &str, topic: &str) -> Self {
        Self {
            brokers: brokers.to_string(),
            group_id: group_id.to_string(),
            topic: topic.to_string(),
        }
    }

    pub async fn start_event_producer(self, outbound_rx: mpsc::Receiver<Vec<(MatchEvent, u64, u64)>>) {
        println!("Here:::::! start_event_producer");
        let producer: FutureProducer = ClientConfig::new()
            .set("bootstrap.servers", &self.brokers)
            .set("message.timeout.ms", "5000")
            .create()
            .expect("Producer creation error");

        let topic_name = self.topic.clone();
        let mut rx = outbound_rx;

        while let Some(event_batch) = rx.recv().await {
            for (internal_event, seq, timestamp) in event_batch {
                let bytes = ProtoMatchEvent::from((internal_event, seq, timestamp)).encode_to_vec();

                let record = FutureRecord::to(&topic_name).payload(&bytes).key("BTC-USD");

                if let Err((e, _)) = producer.send(record, Duration::from_secs(1)).await {
                    eprintln!("Failed to produce event to Redpanda: {:?}", e);
                }
            }
        }
    }
}
