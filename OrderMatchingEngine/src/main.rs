use crate::config::AppConfig;
use crate::infrastructure::redpanda::{RedpandaConsumer, RedpandaProducer};
use crate::models::events::MatchEvent;
use crate::models::order::EngineAction;
use crate::services::matching_engine_service::MatchingEngineService;
use dotenvy::dotenv;
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
    let (inbound_tx, inbound_rx) = mpsc::channel::<EngineAction>(10000); // For incoming kafka message
    let (outbound_tx, outbound_rx) = mpsc::channel::<Vec<MatchEvent>>(10000); // For outgoing event to kafka

    // Setting up kafka consumer
    let consumer = RedpandaConsumer::new(&cfg.brokers, &cfg.group_id, &cfg.cmd_topic, inbound_tx);

    let consumer_arc = Arc::new(consumer);
    consumer_arc.start_event_consumer(cfg.concurrency).await;

    let producer = RedpandaProducer::new(&cfg.brokers, &cfg.group_id, &cfg.events_topic);

    let producer_arc = producer;
    producer_arc.start_event_producer(outbound_rx).await;

    let service = MatchingEngineService::new(outbound_tx);

    tokio::spawn(async move {
        service.run_matching_actor(inbound_rx);
    });

    tokio::signal::ctrl_c().await.unwrap();
    println!("Shutting down gracefully...");
}
