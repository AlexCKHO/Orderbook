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

    let (inbound_tx, inbound_rx) = mpsc::channel::<EngineAction>(10000);
    let (outbound_tx, outbound_rx) = mpsc::channel::<MatchEvent>(10000);

    // 1. Initialize handles as Options or empty vectors outside the if blocks
    let mut consumer_tasks = Vec::new();
    let mut producer_handle = None;

    let use_redpanda_consumer = true; // Set your flags
    let use_redpanda_producer = true;

    // Setting up kafka consumer
    if use_redpanda_consumer {
        let consumer = Arc::new(RedpandaConsumer::new(
            &cfg.brokers,
            &cfg.group_id,
            &cfg.cmd_topic,
            inbound_tx.clone(),
        ));

        for i in 0..cfg.concurrency {
            let c = Arc::clone(&consumer);
            consumer_tasks.push(tokio::spawn(async move {
                c.start_event_consumer(i).await;
            }));
        }
    }

    // Setting up kafka producer
    if use_redpanda_producer {
        let producer = RedpandaProducer::new(&cfg.brokers, &cfg.group_id, &cfg.events_topic);
        producer_handle = Some(tokio::spawn(async move {
            producer.start_event_producer(outbound_rx).await;
        }));
    }

    // gRPC setup...
    if cfg.use_historical_data {
        let grpc_gateway = GrpcGateway::new(inbound_tx.clone());
        let service = MatchingEngineServer::new(grpc_gateway);
        let addr = cfg
            .historical_orders_grpc_addr
            .parse()
            .expect("Invalid GRPC addr");

        tokio::spawn(async move {
            let _ = Server::builder().add_service(service).serve(addr).await;
        });
    }

    let service = MatchingEngineService::new(outbound_tx);
    let engine_counter = Arc::clone(&tps_counter);
    let engine_handle = tokio::spawn(async move {
        service.run_matching_actor(inbound_rx, engine_counter).await;
    });

    // TPS Ticker
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

    // 2. Use futures::future::OptionFuture and handle the Vec of consumers
    let consumers_future = async {
        if !consumer_tasks.is_empty() {
            futures::future::select_all(consumer_tasks).await;
        } else {
            futures::future::pending::<()>().await; // Wait forever if disabled
        }
    };

    let producer_future = async {
        if let Some(h) = producer_handle {
            let _ = h.await;
        } else {
            futures::future::pending::<()>().await;
        }
    };

    println!("✅ Engine started. Press Ctrl+C to stop.");

    tokio::select! {
        _ = tokio::signal::ctrl_c() => {
            println!("Shutting down...");
        },
        _ = consumers_future => {
            eprintln!("A consumer worker exited.");
        },
        _ = producer_future => {
            eprintln!("Producer exited.");
        },
        res = engine_handle => {
            eprintln!("Engine exited: {:?}", res);
        },
    }
}
