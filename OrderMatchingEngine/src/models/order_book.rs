use crate::models::events::{CancelRejectReason, MatchEvent};
use crate::models::order::{Order, OrderType, Side};
use std::collections::{BTreeMap, HashMap, VecDeque};

// Note: Order book on Tokio Integrating Sync Logic with Async Runtime
pub struct OrderBook {
    // FIFO;
    pub asks: BTreeMap<u64, VecDeque<Order>>,
    pub bids: BTreeMap<u64, VecDeque<Order>>,
    // Order.id, (Order.pice, Side)
    pub order_locations: HashMap<u64, (u64, Side)>,
}

impl OrderBook {
    pub fn new() -> Self {
        OrderBook {
            bids: BTreeMap::new(),
            asks: BTreeMap::new(),
            order_locations: HashMap::new(),
        }
    }

    pub fn clear(&mut self) {
        self.bids.clear();
        self.asks.clear();
        self.order_locations.clear();
    }
    pub fn add_order(&mut self, order: Order) -> Vec<MatchEvent> {
        let mut events = Vec::new();

        if order.side == Side::Bid {
            self.match_new_bid(order, &mut events);
        } else if order.side == Side::Ask {
            self.match_new_ask(order, &mut events);
        }

        return events;
    }

    pub fn cancel_order(&mut self, order_id: u64, events: &mut Vec<MatchEvent>) {
        let (price, side) = match self.order_locations.remove(&order_id) {
            Some(loc) => loc,
            None => {
                events.push(MatchEvent::CancelRejected {
                    id: order_id,
                    reason: CancelRejectReason::OrderNotFound,
                });
                return;
            }
        };

        let book = match side {
            Side::Bid => &mut self.bids,
            Side::Ask => &mut self.asks,
        };

        if let Some(queue) = book.get_mut(&price) {
            if let Some(position) = queue.iter().position(|o| o.id == order_id) {
                let cancelled_order = queue.remove(position).unwrap();

                events.push(MatchEvent::OrderCancelled {
                    id: cancelled_order.qty,
                    cancelled_qty: cancelled_order.qty,
                });

                if queue.is_empty() {
                    book.remove(&price);
                }
            }
        }

        return;
    }

    // When people are buying
    fn match_new_bid(&mut self, mut new_bid_order: Order, events: &mut Vec<MatchEvent>) {
        while new_bid_order.qty > 0 {
            if let Some((&ask_price, ask_queue)) = self.asks.iter_mut().next() {
                if new_bid_order.order_type == OrderType::Limit && ask_price > new_bid_order.price {
                    break;
                }

                let best_ask_order = ask_queue.front_mut().unwrap();

                let match_qty = std::cmp::min(best_ask_order.qty, new_bid_order.qty);

                new_bid_order.qty -= match_qty;
                best_ask_order.qty -= match_qty;

                events.push(MatchEvent::TradeExecuted {
                    maker_id: best_ask_order.id,
                    taker_id: new_bid_order.id,
                    price: best_ask_order.price,
                    qty: match_qty,
                    timestamp: new_bid_order.timestamp,
                });

                if best_ask_order.qty == 0 {
                    let removed_order = ask_queue.pop_front().unwrap();
                    self.order_locations.remove(&removed_order.id);
                }

                if ask_queue.is_empty() {
                    self.asks.remove(&ask_price);
                }
            } else {
                break;
            }
        }

        if new_bid_order.qty > 0 {
            if new_bid_order.order_type == OrderType::Limit {
                self.add_order_to_bids(new_bid_order, events);
            } else if new_bid_order.order_type == OrderType::Market {
                events.push(MatchEvent::OrderKilled {
                    id: new_bid_order.id,
                    killed_qty: new_bid_order.qty,
                })
            }
        }
    }

    fn match_new_ask(&mut self, mut new_ask_order: Order, events: &mut Vec<MatchEvent>) {
        while new_ask_order.qty > 0 {
            // next_back gets the larget bid_price
            if let Some((&bid_price, bid_queue)) = self.bids.iter_mut().next_back() {
                // If the largest bid price is lower the new ask order price, no deal.
                if new_ask_order.order_type == OrderType::Limit && bid_price < new_ask_order.price {
                    break;
                }

                let best_bid_order = bid_queue.front_mut().unwrap();

                let match_qty = std::cmp::min(best_bid_order.qty, new_ask_order.qty);

                new_ask_order.qty -= match_qty;
                best_bid_order.qty -= match_qty;

                events.push(MatchEvent::TradeExecuted {
                    maker_id: best_bid_order.id,
                    taker_id: new_ask_order.id,
                    price: best_bid_order.price,
                    qty: match_qty,
                    timestamp: new_ask_order.timestamp,
                });

                if best_bid_order.qty == 0 {
                    let removed_order = bid_queue.pop_front().unwrap();
                    self.order_locations.remove(&removed_order.id);
                }

                if bid_queue.is_empty() {
                    self.bids.remove(&bid_price);
                }
            } else {
                break;
            }
        }
        // Adding the remaining order to the queue if it is a market order => kill
        if new_ask_order.qty > 0 {
            if new_ask_order.order_type == OrderType::Limit {
                self.add_order_to_asks(new_ask_order, events)
            } else if new_ask_order.order_type == OrderType::Market {
                events.push(MatchEvent::OrderKilled {
                    id: new_ask_order.id,
                    killed_qty: new_ask_order.qty,
                })
            }
        }
    }

