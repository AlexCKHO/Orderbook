use crate::models::order_book::OrderBook;
use std::time::{SystemTime, UNIX_EPOCH};
use tokio::sync::{mpsc, oneshot};
use tonic::{Request, Response, Status, Streaming};

use futures::Stream;
use std::pin::Pin;
use tokio_stream::wrappers::ReceiverStream;

// Ref to Generated Code  
use crate::orderbook_grpc::match_event::EventData;
use crate::orderbook_grpc::matching_engine_server::MatchingEngine;
use crate::orderbook_grpc::{
    self, OrderBatchRequest, OrderBatchResponse, OrderRequest, OrderResponse,
};

// Internal Models  
use crate::models::events::{MatchEvent as InternalEvent, MatchEvent};
use crate::models::order::{Order, OrderType, Side};

pub struct MatchingEngineService {
    sender: mpsc::Sender<OrderCommand>,
}

enum OrderCommand {
    PlaceOrder {
        order: Order,
        // Use oneshot channel, to send the result to gRPC handler  
        resp: oneshot::Sender<Vec<InternalEvent>>,
    },

    PlaceOrderBatch {
        orders: Vec<Order>,

        resp: oneshot::Sender<u64>,
    },
}

fn run_matching_actor(mut rx: mpsc::Receiver<OrderCommand>) {
    tokio::spawn(async move {
        let mut order_book = OrderBook::new();

        while let Some(cmd) = rx.recv().await {
            match cmd {
                OrderCommand::PlaceOrder { order, resp } => {
                    let events = order_book.add_order(order);

                    let _ = resp.send(events);
                }

                OrderCommand::PlaceOrderBatch { orders, resp } => {
                    let count = orders.len();

                    for order in orders {
                        order_book.add_order(order);
                    }
                    let _ = resp.send(count as u64);
                }
            }
        }
    });
}

impl MatchingEngineService {
    pub fn new() -> Self {
        let (tx, rx) = mpsc::channel(100_000);

        run_matching_actor(rx);

        Self { sender: tx }
    }

    fn parse_proto_order(req: OrderRequest) -> Result<Order, Status> {
        let side = match req.side {
            1 => Side::Bid,
            2 => Side::Ask,
            _ => return Err(Status::invalid_argument("Invalid Side")),
        };

        let order_type = match req.order_type {
            1 => OrderType::Limit,
            2 => OrderType::Market,
            _ => return Err(Status::invalid_argument("Invalid Order Type")),
        };

        let timestamp = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_nanos() as i64;

        Ok(Order {
            id: req.id,
            price: req.price,
            qty: req.qty,
            side,
            order_type,
            timestamp,
        })
    }

    fn parse_to_grpc_events(events: Vec<MatchEvent>) -> Vec<orderbook_grpc::MatchEvent> {
        let mut result = Vec::new();
        for event in events {
            let event_data = match event {
                InternalEvent::OrderPlaced {
                    id,
                    price,
                    qty,
                    side,
                } => EventData::Placed(orderbook_grpc::OrderPlaced {
                    id,
                    price,
                    qty,
                    side: match side {
                        Side::Bid => 1,
                        Side::Ask => 2,
                    },
                }),
                InternalEvent::TradeExecuted {
                    maker_id,
                    taker_id,
                    price,
                    qty,
                    timestamp,
                } => EventData::Filled(orderbook_grpc::TradeExecuted {
                    maker_id,
                    taker_id,
                    price,
                    qty,
                    timestamp: timestamp as i64,
                }),
                InternalEvent::OrderCancelled { id, cancelled_qty } => {
                    EventData::Cancelled(orderbook_grpc::OrderCancelled { id, cancelled_qty })
                }
                InternalEvent::OrderKilled { id, killed_qty } => {
                    EventData::Killed(orderbook_grpc::OrderKilled { id, killed_qty })
                }
            };
            result.push(orderbook_grpc::MatchEvent {
                event_data: Some(event_data),
            });
        }

        return result;
    }

    async fn process_single_order(
        sender: mpsc::Sender<OrderCommand>,
        req: OrderRequest,
    ) -> Result<OrderResponse, Status> {
        let order = Self::parse_proto_order(req)?;

        let (resp_tx, resp_rx) = oneshot::channel();
        let cmd = OrderCommand::PlaceOrder {
            order,
            resp: resp_tx,
        };

        if sender.send(cmd).await.is_err() {
            return Err(Status::internal("Engine dead"));
        }
        let internal_events = resp_rx.await.map_err(|_| Status::internal("Actor died"))?;

        let proto_events = Self::parse_to_grpc_events(internal_events);

        Ok(OrderResponse {
            success: true,
            message: "".to_string(),
            events: proto_events,
            request_id: req.id,
        })
    }

    async fn process_batch_orders(
        sender: mpsc::Sender<OrderCommand>,
        batch_req: OrderBatchRequest,
    ) -> Result<OrderBatchResponse, Status> {
        let mut orders = Vec::with_capacity(batch_req.orders.len());

        for req in batch_req.orders {
            if let Ok(order) = Self::parse_proto_order(req) {
                orders.push(order);
            }
        }

        if orders.is_empty() {
            return Ok(OrderBatchResponse {
                success: true,
                message: "".to_string(),
                processed_count: 0,
            });
        };

        let (resp_tx, resp_rx) = oneshot::channel();
        let cmd = OrderCommand::PlaceOrderBatch {
            orders,
            resp: resp_tx,
        };

        if sender.send(cmd).await.is_err() {
            return Err(Status::internal("Engine dead"));
        }

        let count = resp_rx.await.map_err(|_| Status::internal("Actor dead"))?;

        Ok(OrderBatchResponse {
            success: true,
            message: "".to_string(),
            processed_count: count,
        })
    }
}

