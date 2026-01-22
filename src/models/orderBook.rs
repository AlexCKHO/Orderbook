use crate::models::events::MatchEvent;
use crate::models::order::{Order, OrderEntry, OrderType, Side};

struct OrderBook {
    // FIFO;
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
            if order.side == Side::Bid {
                self.match_new_market_bid(order);
            } else if order.side == Side::Ask {
                self.match_new_market_ask(order);
            }
        }
    }

    // When people are buying
    fn match_new_limit_bid(&mut self, mut new_bid_order: Order, event: &mut Vec<MatchEvent>) {
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
            self.add_order_to_bids(new_bid_order)
        }
    }

    fn match_new_limit_ask(&mut self, mut new_ask_order: Order, events: &mut Vec<MatchEvent>) {
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
                new_ask_order.qty = 0;
            } else {
                new_ask_order.qty -= best_bid.qty;
                self.bids.pop();
            }
        }

        if new_ask_order.qty > 0 {
            self.add_order_to_asks(new_ask_order)
        }
    }

    fn match_new_market_bid(&mut self, mut new_bid_order: Order, events: &mut Vec<MatchEvent>) {
        // Loop until:
        // 1. Finish matching new_bid_order.qty == 0
        // 2. Asks is empty

        while new_bid_order.qty > 0 {
            let best_ask = match self.asks.last_mut() {
                Some(ask) => ask,
                None => break,
            };

            if best_ask.qty > new_bid_order.qty {
                best_ask.qty -= new_bid_order.qty;

                events.push(MatchEvent::TradeExecuted {
                    maker_id: best_ask.id,
                    taker_id: new_bid_order.id,
                    price: best_ask.price,
                    qty: new_bid_order.qty,
                    // ToDO: fix the timestamp to Unix Timestamp
                    timestamp: new_bid_order.timestamp,
                });

                new_bid_order.qty = 0;

                break;
            } else {
                new_bid_order.qty -= best_ask.qty;

                events.push(MatchEvent::TradeExecuted {
                    maker_id: best_ask.id,
                    taker_id: new_bid_order.id,
                    price: best_ask.price,
                    qty: best_ask.qty,
                    // ToDO: fix the timestamp to Unix Timestamp
                    timestamp: new_bid_order.timestamp,
                });

                self.asks.pop();
            }
        }

        // Notes: If there are remaining
        // and the asks can't fulfil the bid.qty, just kill the order
        // Standard Immediate-Or-Cancel approach
        if new_bid_order.qty > 0 {
            events.push(MatchEvent::OrderKilled {
                id: new_bid_order.id,
                killed_qty: new_bid_order.qty,
            })
        }
    }

    fn match_new_market_ask(&mut self, mut new_ask_order: Order, events: &mut Vec<MatchEvent>) {
        // Loop until:
        // 1. Finish matching new_ask_order.qty == 0
        // 2. Bids is empty

        while new_ask_order.qty > 0 {
            let best_bid = match self.bids.last_mut() {
                Some(bid) => bid,
                None => break,
            };

            if best_bid.qty > new_ask_order.qty {
                best_bid.qty -= new_ask_order.qty;
                new_ask_order.qty = 0;
                break;
            } else {
                new_ask_order.qty -= best_bid.qty;
                self.bids.pop();
            }
        }

        // Notes: If there are remaining
        // and the asks can't fulfil the bid.qty, just kill the order
        // Standard Immediate-Or-Cancel approach

        if new_ask_order.qty > 0 {
            println!(
                "Market Order partially filled. Remaining {} qty killed.",
                new_ask_order.qty
            );
        }
    }

    pub fn add_order_to_bids(&mut self, new_bid_order: Order, events: &mut Vec<MatchEvent>) {
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

            events.push(MatchEvent::OrderPlaced {
                id: new_bid_order.id,
                price: new_bid_order.price,
                qty: new_bid_order.qty,
                side: new_bid_order.side,
            });
        }
    }

    pub fn add_order_to_asks(&mut self, new_ask_order: Order, events: &mut Vec<MatchEvent>) {
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
            // The loop continues as long as x.price < new_bid_order.price is true, and breaks immediately once it becomes false.
            let index = self.asks.partition_point(|x| x.price > new_ask_order.price);

            self.asks.insert(index, OrderEntry::new(&new_ask_order));

            events.push(MatchEvent::OrderPlaced {
                id: new_ask_order.id,
                price: new_ask_order.price,
                qty: new_ask_order.qty,
                side: new_ask_order.side,
            });
        }
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
