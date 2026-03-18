use crate::infrastructure::redpanda::RedpandaConsumer;
use crate::services::matching_engine_service::{MatchingEngineService, run_matching_actor};
use std::sync::Arc;
use tokio;
use tokio::sync::mpsc;

mod orderbook_grpc {
    tonic::include_proto!("orderbook");
}
mod infrastructure;
mod models;
mod services;
mod mappers;

#[tokio::main]
async fn main() {
    let consumer = RedpandaConsumer::new(
        "localhost:9092",
        "matching_engine_btc",
        "engine-commands-topic",
    );

    let consumer_arc = Arc::new(consumer);
    consumer_arc.start_event_consumer().await;

    let (tx, rx) = mpsc::channel(10000); // Input to Engine
    let (event_tx, event_rx) = mpsc::channel(10000);

    let service = MatchingEngineService::new();

    run_matching_actor(rx, event_tx);
    let engine_sender = tx.clone();
    tokio::spawn(async move {
        // inside your consumer loop:
        // let action = parse_kafka_bytes_to_action(payload);
        // engine_sender.send(OrderCommand::KafkaOrder { command: action }).await;
    });

    tokio::signal::ctrl_c().await.unwrap();
    println!("Shutting down gracefully...");
}
