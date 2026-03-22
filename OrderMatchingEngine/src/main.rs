use crate::config::AppConfig;
use crate::infrastructure::redpanda::{RedpandaConsumer, RedpandaProducer};
use crate::models::events::MatchEvent;
use crate::orderbook_grpc::EngineCommand;
use crate::services::matching_engine_service::{MatchingEngineService, run_matching_actor};
use dotenvy::dotenv;
use std::env;
use std::sync::Arc;
use tokio;
use tokio::sync::mpsc;

mod orderbook_grpc {
    tonic::include_proto!("orderbook");
}
mod config;
mod infrastructure;
mod mappers;
mod models;
mod services;

#[tokio::main]
async fn main() {
    dotenv().ok();

    let cfg = AppConfig::from_env();

    // -- Setting up mpsc pipes for communication between engine and redpanda
    let (inbound_tx, inbound_rx) = mpsc::channel::<EngineCommand>(10000); // For incoming kafka message
    let (outbound_tx, outbound_rx) = mpsc::channel::<Vec<MatchEvent>>(10000); // For outgoing event to kafka

    // Setting up kafka consumer
    let consumer = RedpandaConsumer::new(&cfg.brokers, &cfg.group_id, &cfg.topic, inbound_tx);

    let consumer_arc = Arc::new(consumer);
    consumer_arc.start_event_consumer(cfg.concurrency).await;

    let producer = RedpandaProducer::new(&cfg.brokers, &cfg.group_id, &cfg.topic, outbound_rx);

    let producer_arc = Arc::new(producer);
    producer_arc.start_event_producer().await;

    let service = MatchingEngineService::new();

    tokio::signal::ctrl_c().await.unwrap();
    println!("Shutting down gracefully...");
}
