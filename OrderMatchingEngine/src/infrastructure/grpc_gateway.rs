use crate::models::engine_payload::EnginePayload;
use crate::models::order::EngineAction;
use crate::orderbook_grpc::matching_engine_server::MatchingEngine;
use crate::orderbook_grpc::{
    EngineBatchCommand, MatchEvent as ProtoMatchEvent, OrderBatchResponse, SubscribeRequest,
};
use futures::Stream;
use quanta::{Clock, Instant};
use std::pin::Pin;
use std::time::{SystemTime, UNIX_EPOCH};
use tokio::sync::mpsc;
use tokio_stream::wrappers::ReceiverStream;
use tonic::{Request, Response, Status, Streaming};

pub struct GrpcGateway {
    inbound_tx: mpsc::Sender<EnginePayload>,
    clock: Clock,
    base_unix_nanos: i64,
    base_quanta: Instant,
}

impl GrpcGateway {
    pub fn new(inbound_tx: mpsc::Sender<EnginePayload>) -> Self {
        let clock = Clock::new();
        let base_quanta = clock.now();
        let base_unix_nanos = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .expect("Clock moved backwards")
            .as_nanos() as i64;

        Self {
            inbound_tx,
            clock,
            base_unix_nanos,
            base_quanta,
        }
    }

    fn get_unix_nanos(&self) -> i64 {
        let now_quanta = self.clock.now();
        let delta = now_quanta
            .saturating_duration_since(self.base_quanta)
            .as_nanos() as i64;
        self.base_unix_nanos + delta
    }
}
#[tonic::async_trait]
impl MatchingEngine for GrpcGateway {
    type PlaceBatchStreamStream =
        Pin<Box<dyn Stream<Item = Result<OrderBatchResponse, Status>> + Send>>;

    async fn place_batch_stream(
        &self,
        request: Request<Streaming<EngineBatchCommand>>,
    ) -> Result<Response<Self::PlaceBatchStreamStream>, Status> {
        let mut in_stream = request.into_inner();
        let tx = self.inbound_tx.clone();
        let (resp_tx, resp_rx) = mpsc::channel(100);

        // Clock set up
        let base_nanos = self.base_unix_nanos;
        let base_q = self.base_quanta;
        let clock = self.clock.clone();

        tokio::spawn(async move {
            println!("🔍 [DEBUG] Worker task started, waiting for first message...");

            loop {
                match in_stream.message().await {
                    Ok(Some(engine_batch_command)) => {
                        let capacity = engine_batch_command.commands.len();
                        // println!("📦 [DEBUG] Received batch! Size: {}", capacity);

                        let ingress_quanta = clock.now();
                        let delta =
                            ingress_quanta.saturating_duration_since(base_q).as_nanos() as i64;
                        let timestamp = base_nanos + delta;

                        let mut batch = Vec::with_capacity(capacity);

                        for (idx, command) in engine_batch_command.commands.into_iter().enumerate()
                        {
                            match EngineAction::try_from(command) {
                                Ok(action) => batch.push(action),
                                Err(e) => {
                                    eprintln!("⚠️ [PARSE ERROR] Batch index {}: ", idx);
                                }
                            }
                        }

                        let count = batch.len();
                        if count > 0 {
                            let (ack_tx, ack_rx) = tokio::sync::oneshot::channel();

                            if tx
                                .send(EnginePayload {
                                    actions: batch,
                                    reply_tx: Some(ack_tx),
                                    ingress_timestamp: timestamp
                                })
                                .await
                                .is_err()
                            {
                                eprintln!("🔥 [CRITICAL] Matching Engine channel closed!");
                                break;
                            }

                            let resp_tx_clone = resp_tx.clone();
                            //Acknowledge order request gets in orderbook
                            tokio::spawn(async move {
                                match ack_rx.await {
                                    Ok(acks) => {
                                        let reply = OrderBatchResponse {
                                            success: true,
                                            message: "Orders queued".into(),
                                            timestamp,
                                            acks,
                                        };
                                        let _ = resp_tx_clone.send(Ok(reply)).await;
                                    }
                                    Err(_) => {
                                        eprintln!("❌ [ERROR] Engine dropped the ack channel!");
                                    }
                                }
                            });
                        }
                    }
                    Ok(None) => {
                        println!("ℹ️ [DEBUG] Stream closed by client (Normal exit).");
                        break;
                    }
                    Err(status) => {
                        eprintln!(
                            "❌ [GRPC ERROR] Stream broken: CODE: {:?}, MSG: {}",
                            status.code(),
                            status.message()
                        );
                        break;
                    }
                }
            }
            println!("🔍 [DEBUG] Worker task exited.");
        });

        Ok(Response::new(Box::pin(ReceiverStream::new(resp_rx))))
    }

    type SubscribeEventsStream =
        Pin<Box<dyn Stream<Item = Result<ProtoMatchEvent, Status>> + Send>>;

    async fn subscribe_events(
        &self,
        request: Request<SubscribeRequest>,
    ) -> Result<Response<Self::SubscribeEventsStream>, Status> {
        todo!()
    }
}
