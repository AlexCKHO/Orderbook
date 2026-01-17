use crate::models::order::{Order, OrderType, Side};

struct OrderBook {
    bids: Vec<Order>,
    asks: Vec<Order>,
}

impl OrderBook {
    pub fn add_limit_order(&mut self, order: Order) {
        if order.side == Side::Bid {
            self.bids.push(order)
        } else if order.side == Side::Ask {
            self.asks.push(order)
        }
    }
}
