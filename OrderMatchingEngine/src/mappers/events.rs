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
            MatchEvent::TradeExecuted {
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
            MatchEvent::OrderCancelled { id, cancelled_qty } => {
                EventData::Cancelled(orderbook_grpc::OrderCancelled { id, cancelled_qty })
            }
            MatchEvent::OrderKilled { id, killed_qty } => {
                EventData::Killed(orderbook_grpc::OrderKilled { id, killed_qty })
            }
            MatchEvent::CancelRejected { id, reason } => {
                EventData::Rejected(orderbook_grpc::CancelRejected {
                    id,
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
