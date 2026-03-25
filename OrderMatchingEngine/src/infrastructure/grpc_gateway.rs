use crate::models::order::EngineAction;
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
    inbound_tx: mpsc::Sender<Vec<EngineAction>>,
}
impl GrpcGateway {
    pub fn new(inbound_tx: mpsc::Sender<Vec<EngineAction>>) -> Self {
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
        let tx = self.inbound_tx.clone(); // 注意：呢度 tx 要轉做傳送 Vec<EngineAction>
        let (resp_tx, resp_rx) = mpsc::channel(100);

        tokio::spawn(async move {
            while let Ok(Some(engine_batch_command)) = in_stream.message().await {
                let capacity = engine_batch_command.commands.len();
                let mut batch = Vec::with_capacity(capacity); // ✅ 預先分配 RAM

                for command in engine_batch_command.commands {
                    if let Ok(action) = EngineAction::try_from(command) {
                        batch.push(action); // ✅ 儲埋一齊，唔好 await 住
                    } else {
                        eprintln!("⚠️ [WARNING] Failed to parse EngineCommand");
                    }
                }

                let count = batch.len();

                if count > 0 {
                    // ✅ 整個 Batch 只做 1 次 send (1 次 Context Switch)
                    if tx.send(batch).await.is_err() {
                        eprintln!("🔥 [CRITICAL] Matching Engine channel closed!");
                        let _ = resp_tx.send(Err(Status::internal("Engine offline"))).await;
                        break;
                    }
                }

                let reply = OrderBatchResponse {
                    success: true,
                    message: "Orders queued".into(),
                    queued_count: count as u64,
                };

                if resp_tx.send(Ok(reply)).await.is_err() {
                    break;
                }
            }
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
