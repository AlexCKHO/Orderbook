use crate::models::order::EngineAction;
use crate::orderbook_grpc::OrderAck;
use tokio::sync::oneshot;

#[derive(Debug)]
pub struct EnginePayload {
    pub actions: Vec<EngineAction>,
    pub reply_tx: Option<oneshot::Sender<Vec<OrderAck>>>,
}
