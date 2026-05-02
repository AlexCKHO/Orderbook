use crate::models::events::{CancelRejectReason, MatchEvent};
use crate::models::order::Side;
use crate::orderbook_grpc;
use crate::orderbook_grpc::MatchEvent as protoMatchEvent;
use crate::orderbook_grpc::match_event::EventData;
use orderbook_grpc::Side as ProtoSide;

pub struct MapperError(pub String);

impl From<(MatchEvent, u64, u64)> for protoMatchEvent {
    fn from(data: (MatchEvent, u64, u64)) -> Self {
        let (event, seq, timestamp) = data;

        let event_data = match event {
            MatchEvent::OrderPlaced {
                client_order_id,
                engine_order_id,
                price,
                qty,
                side,
                ..
            } => EventData::Placed(orderbook_grpc::OrderPlaced {
                client_order_id,
                engine_order_id,
                price,
                qty,
                side: match side {
                    Side::Bid => ProtoSide::Bid as i32,
                    Side::Ask => ProtoSide::Ask as i32,
                },
            }),
            MatchEvent::TradeExecuted {
                maker_client_order_id,
                taker_client_order_id,
                maker_engine_order_id,
                taker_engine_order_id,
                price,
                qty,
                taker_side,
                trade_id,
                ..
            } => EventData::Filled(orderbook_grpc::TradeExecuted {
                maker_client_order_id,
                taker_client_order_id,
                maker_engine_order_id,
                taker_engine_order_id,
                price,
                qty,
                taker_side: match taker_side {
                    Side::Bid => ProtoSide::Bid as i32,
                    Side::Ask => ProtoSide::Ask as i32,
                },
                trade_id,
            }),
            MatchEvent::OrderCancelled {
                client_order_id,
                engine_order_id,
                cancelled_qty,
                ..
            } => EventData::Cancelled(orderbook_grpc::OrderCancelled {
                client_order_id,
                engine_order_id,
                cancelled_qty,
            }),
            MatchEvent::OrderKilled {
                client_order_id,
                killed_qty,
            } => EventData::Killed(orderbook_grpc::OrderKilled {
                client_order_id,
                killed_qty,
            }),
            MatchEvent::CancelRejected {
                client_order_id,
                reason,
            } => EventData::Rejected(orderbook_grpc::CancelRejected {
                client_order_id,
                reason: match reason {
                    CancelRejectReason::OrderNotFound => 1,
                    _ => 0,
                },
            }),

            MatchEvent::PublicTrade {
                price,
                qty,
                taker_side,
                trade_id,
            } => EventData::Traded(orderbook_grpc::PublicTrade {
                price,
                qty,
                taker_side: match taker_side {
                    Side::Bid => ProtoSide::Bid as i32,
                    Side::Ask => ProtoSide::Ask as i32,
                },
                trade_id,
            }),
        };

        protoMatchEvent {
            sequence: seq,
            timestamp,
            event_data: Some(event_data),
        }
    }
}
