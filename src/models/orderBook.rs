use std::cmp::Ordering;
use crate::models::order::{Order, OrderEntry, OrderType, Side};

struct OrderBook {
    bids: Vec<OrderEntry>,
    asks: Vec<OrderEntry>,
}

impl OrderBook {

    fn add_order(&mut self, order: Order) {

        if order.order_type == OrderType::Limit {
            if order.side == Side::Bid {
                self.match_new_limit_bid(order);

            } else if order.side == Side::Ask {

            }
        }

        else if order.order_type == OrderType:: Market{
            todo!("OrderType:: Market");


        }

    }

    // When people are buying
    fn match_new_limit_bid(&mut self, mut new_bid_order: Order) {

        if let Some(smallest_ask_order) =  self.asks.last_mut() {

            while new_bid_order.price >= smallest_ask_order.price && new_bid_order.qty > 0 {

                if smallest_ask_order.qty > new_bid_order.qty {

                    smallest_ask_order.qty -= new_bid_order.qty;

                    new_bid_order.qty  = 0;

                    return;
                } else if smallest_ask_order.qty <  new_bid_order.qty {

                    new_bid_order.qty -= smallest_ask_order.qty;

                    self.asks.pop();

                    return;

                }

            }



        } else {

            // Ask order is null, which means whole vec of ask order is empty
            // nothing to match, thus add new bid order to bid vec
            self.add_order_to_bids_in_order( new_bid_order);
        }




    }

    pub fn add_order_to_bids_in_order(&mut self, new_bid_order:  Order) {
        // Remaining bid order add to
        if new_bid_order.qty > 0 {
            // Note: 'bids' is sorted by price in ascending order (Small -> Big).
            // Reason: This allows us to instantly pop() the best bid (highest price)
            // from the end of the vector, achieving O(1) complexity.
            let result = self.bids.binary_search_by(|probe| {
                if probe.price > new_bid_order.price {
                    // Case: Probe price is higher than the new order.
                    // Action: Search the left side (smaller indices).
                    Ordering::Greater
                } else if probe.price < new_bid_order.price {
                    // Case: Probe price is lower than the new order.
                    // Action: Search the right side (larger indices).
                    Ordering::Less
                } else {
                    // Case: Prices are equal.
                    // Crucial Logic for FIFO (Price-Time Priority):
                    // We intentionally treat the probe as "smaller" (Less) here.
                    // This forces the binary search to continue looking towards the right.
                    // As a result, the insertion index will be placed *after* all existing
                    // orders with the same price, preserving the time priority.
                    Ordering::Less
                }
            });

            // Since we intentionally avoid returning Ordering::Equal to enforce FIFO,
            // the search effectively fails to "find" a match but provides the correct insertion index via Err.
            let index = result.unwrap_or_else(|i| i);

            self.bids.insert(index, OrderEntry::new(&new_bid_order) );
        }
    }



    pub fn add_order_to_asks_in_order(&mut self, order: Order) {

    }


}
