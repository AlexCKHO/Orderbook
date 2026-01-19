use crate::models::order::{Order, OrderEntry, OrderType, Side};
use std::cmp::Ordering;

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
                self.match_new_limit_ask(order);
            }
        } else if order.order_type == OrderType::Market {
            todo!("OrderType:: Market");
        }
    }

    // When people are buying
    fn match_new_limit_bid(&mut self, mut new_bid_order: Order) {
        // Loop until:
        // 1. Finish matching new_bid_order.qty == 0
        // 2. Asks is empty
        // 3. Price mismatch, no ask price is lower or equal to

        while new_bid_order.qty > 0 {
            let best_ask = match self.asks.last_mut() {
                Some(ask) => ask,
                // Case 2. Asks is empty
                None => break,
            };
            // Case 3. Price mismatch
            if new_bid_order.price < best_ask.price {
                break;
            }

            if best_ask.qty > new_bid_order.qty {
                best_ask.qty -= new_bid_order.qty;
                new_bid_order.qty = 0;
            } else {
                // For == and <, all need to pop
                new_bid_order.qty -= best_ask.qty;
                self.asks.pop();
            }
        }

        if new_bid_order.qty > 0 {
            self.add_order_to_bids_in_order(new_bid_order)
        }
    }

    fn match_new_limit_ask(&mut self, mut new_ask_order: Order) {
        // Loop until:
        // 1. Finish matching new_ask_order.qty == 0
        // 2. Bids is empty, no orders to be matched
        // 3. Price mismatch, no bid price is higher or equal to

        while new_ask_order.qty > 0 {
            let best_bid = match self.bids.last_mut() {
                Some(bid) => bid,
                // 2. Bids is empty,
                None => break,
            };
            // 3. Price mismatch, no bid price is higher or equal to
            // Even the best price people providing is smaller than new ask price
            // there is no chance for matching, thus break
            if new_ask_order.price > best_bid.price {
                break;
            }

            if best_bid.qty > new_ask_order.qty {
                best_bid.qty -= new_ask_order.qty;
                new_ask_order.qty = 0
            } else {
                new_ask_order.qty -= best_bid.qty;
                self.bids.pop();
            }
        }

        if new_ask_order.qty > 0 {
            self.add_order_to_asks_in_order(new_ask_order)
        }
    }

    pub fn add_order_to_bids_in_order(&mut self, new_bid_order: Order) {
        if new_bid_order.qty > 0 {
            // Bids are sorted in Ascending order: [Smallest Price ... Highest Price].
            // Example: [98, 99, 100(Old)]
            // Reason: This allows O(1) popping of the Best Bid (Highest) from the end.

            // --- FIFO Logic (Price-Time Priority) ---
            // We need to insert the NEW order *before* the OLD order of the same price.
            // Target Layout: [98, 99, 100(New), 100(Old)]
            // This ensures that pop() retrieves 100(Old) first.

            // 'partition_point' returns the index of the first element where the predicate is FALSE.
            // Predicate: "Is x.price < new.price?"
            // We want the first element where !(x < new), which implies x >= new.
            let index = self.bids.partition_point(|x| x.price < new_bid_order.price);

            self.bids.insert(index, OrderEntry::new(&new_bid_order));
        }
    }

    pub fn add_order_to_asks_in_order(&mut self, new_ask_order: Order) {
        if new_ask_order.qty > 0 {
            // Asks are sorted in Descending order: [Highest Price ... Smallest Price].
            // Example: [102, 101, 100(Old)]
            // Reason: This allows O(1) popping of the Best Ask (Lowest) from the end.

            // --- FIFO Logic (Price-Time Priority) ---
            // We need to insert the NEW order *before* the OLD order of the same price.
            // Target Layout: [102, 101, 100(New), 100(Old)]
            // This ensures that pop() retrieves 100(Old) first.

            // 'partition_point' returns the index of the first element where the predicate is FALSE.
            // Predicate: "Is x.price > new.price?"
            // We want the first element where !(x > new), which implies x <= new.
            let index = self.asks.partition_point(|x| x.price > new_ask_order.price);

            self.asks.insert(index, OrderEntry::new(&new_ask_order));
        }
    }
}
