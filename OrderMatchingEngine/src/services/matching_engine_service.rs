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
    self, CancelRequest, EngineBatchCommand, EngineCommand, OrderBatchResponse, OrderRequest,
    OrderResponse,
};

// Internal Models
use crate::models::events::{CancelRejectReason, MatchEvent as InternalEvent, MatchEvent};
use crate::models::order::{CancelEntry, EngineAction, OrderEntry, OrderType, Side};
use crate::orderbook_grpc::engine_command::Command;

pub struct MatchingEngineService {
    sender: mpsc::Sender<OrderCommand>,
}

enum OrderCommand {
    PlaceOrder {
        commands: EngineAction,
        resp: oneshot::Sender<Vec<InternalEvent>>,
    },

    // Retain original batch order (for legacy code reference)
    // PlaceOrderBatch {
    //     commands: Vec<EngineAction>,
    //     resp: oneshot::Sender<u64>,
    // },

    // Dedicated lock-free command for high-frequency streaming
    PlaceOrderBatchStream {
        commands: Vec<EngineAction>,
        responder: mpsc::Sender<Result<OrderBatchResponse, Status>>,
    },
}

fn run_matching_actor(mut rx: mpsc::Receiver<OrderCommand>) {
    tokio::spawn(async move {
        let mut order_book = OrderBook::new();

        while let Some(cmd) = rx.recv().await {
            match cmd {
                //todo make use of events
                OrderCommand::PlaceOrder { commands, resp } => {
                    let events = order_book.process_single(commands);
                    let _ = resp.send(events);
                }

                // OrderCommand::PlaceOrderBatch { commands, resp } => {
                //     let count = commands.len();
                //     order_book.process_batch(commands);
                //     let _ = resp.send(count as u64);
                // }

                // Asynchronous pipeline processing
                OrderCommand::PlaceOrderBatchStream {
                    commands,
                    responder,
                } => {
                    let count = commands.len();

                    // 1. Synchronous matching (CPU bound, avoids context switching)
                    order_book.process_batch(commands);

                    // 2. Prepare response
                    let resp = OrderBatchResponse {
                        success: true,
                        message: "".to_string(),
                        processed_count: count as u64,
                    };

                    // 3. Fire-and-forget: send directly to the gRPC response channel.
                    // Using try_send ensures the actor is never blocked by await.
                    // As long as the channel has capacity, this takes nanoseconds.
                    let _ = responder.try_send(Ok(resp));
                }
            }
        }
    });
}

impl MatchingEngineService {
    pub fn new() -> Self {
        // Architecture Note:
        // We intentionally use a very small bounded channel (e.g., buffer = 4 or 16) for the Actor mailbox.
        // During stress testing (7M+ TPS), a large buffer (e.g., 1024) caused severe bufferbloat
        // and latency spikes due to queueing delays.
        // A small buffer naturally enforces TCP/HTTP2 backpressure onto the gRPC clients,
        // keeping tail latencies (p99) strictly under 35ms while maintaining maximum CPU throughput.
        let (tx, rx) = mpsc::channel(4);

        run_matching_actor(rx);

        Self { sender: tx }
    }

    fn parse_proto_order(req: OrderRequest) -> Result<EngineAction, Status> {
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

        Ok(EngineAction::Create(OrderEntry {
            id: req.id,
            price: req.price,
            qty: req.qty,
            side,
            order_type,
            timestamp,
        }))
    }
    fn parse_proto_cancel_request(req: CancelRequest) -> Result<EngineAction, Status> {
        Ok(EngineAction::Cancel(CancelEntry { id: req.id }))
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
                    timestamp,
                }),
                InternalEvent::OrderCancelled { id, cancelled_qty } => {
                    EventData::Cancelled(orderbook_grpc::OrderCancelled { id, cancelled_qty })
                }
                InternalEvent::OrderKilled { id, killed_qty } => {
                    EventData::Killed(orderbook_grpc::OrderKilled { id, killed_qty })
                }

                InternalEvent::CancelRejected { id, reason } => {
                    EventData::Rejected(orderbook_grpc::CancelRejected {
                        id,
                        reason: match reason {
                            CancelRejectReason::OrderNotFound => 1,
                        },
                    })
                }
            };
            result.push(orderbook_grpc::MatchEvent {
                event_data: Some(event_data),
            });
        }

        result
    }

    async fn process_single_order(
        sender: mpsc::Sender<OrderCommand>,
        req: EngineCommand,
    ) -> Result<OrderResponse, Status> {
        let (engine_action, request_id) = match req.command.unwrap() {
            Command::PlaceOrder(order_entry) => {
                let id = order_entry.id;
                let action = Self::parse_proto_order(order_entry)
                    .map_err(|_| Status::invalid_argument("Failed to map Order"))?;
                (action, id)
            }
            Command::CancelOrder(cancel_entry) => {
                let id = cancel_entry.id.clone();
                let action = Self::parse_proto_cancel_request(cancel_entry)
                    .map_err(|_| Status::invalid_argument("Failed to map Cancel"))?;
                (action, id)
            }
        };

        let (resp_tx, resp_rx) = oneshot::channel();

        let cmd = OrderCommand::PlaceOrder {
            commands: engine_action,
            resp: resp_tx,
        };

        if sender.send(cmd).await.is_err() {
            return Err(Status::internal("Engine offline"));
        }
        let internal_events = resp_rx
            .await
            .map_err(|_| Status::internal("Actor offline"))?;

        let proto_events = Self::parse_to_grpc_events(internal_events);

        Ok(OrderResponse {
            success: true,
            message: "Order processed successfully".to_string(),
            events: proto_events,
            request_id,
        })
    }
}

