use tokio;

mod orderbook_grpc {
    tonic::include_proto!("orderbook");
}
mod models;
mod services;



#[tokio::main]
async fn main() {

    println!("Hello, world!");
}
