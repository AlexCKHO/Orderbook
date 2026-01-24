use tokio;
mod models;
mod services;

pub mod orderbook_grpc {
    tonic::include_proto!("orderbook");
}

#[tokio::main]

async fn main() {

    println!("Hello, world!");
}
