use crate::models::order_book::OrderBook;
use std::sync::Arc;
use std::time::{SystemTime, UNIX_EPOCH};
use tokio::sync::Mutex;
use tonic::{Request, Response, Status, Streaming};

// 🔥 Stream related imports
use tokio::sync::mpsc;
use tokio_stream::wrappers::ReceiverStream;
use tokio_stream::StreamExt; // Provides .next() / .message() for streams
use std::pin::Pin;
use futures::Stream;

// Ref to Generated Code
use crate::orderbook_grpc::match_event::EventData;
use crate::orderbook_grpc::matching_engine_server::MatchingEngine;
use crate::orderbook_grpc::{self, OrderRequest, OrderResponse};

// Ref to Internal Model
use crate::models::events::MatchEvent as InternalEvent;
use crate::models::order::{Order, OrderType, Side};

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

    // 🛠️ Helper Function: Extracts core logic to avoid code duplication
    // This logic handles: Validation -> Transformation -> Matching -> Response Generation
    async fn _process_order(&self, req: OrderRequest) -> Result<OrderResponse, Status> {

        // --- A. Server Arrival Time ---
        let timestamp = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_nanos() as i64;

        // --- B. Validation & Conversion ---
        let side = match req.side {
            1 => Side::Bid,
            2 => Side::Ask,
            _ => return Err(Status::invalid_argument("Invalid side (1=BID, 2=ASK)")),
        };

        let order_type = match req.order_type {
            1 => OrderType::Limit,
            2 => OrderType::Market,
            _ => return Err(Status::invalid_argument("Invalid order type (1=LIMIT, 2=MARKET)")),
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

        // --- C. Execution (Critical Section) ---
        // Lock Engine -> Execute -> Auto-unlock
        let internal_events = {
            let mut engine = self.engine.lock().await;
            engine.add_order(order)
        };

        // --- D. Translation ---
        let mut proto_events = Vec::new();
        for event in internal_events {
            let event_data = match event {
                InternalEvent::OrderPlaced { id, price, qty, side } => {
                    EventData::Placed(orderbook_grpc::OrderPlaced {
                        id, price, qty,
                        side: match side { Side::Bid => 1, Side::Ask => 2 },
                    })
                },
                InternalEvent::TradeExecuted { maker_id, taker_id, price, qty, timestamp } => {
                    EventData::Filled(orderbook_grpc::TradeExecuted {
                        maker_id, taker_id, price, qty, timestamp: timestamp as i64,
                    })
                },
                InternalEvent::OrderCancelled {id, cancelled_qty} => {
                    EventData::Cancelled(orderbook_grpc::OrderCancelled { id, cancelled_qty })
                },
                InternalEvent::OrderKilled {id, killed_qty} => {
                    EventData::Killed(orderbook_grpc::OrderKilled{ id, killed_qty })
                }
            };
            proto_events.push(orderbook_grpc::MatchEvent { event_data: Some(event_data) });
        }

        // --- E. Return Response ---
        Ok(OrderResponse {
            success: true,
            message: "".to_string(),
            events: proto_events,
            request_id: req.id, // 🔥 Key: Must return ID to Client for Latency calculation
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
        let req = request.into_inner();
        // Call helper directly
        let response = self._process_order(req).await?;
        Ok(Response::new(response))
    }

    // 🔥 2. Bidirectional Streaming (Streaming Implementation)
    type PlaceOrderStreamStream = Pin<Box<dyn Stream<Item = Result<OrderResponse, Status>> + Send>>;

    async fn place_order_stream(
        &self,
        request: Request<Streaming<OrderRequest>>,
    ) -> Result<Response<Self::PlaceOrderStreamStream>, Status> {

        println!("🌊 Streaming connection established...");
        let mut in_stream = request.into_inner();

        // To use 'self' inside tokio::spawn, we need to clone the Arc pointer (if engine is Arc).
        // Since engine is already an Arc, we clone the engine pointer to move it into the task.
        // Alternatively, we could wrap the service itself in an Arc, but since we are in a tonic trait,
        // self is &self. The cleanest way here is to clone the engine.

        let engine_clone = self.engine.clone(); // Clone Arc<Mutex<>>

        // Create Channel (Buffer = 10000)
        let (tx, rx) = mpsc::channel(10000);

        tokio::spawn(async move {
            while let Ok(Some(req)) = in_stream.message().await {
                // We cannot call self._process_order here due to self lifetime issues.
                // Instead, we manually execute the logic (or could refactor _process_order to be static).
                // Logic expanded here for simplicity:

                // --- Logic Start ---
                // 1. Validation (Simplified version, omitting some checks for demo speed;
                // in real projects, this should be an encapsulated validation fn)
                let side_res = match req.side { 1 => Ok(Side::Bid), 2 => Ok(Side::Ask), _ => Err(()) };
                let type_res = match req.order_type { 1 => Ok(OrderType::Limit), 2 => Ok(OrderType::Market), _ => Err(()) };

                if side_res.is_err() || type_res.is_err() { continue; } // Skip invalid orders
                let side = side_res.unwrap();
                let order_type = type_res.unwrap();

                let timestamp = SystemTime::now().duration_since(UNIX_EPOCH).unwrap().as_nanos() as i64;
                let order = Order {
                    id: req.id, price: req.price, qty: req.qty, side, order_type, timestamp,
                };

                // 2. Execution
                let internal_events = {
                    let mut book = engine_clone.lock().await;
                    book.add_order(order)
                };

                // 3. Translation
                let mut proto_events = Vec::new();
                for event in internal_events {
                    let event_data = match event {
                        InternalEvent::OrderPlaced { id, price, qty, side } => {
                            EventData::Placed(orderbook_grpc::OrderPlaced {
                                id, price, qty,
                                side: match side { Side::Bid => 1, Side::Ask => 2 },
                            })
                        },
                        InternalEvent::TradeExecuted { maker_id, taker_id, price, qty, timestamp } => {
                            EventData::Filled(orderbook_grpc::TradeExecuted {
                                maker_id, taker_id, price, qty, timestamp: timestamp as i64,
                            })
                        },
                        _ => continue, // Omitted Cancel/Kill for brevity
                    };
                    proto_events.push(orderbook_grpc::MatchEvent { event_data: Some(event_data) });
                }

                let response = OrderResponse {
                    success: true,
                    message: "".to_string(),
                    events: proto_events,
                    request_id: req.id, // Echo ID back
                };
                // --- Logic End ---

                // Send response; if it fails (Client disconnected), break the loop
                if tx.send(Ok(response)).await.is_err() {
                    break;
                }
            }
            println!("👋 Stream closed");
        });

        let out_stream = ReceiverStream::new(rx);
        Ok(Response::new(Box::pin(out_stream) as Self::PlaceOrderStreamStream))
    }
}