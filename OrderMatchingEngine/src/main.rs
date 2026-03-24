use crate::config::AppConfig;
// use crate::infrastructure::redpanda_gateway::{RedpandaConsumer, RedpandaProducer};
use crate::infrastructure::grpc_gateway::GrpcGateway;
use crate::infrastructure::redpanda_gateway::{RedpandaConsumer, RedpandaProducer};
use crate::models::events::MatchEvent;
use crate::models::order::EngineAction;
use crate::services::matching_engine_service::MatchingEngineService;
use dotenvy::dotenv;
use std::sync::Arc;
use std::sync::atomic::{AtomicU64, Ordering};
use std::time::Duration;
use tokio;
use tokio::sync::mpsc;
use tokio::time::interval;
use tonic::transport::Server;

mod orderbook_grpc {
    tonic::include_proto!("orderbook");
}
use orderbook_grpc::matching_engine_server::MatchingEngineServer;
mod config;
mod infrastructure;
mod mappers;
mod models;
mod services;

#[tokio::main]
async fn main() {
    dotenv().ok();

    let cfg = AppConfig::from_env();
    let tps_counter = Arc::new(AtomicU64::new(0));

    // -- Setting up mpsc pipes for communication between engine and redpanda
    let (inbound_tx, inbound_rx) = mpsc::channel::<EngineAction>(10000); // For incoming kafka message
    let (outbound_tx, outbound_rx) = mpsc::channel::<MatchEvent>(10000); // For outgoing event to kafka

    let use_redpanda_consumer = false;
    let use_redpanda_producer = false;

    // Setting up kafka consumer
    if use_redpanda_consumer {
        let consumer = RedpandaConsumer::new(
            &cfg.brokers,
            &cfg.group_id,
            &cfg.cmd_topic,
            inbound_tx.clone(),
        );
        let consumer_arc = Arc::new(consumer);
        let mut consumer_handle = Vec::new();
        for i in 0..cfg.concurrency {
            let c = Arc::clone(&consumer_arc);
            consumer_handle.push(tokio::spawn(async move {
                c.start_event_consumer(i).await;
            }));
        }
    }

    // Setting up kafka producer

    if use_redpanda_producer {
        let producer = RedpandaProducer::new(&cfg.brokers, &cfg.group_id, &cfg.events_topic);
        let producer_handle = tokio::spawn(async move {
            producer.start_event_producer(outbound_rx).await;
        });
    }

    // Setting up gRPC for replaying historical data
    if cfg.use_historical_data {
        let grpc_gateway = GrpcGateway::new(inbound_tx.clone());
        let service = MatchingEngineServer::new(grpc_gateway);

        let addr = cfg
            .historical_orders_grpc_addr
            .parse()
            .expect("Failed to parse GRPC address. Check your .env file!");

        println!("📡 gRPC Server listening on {}", addr);

        let _ = tokio::spawn(async move {
            if let Err(e) = Server::builder().add_service(service).serve(addr).await {
                eprintln!("🔥 gRPC Server error: {}", e);
            }
        });
    }

    // Setting up MatchingEngineService
    let service = MatchingEngineService::new(outbound_tx);
    let engine_counter = Arc::clone(&tps_counter);
    let engine_handle = tokio::spawn(async move {
        service.run_matching_actor(inbound_rx, engine_counter).await;
    });

    let ticker_counter = Arc::clone(&tps_counter);
    tokio::spawn(async move {
        let mut ticker = interval(Duration::from_secs(1));
        let mut last_count = 0;
        loop {
            ticker.tick().await;
            let current_count = ticker_counter.load(Ordering::Relaxed);
            let tps = current_count - last_count;
            if tps > 0 {
                println!("🚀 Current TPS: {}", tps);
            }
            last_count = current_count;
        }
    });

    tokio::select! {
        _ = tokio::signal::ctrl_c() => {
            println!("Received Ctrl+C, shutting down...");
        },
        res = futures::future::select_all(consumer_handle) => {
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