    pub fn add_order_to_bids(&mut self, new_bid_order: Order, events: &mut Vec<MatchEvent>) {
        self.order_locations
            .insert(new_bid_order.id, (new_bid_order.price, Side::Bid));

        events.push(MatchEvent::OrderPlaced {
            id: new_bid_order.id,
            price: new_bid_order.price,
            qty: new_bid_order.qty,
            side: new_bid_order.side,
        });
        self.bids
            .entry(new_bid_order.price) // 1. Locate the specific price level in the bid side of the book
            .or_insert_with(VecDeque::new) // 2. If the price level is empty, initialize a new queue
            .push_back(new_bid_order); // 3. Append the order to the end to maintain time priority (FIFO)
    }

    pub fn add_order_to_asks(&mut self, new_ask_order: Order, events: &mut Vec<MatchEvent>) {
        self.order_locations
            .insert(new_ask_order.id, (new_ask_order.price, Side::Ask));

        events.push(MatchEvent::OrderPlaced {
            id: new_ask_order.id,
            price: new_ask_order.price,
            qty: new_ask_order.qty,
            side: new_ask_order.side,
        });
        self.asks
            .entry(new_ask_order.price) // 1. Check if this price level already exists in the book
            .or_insert_with(VecDeque::new) // 2. If not, create a new "queue" (VecDeque) for this price
            .push_back(new_ask_order); // 3. Add the order to the end of the queue (FIFO)
    }
}
#[cfg(test)]
mod tests {
    use super::*;
    /// Helper function to construct Orders
    fn new_order(
        id: u64,
        price: u64,
        qty: u64,
        side: Side,
        order_type: OrderType,
        timestamp: i64,
    ) -> Order {
        Order {
            id,
            price,
            qty,
            side,
            order_type,
            timestamp,
        }
    }

    #[test]
    fn test_limit_order_matching() {
        // 1. Initialize OrderBook (Use new(), don't construct manually)
        let mut ob = OrderBook::new();

        // 2. Scenario: User A places a Sell order (Maker)
        // Ask @ 100, Qty 10
        let ask_order = new_order(1, 100, 10, Side::Ask, OrderType::Limit, 1000);
        ob.add_order(ask_order);

        // Check: Order should be in Asks BTreeMap at price 100
        assert!(ob.asks.contains_key(&100));
        assert_eq!(ob.asks.get(&100).unwrap().len(), 1);
        assert!(ob.bids.is_empty());

        // 3. Scenario: User B tries to Buy (Taker), but price is too low
        // Bid @ 99, Qty 5
        let bid_cheap = new_order(2, 99, 5, Side::Bid, OrderType::Limit, 2000);
        ob.add_order(bid_cheap);

        // Check: No match. Bid enters Bids BTreeMap at price 99.
        assert!(ob.bids.contains_key(&99));
        assert_eq!(ob.asks.get(&100).unwrap()[0].qty, 10); // Ask remains untouched

        // 4. Scenario: User C places a Buy order (Taker) with matching price
        // Bid @ 100, Qty 3
        let bid_match = new_order(3, 100, 3, Side::Bid, OrderType::Limit, 3000);
        let events = ob.add_order(bid_match);

        // Check: Immediate match!
        // 1. User C (Bid) is fully filled, so it should NOT be in Bids book.
        //    (Only User B @ 99 remains)
        assert!(!ob.bids.contains_key(&100));
        assert!(ob.bids.contains_key(&99));

        // 2. Ask @ 100 should still exist, but Qty reduces 10 -> 7
        assert_eq!(ob.asks.get(&100).unwrap()[0].qty, 7);

        // 3. Check Events
        assert_eq!(events.len(), 1); // Should have 1 TradeExecuted event

        println!("✅ Test Passed: Basic Matching Logic is Correct!");
    }

