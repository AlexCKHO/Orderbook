use crate::models::events::MatchEvent;
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
    inbound_tx: mpsc::Sender<EngineAction>,
}
impl GrpcGateway {
    pub fn new(inbound_tx: mpsc::Sender<EngineAction>) -> Self {
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
            while let Ok(Some(engine_batch_command)) = in_stream.message().await {
                let mut count = 0;
                for command in engine_batch_command.commands {
                    if let Ok(action) = EngineAction::try_from(command) {
                        if let Err(_) = tx.send(action).await {
                            eprintln!(
                                "🔥 [CRITICAL] Matching Engine channel closed! Stopping gateway worker."
                            );
                            return;
                        }
                        count += 1;
                    }
                }

                let reply = OrderBatchResponse {
                    success: true,
                    message: "Order queued".into(),
                    queued_count: count,
                };
                let _ = resp_tx.send(Ok(reply)).await;
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