#[tonic::async_trait]
impl MatchingEngine for MatchingEngineService {
    // 2. Bidirectional Streaming (Streaming Implementation)
    type PlaceOrderStreamStream = Pin<Box<dyn Stream<Item = Result<OrderResponse, Status>> + Send>>;

    async fn place_order_stream(
        &self,
        request: Request<Streaming<EngineCommand>>,
    ) -> Result<Response<Self::PlaceOrderStreamStream>, Status> {
        println!("Streaming connection established...");
        let mut in_stream = request.into_inner();
        let actor_sender = self.sender.clone();

        let (tx, rx) = mpsc::channel(1000);

        tokio::spawn(async move {
            while let Ok(Some(req)) = in_stream.message().await {
                if let Ok(response) = Self::process_single_order(actor_sender.clone(), req).await {
                    if tx.send(Ok(response)).await.is_err() {
                        break;
                    }
                }
            }
            println!("Stream closed");
        });

        Ok(Response::new(
            Box::pin(ReceiverStream::new(rx)) as Self::PlaceOrderStreamStream
        ))
    }
    type PlaceBatchStreamStream =
        Pin<Box<dyn Stream<Item = Result<OrderBatchResponse, Status>> + Send>>;

    async fn place_batch_stream(
        &self,
        request: Request<Streaming<EngineBatchCommand>>,
    ) -> Result<Response<Self::PlaceBatchStreamStream>, Status> {
        println!("[ASYNC PIPELINE] Batch streaming connection established.");

        let mut in_stream = request.into_inner();
        let actor_sender = self.sender.clone();

        // Key: Allocate a large buffer (e.g., 10,000).
        // This ensures the channel isn't overwhelmed when the actor
        // instantly returns thousands of processed orders.
        let (tx, rx) = mpsc::channel(10_000);

        tokio::spawn(async move {
            // Receiver task: Dedicated to handling Network I/O
            while let Ok(Some(batch_req)) = in_stream.message().await {
                let commands = Vec::with_capacity(batch_req.commands.len());

                // for req in batch_req.commands {
                //     if let Ok(command) = Self::parse_proto_order(req) {
                //         commands.push(command);
                //     }
                // }

                let batch_size = commands.len();
                if batch_size > 0 {
                    // Construct new command, bundling the response channel 'tx'
                    let cmd = OrderCommand::PlaceOrderBatchStream {
                        commands,
                        responder: tx.clone(),
                    };

                    // let max_cap = actor_sender.max_capacity();
                    // let current_avail = actor_sender.capacity();
                    // let queue_size = max_cap - current_avail;

                    // Log warning if the batch queue exceeds threshold
                    // if queue_size > 10 {
                    //     println!("[CONGESTION WARNING] Actor mailbox is filling up! Queued batches: {} / {}", queue_size, max_cap);
                    // } else {
                    //     println!("[CLEAR] Current queue size: {} / {}", queue_size, max_cap);
                    // }

                    // Send to actor queue.
                    // Loop immediately to read the next batch without waiting for the result.
                    if actor_sender.send(cmd).await.is_err() {
                        eprintln!("Engine actor offline");
                        break;
                    }
                }
            }
            println!("Stream closed");
        });

        Ok(Response::new(
            Box::pin(ReceiverStream::new(rx)) as Self::PlaceBatchStreamStream
        ))
    }
}