    #[test]
    fn test_fifo_ordering() {
        let mut ob = OrderBook::new();

        // User A places Bid @ 100 (First)
        ob.add_order(new_order(1, 100, 10, Side::Bid, OrderType::Limit, 1000));

        // User B places Bid @ 100 (Later)
        ob.add_order(new_order(2, 100, 10, Side::Bid, OrderType::Limit, 2000));

        // Layout in BTreeMap: Key 100 -> VecDeque [Order(1), Order(2)]
        // Verification:
        let queue = ob.bids.get(&100).unwrap();
        assert_eq!(queue.len(), 2);
        assert_eq!(queue[0].id, 1); // Front is User A
        assert_eq!(queue[1].id, 2); // Back is User B

        // Match Logic: Sell order @ 100, Qty 10
        // Should consume User A (Front) completely.
        ob.add_order(new_order(3, 100, 10, Side::Ask, OrderType::Limit, 3000));

        // The remaining order in the book should be User B (ID 2).
        let queue_after = ob.bids.get(&100).unwrap();
        assert_eq!(queue_after.len(), 1);
        assert_eq!(queue_after[0].id, 2); // User B is now at the front
    }

    #[test]
    fn test_asks_fifo_ordering() {
        let mut ob = OrderBook::new();

        // Seller A places Ask @ 100 (First)
        ob.add_order(new_order(1, 100, 10, Side::Ask, OrderType::Limit, 1000));

        // Seller B places Ask @ 100 (Later)
        ob.add_order(new_order(2, 100, 10, Side::Ask, OrderType::Limit, 2000));

        // Layout: Key 100 -> VecDeque [Order(1), Order(2)]

        // Buyer C comes to buy 10 @ 100
        ob.add_order(new_order(3, 100, 10, Side::Bid, OrderType::Limit, 3000));

        // Assertions:
        // One Ask should remain.
        assert_eq!(ob.asks.get(&100).unwrap().len(), 1);

        // The remaining Ask MUST be User B (ID: 2).
        // User A (ID: 1) was at front and got popped.
        assert_eq!(ob.asks.get(&100).unwrap()[0].id, 2);

        println!("✅ Test Passed: Asks FIFO is Correct!");
    }

    #[test]
    fn test_market_bid_order_ioc() {
        let mut ob = OrderBook::new();

        // 1. Setup Liquidity (Asks)
        // Seller A: Limit Sell @ 100, Qty 10
        ob.add_order(new_order(1, 100, 10, Side::Ask, OrderType::Limit, 1000));
        // Seller B: Limit Sell @ 102, Qty 20
        ob.add_order(new_order(2, 102, 20, Side::Ask, OrderType::Limit, 2000));

        // 2. Market Buy comes in (Qty 15)
        // Logic: Consumes 10 @ 100, then 5 @ 102.
        ob.add_order(new_order(3, 0, 15, Side::Bid, OrderType::Market, 3000));

        // Check: The $100 price level should be gone (Empty queue removes the key).
        assert!(!ob.asks.contains_key(&100));

        // Check: The $102 price level should remain with 15 qty (20 - 5).
        let queue_102 = ob.asks.get(&102).unwrap();
        assert_eq!(queue_102[0].qty, 15);
        assert_eq!(queue_102[0].price, 102);

        // 3. Huge Market Buy (Qty 1000)
        // Logic: Consumes remaining 15 @ 102. The rest (985) is Killed (IOC).
        ob.add_order(new_order(4, 0, 1000, Side::Bid, OrderType::Market, 4000));

        // Check: Asks should be completely empty.
        assert!(ob.asks.is_empty());
        // Check: Bids should also be empty (Market orders don't rest).
        assert!(ob.bids.is_empty());

        println!("✅ Test Passed: Market Bid Order (IOC) logic is correct!");
    }

    #[test]
    fn test_market_ask_order_ioc() {
        let mut ob = OrderBook::new();

        // 1. Setup Liquidity (Bids)
        // Buyer A: Limit Buy @ 98, Qty 20
        ob.add_order(new_order(1, 98, 20, Side::Bid, OrderType::Limit, 1000));
        // Buyer B: Limit Buy @ 100, Qty 10
        ob.add_order(new_order(2, 100, 10, Side::Bid, OrderType::Limit, 2000));

        // 2. Market Sell comes in (Qty 15)
        // Logic: Hits Best Bid ($100) first (eats 10), then ($98) (eats 5).
        ob.add_order(new_order(3, 0, 15, Side::Ask, OrderType::Market, 3000));

        // Check: The $100 Bid should be gone.
        assert!(!ob.bids.contains_key(&100));

        // Check: The remaining Bid should be the $98 one, qty 15.
        let queue_98 = ob.bids.get(&98).unwrap();
        assert_eq!(queue_98[0].price, 98);
        assert_eq!(queue_98[0].qty, 15);

        // 3. Huge Market Sell (Qty 1000)
        ob.add_order(new_order(4, 0, 1000, Side::Ask, OrderType::Market, 4000));

        // Check: Empty.
        assert!(ob.bids.is_empty());
        assert!(ob.asks.is_empty());

        println!("✅ Test Passed: Market Ask Order (IOC) logic is correct!");
    }
}
