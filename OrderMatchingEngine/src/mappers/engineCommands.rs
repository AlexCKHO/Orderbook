use crate::models::order::{CancelEntry, EngineAction, OrderEntry, OrderType, Side};
use crate::orderbook_grpc::engine_command::Command;
use crate::orderbook_grpc::{CancelRequest, EngineCommand, OrderRequest};
use std::time::{SystemTime, UNIX_EPOCH};

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
            client_id: req.client_id,
            price: req.price,
            qty: req.qty,
            side,
            order_type,
            timestamp,
        }))
    }
}

impl From<CancelRequest> for EngineAction {
    fn from(req: CancelRequest) -> Self {
        EngineAction::Cancel(CancelEntry {
            client_id: req.client_id,
        })
    }
}

impl TryFrom<EngineCommand> for EngineAction {
    type Error = MapperError;

    fn try_from(req: EngineCommand) -> Result<Self, Self::Error> {
        // Unpack the envelope (oneof payload)
        match req.command {
            Some(Command::PlaceOrder(order_req)) => {
                // Call the TryFrom<OrderRequest> implementation defined above
                EngineAction::try_from(order_req)
            }
            Some(Command::CancelOrder(cancel_req)) => {
                // Call the From<CancelRequest> implementation defined above
                Ok(EngineAction::from(cancel_req))
            }
            None => Err(MapperError("Received empty EngineCommand (None)".into())),
        }
    }
}
