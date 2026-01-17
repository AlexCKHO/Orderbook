use crate::models::order::{Order, OrderType, Side};

struct OrderBook {
    bids: Vec<Order>,
    asks: Vec<Order>,
}

impl OrderBook {

    fn add_order(&mut self, order: Order) {

        if order.order_type == OrderType::Limit {
            if order.side == Side::Bid {


            } else if order.side == Side::Ask {

            }
        }

        else if order.order_type == OrderType:: Market{
            todo!("OrderType:: Market");


        }

    }

    // When people are buying
    fn match_new_bid(&mut self, new_bid_order: Order) {
        if let Some(smallest_ask_order) =  self.asks.last_mut() {
            if new_bid_order.price == smallest_ask_order.price {
                // Balancing biding and asking qty
                // If new_bid_order.qty > smallest_ask_order.qty
                    // new_bid_order.qty -= smallest_ask_order.qty
                    // smallest_ask_order gone from bids
                // Else if new_bid_order.qty < smallest_ask_order.qty
                    // smallest_ask_order.qty -= new_bid_order.qty
                    // new_bid_order gone
            }

        } else {


        }




    }

    pub fn add_order_to_bids_in_order(&mut self, order: Order) {

    }

    pub fn add_order_to_asks_in_order(&mut self, order: Order) {

    }


}
