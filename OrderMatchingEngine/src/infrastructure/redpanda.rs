use crate::models::events::MatchEvent;
use crate::orderbook_grpc::MatchEvent as ProtoMatchEvent;
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
}

impl RedpandaConsumer {
    pub fn new(brokers: &str, group_id: &str, topic: &str) -> Self {
        Self {
            brokers: brokers.to_string(),
            group_id: group_id.to_string(),
            topic: topic.to_string(),
        }
    }

    pub async fn start_event_consumer(self: Arc<Self>) {
        let consumer: StreamConsumer = ClientConfig::new()
            .set("bootstrap.servers", &self.brokers)
            // Group ID evenly distributes partitions across all consumer instances
            // subscribed to the same topic.
            .set("group.id", &self.group_id)
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
            .subscribe(&[&self.topic])
            .expect("Failed to subscribe to topic");

        println!("Started Redpanda worker for topic: {}", self.topic);

        // `tokio::spawn` runs this block as an asynchronous background task.
        // `move` transfers ownership of 'consumer' to the background task,
        // ensuring it lives as long as the task runs without lifetime issues.
        tokio::spawn(async move {
            // Infinite loop to continuously process incoming messages.
            loop {
                // recv() polls the broker for new messages.
                // .await yields execution to the Tokio runtime while waiting,
                // freeing up the thread for other tasks.
                match consumer.recv().await {
                    Err(e) => eprint!("Kafka error: {}", e),
                    Ok(msg) => {
                        // Treat the incoming raw message bytes as a string slice (&str).
                        let payload = msg.payload_view::<str>().unwrap_or(Ok("")).unwrap_or("");
                        println!("Processing: {}", payload);

                        // Manually commit the offset, telling Redpanda this
                        // specific message is "Done".
                        if let Err(e) = consumer.commit_message(&msg, CommitMode::Async) {
                            eprint!("Failed to commit offset: {}", e)
                        }
                    }
                }
            }
        });
    }
}

// --- LIFECYCLE SUMMARY ---
// THE WAIT: Hits .recv().await and yields. The task pauses without consuming CPU cycles until a message is available.
// THE CATCH: The broker responds and the match block resolves the message into Ok(msg).
// THE WORK: Extracts the payload as a borrowed &str, referencing existing memory rather than copying data.
// THE RECEIPT: Calls commit_message to update the offset. This tells Redpanda: "Message #10 is finished; send #11 next."
// THE JUMP & REPEAT: The loop restarts instantly. It either grabs the next queued message or returns to The Wait state.

pub async fn start_event_producer(
    brokers: &str,
    topic: &str,
    mut outbound_rx: mpsc::Receiver<Vec<MatchEvent>>,
) {
    let producer: FutureProducer = ClientConfig::new()
        .set("bootstrap.servers", brokers)
        .set("message.timeout.ms", "5000")
        .create()
        .expect("Producer creation error");

    let topic = topic.to_string();

    tokio::spawn(async move {
        while let Some(events) = outbound_rx.recv().await {
            for proto_event in events {
                let bytes = ProtoMatchEvent::from(proto_event).encode_to_vec();

                let record = FutureRecord::to(&topic).payload(&bytes).key("BTC-USD");

                let _ = producer.send(record, Duration::from_secs(0)).await;
            }
        }
    });
}
