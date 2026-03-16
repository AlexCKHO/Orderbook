use crate::infrastructure::redpanda::RedpandaConsumer;
use std::sync::Arc;
use tokio;

mod orderbook_grpc {
    tonic::include_proto!("orderbook");
}
mod infrastructure;
mod models;
mod services;

#[tokio::main]
async fn main() {
    let consumer = RedpandaConsumer::new(
        "localhost:9092",
        "matching_engine_btc",
        "engine-commands-topic",
    );

    let consumer_arc = Arc::new(consumer);
    consumer_arc.start().await;



    tokio::signal::ctrl_c().await.unwrap();
    println!("Shutting down gracefully...");
}
