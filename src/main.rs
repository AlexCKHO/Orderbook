use tokio;
use tonic::transport::Server;
use crate::orderbook_grpc::matching_engine_server::MatchingEngineServer;
use crate::services::matching_engine_service::MatchingEngineService;

mod orderbook_grpc {
    tonic::include_proto!("orderbook");
}
mod models;
mod services;



#[tokio::main]
async fn main() -> Result<(), Box<dyn std::error::Error>> {

    println!("Hello, world!");

    // A. Setting Address (Localhost : Port 50051)
    let addr = "127.0.0.1:50051".parse()?;

    //B. Setting up the service
    // Here will call the MatchingEngineService::new() to initiate the OrderBook
    let service = MatchingEngineService::new();

    println!("🚀 Rust Matching Engine is listening on {}", addr);

    Server::builder()
        .add_service(MatchingEngineServer::new(service))
        .serve(addr)
        .await?;

    Ok(())

}
