use crate::models::events::{CancelRejectReason, MatchEvent};
use crate::models::order::Side;
use crate::orderbook_grpc;
use crate::orderbook_grpc::MatchEvent as protoMatchEvent;
use crate::orderbook_grpc::match_event::EventData;

pub struct MapperError(pub String);

impl From<MatchEvent> for protoMatchEvent {
    fn from(event: MatchEvent) -> protoMatchEvent {
        let event_data = match event {
            MatchEvent::OrderPlaced {
                client_id,
                price,
                qty,
                side,
                ..
            } => EventData::Placed(orderbook_grpc::OrderPlaced {
                client_id,
                price,
                qty,
                side: match side {
                    Side::Bid => 1,
                    Side::Ask => 2,
                },
            }),
            MatchEvent::TradeExecuted {
                maker_id,
                taker_id,
                price,
                qty,
                timestamp,
                taker_side,
                trade_id,
            } => EventData::Filled(orderbook_grpc::TradeExecuted {
                maker_id,
                taker_id,
                price,
                qty,
                timestamp,
                taker_side: match taker_side {
                    Side::Bid => 1,
                    Side::Ask => 2,
                },
                trade_id,
            }),
            MatchEvent::OrderCancelled {
                client_id,
                cancelled_qty,
                ..
            } => EventData::Cancelled(orderbook_grpc::OrderCancelled {
                client_id,
                cancelled_qty,
            }),
            MatchEvent::OrderKilled {
                client_id,
                killed_qty,
            } => EventData::Killed(orderbook_grpc::OrderKilled {
                client_id,
                killed_qty,
            }),
            MatchEvent::CancelRejected { client_id, reason } => {
                EventData::Rejected(orderbook_grpc::CancelRejected {
                    client_id,
                    reason: match reason {
                        CancelRejectReason::OrderNotFound => 1,
                    },
                })
            }
        };
        orderbook_grpc::MatchEvent {
            event_data: Some(event_data),
        }
    }
}
