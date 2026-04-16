use crate::config::AppConfig;
use crate::infrastructure::grpc_gateway::GrpcGateway;
use crate::infrastructure::redpanda_gateway::{RedpandaConsumer, RedpandaProducer};
use crate::models::events::MatchEvent;
use crate::services::dispatcher_service::DispatcherService;
use crate::services::matching_engine_service::MatchingEngineService;
use dotenvy::dotenv;
use std::sync::Arc;
use std::sync::atomic::AtomicU64;
use tokio;
use tokio::sync::mpsc;
use tonic::transport::Server;

mod orderbook_grpc {
    tonic::include_proto!("orderbook");
}
use crate::models::engine_payload::EnginePayload;
use orderbook_grpc::matching_engine_server::MatchingEngineServer;

mod config;
mod infrastructure;
mod mappers;
mod models;
mod services;
mod system;

#[tokio::main]
async fn main() {
    dotenv().ok();

    let cfg = AppConfig::from_env();
    let tps_counter = Arc::new(AtomicU64::new(0));

    // main.rs
    let (inbound_tx, inbound_rx) = mpsc::channel::<EnginePayload>(128);
    let (dispatcher_tx, mut dispatcher_rx) = mpsc::channel::<Vec<(MatchEvent, u64)>>(100_000);
    let (kafka_tx, kafka_rx) = mpsc::channel::<Vec<(MatchEvent, u64)>>(100_000);

    // 1. Initialize handles as Options or empty vectors outside the if blocks
    let mut consumer_tasks = Vec::new();
    let mut producer_handle = None;
    let mut dispatcher_handle = None;

    // Receiving message from Kafka
    let use_redpanda_consumer = true;
    // Sending message to Kafka
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
            producer.start_event_producer(kafka_rx).await;
        }));

        let dispatcher = DispatcherService::new(kafka_tx);
        dispatcher_handle = Some(tokio::spawn(async move {
            dispatcher.run(dispatcher_rx).await;
        }));
    } else {
        tokio::spawn(async move { while let Some(_) = dispatcher_rx.recv().await {} });
    }

    // gRPC setup...
    if cfg.use_historical_data {
        let grpc_gateway = GrpcGateway::new(inbound_tx.clone());
        let service = MatchingEngineServer::new(grpc_gateway)
            .max_decoding_message_size(32 * 1024 * 1024)
            .max_encoding_message_size(32 * 1024 * 1024);

        let addr = cfg
            .historical_orders_grpc_addr
            .parse()
            .expect("Invalid GRPC addr");

        tokio::spawn(async move {
            let _ = Server::builder().add_service(service).serve(addr).await;
        });
    }

    let service = MatchingEngineService::new(dispatcher_tx);
    let engine_counter = Arc::clone(&tps_counter);


    let engine_thread = std::thread::spawn(move || {

        service.run_matching_actor(inbound_rx, engine_counter);
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

    let dispatcher_future = async {
        if let Some(h) = dispatcher_handle {
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
        _ = dispatcher_future => {
            eprintln!("Dispatcher exited.");
        },
    }
}
