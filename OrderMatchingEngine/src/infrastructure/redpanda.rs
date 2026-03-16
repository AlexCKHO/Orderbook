use rdkafka::config::ClientConfig;
use rdkafka::consumer::{CommitMode, Consumer, StreamConsumer};
use std::sync::Arc;
use rdkafka::Message;

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
    pub async fn start(self: Arc<Self>) {
        let consumer: StreamConsumer = ClientConfig::new()
            .set("bootstrap.servers", &self.brokers)
            // Group id for evenly distributing the message across all instances
            // subscribed to the same topic
            .set("group.id", &self.group_id)
            // Instead of the
            .set("enable.auto.commit", "false")

            .set("auto.offset.reset", "earliest")
            .create()
            .expect("Consumer creation failed");

        consumer
            .subscribe(&[&self.topic])
            .expect("Failed to subscribe to topic");

        println!("Started Redpanda worker for topic: {}", self.topic);

        tokio::spawn(async move {
            loop {
                match consumer.recv().await {
                    Err(e) => eprint!("Kafka error:{}", e),
                    Ok(msg) => {

                        let payload = msg.payload_view::<str>().unwrap_or(Some("")).unwrap_or("");
                        println!("Processing: {}", payload);

                        if let Err(e) = consumer.commit_message(&msg, CommitMode::Async) {
                            eprint!("Failed to commit offset: {}", e)
                        }
                    }
                }
            }
        })
    }
}
