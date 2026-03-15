use rdkafka::consumer::StreamConsumer;
use rdkafka::error::KafkaError::ClientConfig;
use tokio;
use crate::services::matching_engine_service::MatchingEngineService;

mod orderbook_grpc {
    tonic::include_proto!("orderbook");
}
mod models;
mod services;
mod infrastructure;

#[tokio::main]
async fn main() {


    let addr = "localhost:9092";

    //B. Setting up the service
    // Here will call the MatchingEngineService::new() to initiate the OrderBook
    let service = MatchingEngineService::new();

    println!("🚀 Rust Matching Engine is listening on {}", addr);





}
