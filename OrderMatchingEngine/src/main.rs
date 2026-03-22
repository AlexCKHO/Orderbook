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
    let mut handles = Vec::new();
    for i in 0..cfg.concurrency {
        let c = Arc::clone(&consumer_arc);
        handles.push(tokio::spawn(async move {
            c.start_event_consumer(i).await;
        }));
    }

    // Setting up kafka producer
    let producer = RedpandaProducer::new(&cfg.brokers, &cfg.group_id, &cfg.events_topic);
    let producer_handle = tokio::spawn(async move {
        producer.start_event_producer(outbound_rx).await;
    });

    let service = MatchingEngineService::new(outbound_tx);
    let engine_handle = tokio::spawn(async move {
        service.run_matching_actor(inbound_rx).await;
    });

    // 喺 main 尾部
    tokio::select! {
        _ = tokio::signal::ctrl_c() => {
            println!("Received Ctrl+C, shutting down...");
        },
        res = futures::future::select_all(handles) => {
            eprintln!("One of the consumer workers died: {:?}", res);
        },
        res = producer_handle => {
            eprintln!("Producer task exited unexpectedly: {:?}", res);
        },
        res = engine_handle => {
            eprintln!("Engine task exited unexpectedly: {:?}", res);
        },
    }
}
