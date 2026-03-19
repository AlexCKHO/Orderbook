use std::time::{SystemTime, UNIX_EPOCH};
use crate::models::order::{CancelEntry, EngineAction, OrderEntry, OrderType, Side};
use crate::orderbook_grpc::{CancelRequest, OrderRequest};

pub struct MapperError(pub String);

impl TryFrom<OrderRequest> for EngineAction {
    type Error = MapperError;

    fn try_from(req: OrderRequest) -> Result<Self, Self::Error> {
        let side = match req.side {
            1 => Side::Bid,
            2 => Side::Ask,
            _ => return Err(MapperError("Invalid Side".into())),
        };

        let order_type = match req.order_type {
            1 => OrderType::Limit,
            2 => OrderType::Market,
            _ => return Err(MapperError("Invalid Order Type".into())),
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
}

impl From <CancelRequest> for EngineAction {
    fn from(req: CancelRequest) -> Self {
        EngineAction::Cancel(CancelEntry { id: req.id })
    }

}