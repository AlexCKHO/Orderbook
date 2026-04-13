use crate::models::order::EngineAction;

use crate::models::engine_payload::EnginePayload;
use crate::orderbook_grpc::matching_engine_server::MatchingEngine;
use crate::orderbook_grpc::{
    EngineBatchCommand, MatchEvent as ProtoMatchEvent, OrderBatchResponse, SubscribeRequest,
};
use futures::Stream;
use std::pin::Pin;
use tokio::sync::mpsc;
use tokio_stream::wrappers::ReceiverStream;
use tonic::{Request, Response, Status, Streaming};

pub struct GrpcGateway {
    inbound_tx: mpsc::Sender<EnginePayload>,
}
impl GrpcGateway {
    pub fn new(inbound_tx: mpsc::Sender<EnginePayload>) -> Self {
        Self { inbound_tx }
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

        tokio::spawn(async move {
            println!("🔍 [DEBUG] Worker task started, waiting for first message...");

            loop {
                match in_stream.message().await {
                    Ok(Some(engine_batch_command)) => {
                        let capacity = engine_batch_command.commands.len();
                        // println!("📦 [DEBUG] Received batch! Size: {}", capacity);

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
                                })
                                .await
                                .is_err()
                            {
                                eprintln!("🔥 [CRITICAL] Matching Engine channel closed!");
                                break;
                            }

                            let resp_tx_clone = resp_tx.clone();

                            tokio::spawn(async move {
                                match ack_rx.await {
                                    Ok(acks) => {
                                        let reply = OrderBatchResponse {
                                            success: true,
                                            message: "Orders queued".into(),
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
