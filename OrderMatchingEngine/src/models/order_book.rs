use crate::models::events::MatchEvent;
use crate::models::order::{Order, OrderEntry, OrderType, Side};
use std::collections::{BTreeMap, HashMap, VecDeque};
// Note: Orderbook on Tokio Integrating Sync Logic with Async Runtime
pub struct OrderBook {
    // FIFO;
    pub asks: BTreeMap<u64, VecDeque<Order>>,
    pub bids: BTreeMap<u64, VecDeque<Order>>,

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
            if (new_bid_order.order_type == OrderType::Limit) {
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
            if (new_ask_order.order_type == OrderType::Limit) {
                self.add_order_to_asks(new_ask_order, events)
            } else if (new_ask_order.order_type == OrderType::Market) {
                events.push(MatchEvent::OrderKilled {
                    id: new_ask_order.id,
                    killed_qty: new_ask_order.qty,
                })
            }
        }
    }

    pub fn add_order_to_bids(&mut self, new_bid_order: Order, events: &mut Vec<MatchEvent>) {
        self.bids
            .entry(new_bid_order.price) // 1. Locate the specific price level in the bid side of the book
            .or_insert_with(VecDeque::new) // 2. If the price level is empty, initialize a new queue
            .push_back(new_bid_order.clone()); // 3. Append the order to the end to maintain time priority (FIFO)
        self.order_locations
            .insert(new_bid_order.id, (new_bid_order.price, Side::Bid));

        events.push(MatchEvent::OrderPlaced {
            id: new_bid_order.id,
            price: new_bid_order.price,
            qty: new_bid_order.qty,
            side: new_bid_order.side,
        });
    }

    pub fn add_order_to_asks(&mut self, new_ask_order: Order, events: &mut Vec<MatchEvent>) {
        self.asks
            .entry(new_ask_order.price) // 1. Check if this price level already exists in the book
            .or_insert_with(VecDeque::new) // 2. If not, create a new "queue" (VecDeque) for this price
            .push_back(new_ask_order.clone()); // 3. Add the order to the end of the queue (FIFO)
        self.order_locations
            .insert(new_ask_order.id, (new_ask_order.price, Side::Ask));

        events.push(MatchEvent::OrderPlaced {
            id: new_ask_order.id,
            price: new_ask_order.price,
            qty: new_ask_order.qty,
            side: new_ask_order.side,
        });
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Helper function to construct Orders to reduce boilerplate code in tests.
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
            // Initialize timestamp here if added in the future
        }
    }

    #[test]
    fn test_limit_order_matching() {
        // 1. Initialize OrderBook
        let mut ob = OrderBook {
            bids: Vec::new(),
            asks: Vec::new(),
        };

        // 2. Scenario: User A places a Sell order (Maker)
        // Ask @ 100, Qty 10
        let ask_order = new_order(1, 100, 10, Side::Ask, OrderType::Limit, 1768032384123456789);
        ob.add_order(ask_order);

        // Check: Order should be successfully added to the Asks queue
        assert_eq!(ob.asks.len(), 1);
        assert_eq!(ob.bids.len(), 0);

        // 3. Scenario: User B tries to Buy (Taker), but price is too low
        // Bid @ 99, Qty 5
        let bid_cheap = new_order(2, 99, 5, Side::Bid, OrderType::Limit, 1768052384123456789);
        ob.add_order(bid_cheap);

        // Check: No match should occur due to price mismatch. Order enters Bids queue.
        assert_eq!(ob.bids.len(), 1);
        assert_eq!(ob.asks[0].qty, 10); // The Ask order remains untouched

        // 4. Scenario: User C places a Buy order (Taker) with matching price (Aggressive)
        // Bid @ 100, Qty 3
        let bid_match = new_order(3, 100, 3, Side::Bid, OrderType::Limit, 1768082384123456789);
        ob.add_order(bid_match);

        // Check: Immediate match should occur!
        // Bids queue should still have 1 order (User C is filled immediately, User B remains)
        assert_eq!(ob.bids.len(), 1);

        // Asks queue should still have 1 order, but quantity reduces from 10 to 7 (Partial Fill)
        assert_eq!(ob.asks.len(), 1);
        assert_eq!(ob.asks[0].qty, 7);

        println!("✅ Test Passed: Basic Matching Logic is Correct!");
    }

    #[test]
    fn test_fifo_ordering() {
        let mut ob = OrderBook {
            bids: Vec::new(),
            asks: Vec::new(),
        };

        // User A places Bid @ 100 (First arrival)
        ob.add_order(new_order(
            1,
            100,
            10,
            Side::Bid,
            OrderType::Limit,
            1768032384123456789,
        ));

        // User B places Bid @ 100 (Arrives later)
        ob.add_order(new_order(
            2,
            100,
            10,
            Side::Bid,
            OrderType::Limit,
            1768052384123456789,
        ));

        // Expected Memory Layout (Bids are Ascending):
        // [100 (User B/New), 100 (User A/Old)]
        // pop() retrieves from the end -> gets User A (Old) first.

        // Validation Logic:
        // An incoming Sell order @ 100, Qty 10 should completely fill User A, leaving User B.
        ob.add_order(new_order(
            3,
            100,
            10,
            Side::Ask,
            OrderType::Limit,
            1768082384123456789,
        ));

        // The remaining order in the book should be User B (ID 2), honoring Time Priority.
        assert_eq!(ob.bids[0].id, 2);
    }

    #[test]
    fn test_asks_fifo_ordering() {
        let mut ob = OrderBook {
            bids: Vec::new(),
            asks: Vec::new(),
        };

        // 1. Seller A places Ask @ 100 (First arrival - "The Old Order")
        // ID: 1
        ob.add_order(new_order(
            1,
            100,
            10,
            Side::Ask,
            OrderType::Limit,
            1768032384123456789,
        ));

        // 2. Seller B places Ask @ 100 (Later arrival - "The New Order")
        // ID: 2
        ob.add_order(new_order(
            2,
            100,
            10,
            Side::Ask,
            OrderType::Limit,
            1768052384123456789,
        ));

        // --- Memory Layout Check (Mental Model) ---
        // Asks are sorted Descending: [Highest ... Lowest]
        // Since Price is equal, Logic dictates New comes BEFORE Old.
        // Expected Vector State: [ {ID:2, Price:100}, {ID:1, Price:100} ]
        // The last element (ID:1) is the "Best Ask" because it arrived first.

        // 3. Buyer C comes in to Buy 10 units @ 100
        // This should trigger a match against the "Best Ask".
        ob.add_order(new_order(
            3,
            100,
            10,
            Side::Bid,
            OrderType::Limit,
            1768082384123456789,
        ));

        // --- Assertions ---
        // Buyer C should be fully filled.
        assert_eq!(ob.bids.len(), 0);

        // One Ask should remain.
        assert_eq!(ob.asks.len(), 1);

        // The remaining Ask MUST be User B (ID: 2).
        // Why? Because User A (ID: 1) was at the end of the vector and got popped first.
        assert_eq!(ob.asks[0].id, 2);

        println!("✅ Test Passed: Asks FIFO (Price-Time Priority) is Correct!");
    }

    #[test]
    fn test_market_bid_order_ioc() {
        let mut ob = OrderBook {
            bids: vec![],
            asks: vec![],
        };

        // 1. Setup Liquidity (Asks)
        // Seller A: Limit Sell @ 100, Qty 10 (Best Price)
        ob.add_order(new_order(
            1,
            100,
            10,
            Side::Ask,
            OrderType::Limit,
            1768032384123456789,
        ));
        // Seller B: Limit Sell @ 102, Qty 20 (Worse Price)
        ob.add_order(new_order(
            2,
            102,
            20,
            Side::Ask,
            OrderType::Limit,
            1768033384123456789,
        ));

        // 2. Market Buy comes in (Qty 15)
        // Expectation: It should consume the entire level at $100 (10 qty)
        // and partially consume the level at $102 (5 qty).
        ob.add_order(new_order(
            3,
            0,
            15,
            Side::Bid,
            OrderType::Market,
            1768033484123456789,
        ));

        // Check: The $100 price level should be fully consumed (Popped).
        assert_eq!(ob.asks.len(), 1);

        // Check: The $102 price level should remain with 15 qty (20 - 5).
        // Since Asks are sorted Descending, the remaining one is at index 0.
        assert_eq!(ob.asks[0].qty, 15);
        assert_eq!(ob.asks[0].price, 102);

        // 3. Huge Market Buy (Qty 1000)
        // Expectation: Consumes the remaining 15 units, then the rest of the order is killed (IOC).
        // It does NOT enter the queue.
        ob.add_order(new_order(
            4,
            0,
            1000,
            Side::Bid,
            OrderType::Market,
            1768033485123456789,
        ));

        // Check: Asks should be completely empty.
        assert_eq!(ob.asks.len(), 0);
        // Check: Bids should also be empty (because Market Orders are IOC and don't queue).
        assert_eq!(ob.bids.len(), 0);

        println!("✅ Test Passed: Market Bid Order (IOC) logic is correct!");
    }

    #[test]
    fn test_market_ask_order_ioc() {
        let mut ob = OrderBook {
            bids: vec![],
            asks: vec![],
        };

        // 1. Setup Liquidity (Bids)
        // Buyer A: Limit Buy @ 98, Qty 20 (Lower Price)
        ob.add_order(new_order(
            1,
            98,
            20,
            Side::Bid,
            OrderType::Limit,
            1768033384123456789,
        ));
        // Buyer B: Limit Buy @ 100, Qty 10 (Higher/Best Price)
        ob.add_order(new_order(
            2,
            100,
            10,
            Side::Bid,
            OrderType::Limit,
            1768033484123456789,
        ));

        // Verify Layout: Bids are Ascending [98, 100].
        // pop() takes from the end, so it should take $100 first.

        // 2. Market Sell comes in (Qty 15)
        // Expectation: It should hit the Best Bid ($100) first (eats 10),
        // then hit the Next Best Bid ($98) (eats 5).
        ob.add_order(new_order(
            3,
            0,
            15,
            Side::Ask,
            OrderType::Market,
            1768033584123456789,
        ));

        // Check: The $100 Bid should be fully consumed (Popped).
        // Only 1 bid level remains.
        assert_eq!(ob.bids.len(), 1);

        // Check: The remaining Bid should be the $98 one, with qty reduced.
        // Original 20 - 5 consumed = 15 remaining.
        assert_eq!(ob.bids[0].price, 98);
        assert_eq!(ob.bids[0].qty, 15);

        // 3. Huge Market Sell (Qty 1000)
        // Expectation: Consumes the remaining 15 units at $98.
        // The rest of the sell order (985 qty) is Killed immediately (IOC).
        ob.add_order(new_order(
            4,
            0,
            1000,
            Side::Ask,
            OrderType::Market,
            1768033585123456789,
        ));

        // Check: Orderbook should be completely empty.
        assert_eq!(ob.bids.len(), 0);
        assert_eq!(ob.asks.len(), 0);

        println!("✅ Test Passed: Market Ask Order (IOC) logic is correct!");
    }
}
