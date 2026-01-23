use tonic::{transport::Server, Request, Response, Status};
use tokio;
use std::sync;
use std::time;
mod models;
pub mod orderbook_grpc {
    tonic::include_proto!("orderbook");
}

#[tokio::main]
async fn main() {

    println!("Hello, world!");
}