#[tonic::async_trait]
impl MatchingEngine for MatchingEngineService {
    // 1. Single Request (Retained for compatibility)  
    async fn place_order(
        &self,
        request: Request<OrderRequest>,
    ) -> Result<Response<OrderResponse>, Status> {
        let resp = Self::process_single_order(self.sender.clone(), request.into_inner()).await?;

        Ok(Response::new(resp))
    }

    // 🔥 2. Bidirectional Streaming (Streaming Implementation)    
    type PlaceOrderStreamStream = Pin<Box<dyn Stream<Item = Result<OrderResponse, Status>> + Send>>;

    async fn place_order_stream(
        &self,
        request: Request<Streaming<OrderRequest>>,
    ) -> Result<Response<Self::PlaceOrderStreamStream>, Status> {
        println!("🌊 Streaming connection established...");
        let mut in_stream = request.into_inner();
        let actor_sender = self.sender.clone();
        let (tx, rx) = mpsc::channel(10000);

        tokio::spawn(async move {
            while let Ok(Some(req)) = in_stream.message().await {
                if let Ok(response) = Self::process_single_order(actor_sender.clone(), req).await {
                    if tx.send(Ok(response)).await.is_err() {
                        break;
                    }
                }
            }
            println!("👋 Stream closed");
        });

        Ok(Response::new(
            Box::pin(ReceiverStream::new(rx)) as Self::PlaceOrderStreamStream
        ))
    }

    type PlaceBatchStreamStream =
    Pin<Box<dyn Stream<Item = Result<OrderBatchResponse, Status>> + Send>>;
    async fn place_batch_stream(
        &self,
        request: Request<Streaming<OrderBatchRequest>>,
    ) -> Result<Response<Self::PlaceBatchStreamStream>, Status> {
        println!("📦 Batch Streaming connection established!");

        let mut in_stream = request.into_inner();
        let actor_sender = self.sender.clone();
        let (tx, rx) = mpsc::channel(100);

        tokio::spawn(async move {
            while let Ok(Some(batch_req)) = in_stream.message().await {
                if let Ok(response) =
                    Self::process_batch_orders(actor_sender.clone(), batch_req).await
                {
                    if tx.send(Ok(response)).await.is_err() {
                        break;
                    }
                }
            }
            println!("👋 Stream closed");
        });

        Ok(Response::new(
            Box::pin(ReceiverStream::new(rx)) as Self::PlaceBatchStreamStream
        ))
    }
}


#[cfg(test)]
mod performance_tests {
    use super::*;
    use rand::Rng;
    use std::time::Instant;

    fn generate_random_request(index: u64) -> OrderRequest {
        let mut rng = rand::thread_rng();
        OrderRequest {
            id: 1_000_000 + index,
            price: rng.gen_range(100..201),
            qty: rng.gen_range(1..100),
            side: if rng.gen_bool(0.5) { 1 } else { 2 },
            order_type: 1,
            timestamp: 0,
        }
    }

    #[tokio::test]
    async fn bench_local_engine_performance() {
        let service = MatchingEngineService::new();

        // Reduce to 100_000 if 1M takes too long during local testing  
        let iterations = 1_000_000;
        let mut requests = Vec::with_capacity(iterations);

        for i in 0..iterations as u64 {
            requests.push(generate_random_request(i));
        }

        println!("🚀 Starting Actor Benchmark with {} orders...", iterations);

        // Pre-allocate vector to store per-request latencies in nanoseconds  
        let mut latencies = Vec::with_capacity(iterations);

        let start = Instant::now();

        for req in requests {
            let req_start = Instant::now();

            let _ = MatchingEngineService::process_single_order(service.sender.clone(), req).await.unwrap();

            latencies.push(req_start.elapsed().as_nanos() as u64);
        }

        let duration = start.elapsed();
        let tps = (iterations as f64 / duration.as_secs_f64()) as u64;
        let avg_latency = duration.as_nanos() / iterations as u128;

        // Sort latencies to compute percentiles  
        // Using sort_unstable as it avoids allocation and is generally faster        latencies.sort_unstable();  

        let p50_ns = latencies[(iterations as f64 * 0.50) as usize];
        let p99_ns = latencies[(iterations as f64 * 0.99) as usize];
        let max_ns = *latencies.last().unwrap_or(&0);

        let p50_ms = p50_ns as f64 / iterations as f64;
        let p99_ms = p99_ns as f64 / iterations as f64;
        let max_ms = max_ns as f64 / iterations as f64;

        println!("-------------------------------------------");
        println!("🏁 Results:");
        println!("⏱️  Total Time: {:?}", duration);
        println!("📈 Throughput: {} TPS", tps);
        println!("📉 Avg Latency: {} ns per order", avg_latency);
        println!("📊 Latency (p50): {:.3} ms", p50_ms);
        println!("📊 Latency (p99): {:.3} ms", p99_ms);
        println!("📊 Latency (Max): {:.3} ms", max_ms);
        println!("-------------------------------------------");
    }
}