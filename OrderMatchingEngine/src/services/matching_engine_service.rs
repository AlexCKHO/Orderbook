use crate::models::order_book::OrderBook;
use std::sync::Arc;
use std::time::{SystemTime, UNIX_EPOCH};
use tokio::sync::Mutex;
use tonic::{Code, Request, Response, Status};

// Ref to Generated Code
use crate::orderbook_grpc::match_event::EventData;
use crate::orderbook_grpc::matching_engine_server::MatchingEngine;
use crate::orderbook_grpc::{self, OrderRequest, OrderResponse};

// Ref to Internal Model
use crate::models::events::MatchEvent as InternalEvent;
use crate::models::order::{Order, OrderType, Side};

// Note: Orderbook on Tokio Integrating Sync Logic with Async Runtime
pub struct MatchingEngineService {
    engine: Arc<Mutex<OrderBook>>,
}

impl MatchingEngineService {
    pub fn new() -> Self {
        let order_book = OrderBook::new();
        Self {
            engine: Arc::new(Mutex::new(order_book)),
        }
    }
}

#[tonic::async_trait]
impl MatchingEngine for MatchingEngineService {
    async fn place_order(
        &self,
        request: Request<OrderRequest>,
    ) -> Result<Response<OrderResponse>, Status> {
        //--- A. Server Arrival Time in UNIX ---
        let timestamp = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_nanos() as i64;

        let req = request.into_inner();

        // --- B. Validation & Conversion (Proto -> Internal) ---
        let side = match req.side {
            1 => Side::Bid,
            2 => Side::Ask,
            _ => return Err(Status::invalid_argument("Invalid side")),
        };

        let order_type = match req.order_type {
            1 => OrderType::Limit,
            2 => OrderType::Market,
            _ => return Err(Status::invalid_argument("Invalid order type")),
        };

        if req.qty <= 0 {
            return Err(Status::invalid_argument("Quantity must be > 0"));
        }

        if order_type == OrderType::Limit && req.price <= 0 {
            return Err(Status::invalid_argument("Price must be > 0 for Limit Order"));
        }

        let order = Order {
            id: req.id,
            price: req.price,
            qty: req.qty,
            side,
            order_type,
            timestamp,
        };

        // --- C. Execution (The Engine) ---
        let mut engine = self.engine.lock().await;
        let internal_events = engine.add_order(order);

        // --- D. Translation (Internal -> Proto) ---
        let mut proto_events = Vec::new();

        for event in internal_events {
            let event_data = match event {
                // 1. Order Placed
                InternalEvent::OrderPlaced { id, price, qty, side } => {
                    EventData::Placed(orderbook_grpc::OrderPlaced {
                        id,
                        price,
                        qty,
                        side: match side {
                            Side::Bid => 1,
                            Side::Ask => 2,
                        }
                    })
                },

                // 2. Trade Executed
                InternalEvent::TradeExecuted { maker_id, taker_id, price, qty, timestamp } => {
                    EventData::Filled(orderbook_grpc::TradeExecuted {
                        maker_id,
                        taker_id,
                        price,
                        qty,
                        timestamp: timestamp as i64,
                    })
                }

                //3. Order Cancelled
                InternalEvent::OrderCancelled {id, cancelled_qty} => {
                    EventData::Cancelled(orderbook_grpc::OrderCancelled {
                        id,
                        cancelled_qty,
                    })
                }

                //4. Order Killed
                InternalEvent::OrderKilled {id, killed_qty} => {

                    EventData::Killed(orderbook_grpc::OrderKilled{
                        id,
                        killed_qty,
                    })
                }

            };
            proto_events.push(orderbook_grpc::MatchEvent {
                event_data: Some(event_data),
            });
        }
        // --- E. Response ---
        let reply = OrderResponse {
            success: true,
            message: "".to_string(), //Empty when success
            events: proto_events,    // repeated list
        };

        Ok(Response::new(reply))

    }
}
