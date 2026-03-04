use crate::models::events::{CancelRejectReason, MatchEvent};
use crate::models::order::{EngineAction, OrderEntry, OrderType, Side};
use slab::Slab;

// We assume prices are integers and fit within a reasonable range (e.g., 0 to MAX_PRICE).
// If your prices are decimals (e.g., 100.50), you must multiply them by a factor (e.g., 100)
// to use them as integer indices before passing them to the engine.
const MAX_PRICE: usize = 100_000;

#[derive(Debug, Clone)]
pub struct SlabOrder {
    pub id: u64,
    pub price: u64,
    pub qty: u64,
    pub side: Side,
    pub order_type: OrderType,
    pub timestamp: i64,
    // Links for the doubly-linked list at a specific price level
    pub next: Option<usize>,
    pub prev: Option<usize>,
}

#[derive(Debug, Default, Clone)]
pub struct PriceLevel {
    pub head: Option<usize>,
    pub tail: Option<usize>,
}

pub struct OrderBook {
    // The memory pool: all orders live here. No individual heap allocations!
    pub orders: Slab<SlabOrder>,

    // Quick lookup: Maps an external order_id to the internal Slab index
    // Note: We use a simple array if IDs are sequential, or a fast hashmap if not.
    // For ultimate speed with sequential IDs, a Vec is best. We'll use a fast hashmap here
    // to match your current API, but consider changing to an array if IDs are 1, 2, 3...
    pub order_locations: std::collections::HashMap<u64, usize>,

    // Price Levels: Direct array lookup instead of BTreeMap. O(1) access.
    pub asks: Vec<PriceLevel>,
    pub bids: Vec<PriceLevel>,

    // Tracking the best bid/ask to avoid scanning empty arrays
    pub best_bid: Option<usize>,
    pub best_ask: Option<usize>,
}

impl OrderBook {
    pub fn new() -> Self {
        OrderBook {
            // Pre-allocate space for 12 million orders to avoid resizing during bursts
            orders: Slab::with_capacity(12_000_000),
            order_locations: std::collections::HashMap::with_capacity(12_000_000),
            asks: vec![PriceLevel::default(); MAX_PRICE],
            bids: vec![PriceLevel::default(); MAX_PRICE],
            best_bid: None,
            best_ask: None,
        }
    }

    pub fn clear(&mut self) {
        self.orders.clear();
        self.order_locations.clear();
        self.asks.fill(PriceLevel::default());
        self.bids.fill(PriceLevel::default());
        self.best_bid = None;
        self.best_ask = None;
    }

    pub fn process_batch(&mut self, commands: Vec<EngineAction>) -> Vec<MatchEvent> {
        let mut all_events = Vec::with_capacity(commands.len());
        for command in commands {
            all_events.extend(self.process_single(command));
        }
        all_events
    }

    pub fn process_single(&mut self, command: EngineAction) -> Vec<MatchEvent> {
        match command {
            EngineAction::Create(order) => self.add_order(order),
            EngineAction::Cancel(cancel) => self.cancel_order(cancel.id),
        }
    }

    pub fn add_order(&mut self, order: OrderEntry) -> Vec<MatchEvent> {
        let mut events = Vec::new();

        if order.side == Side::Bid {
            self.match_new_bid(order, &mut events);
        } else if order.side == Side::Ask {
            self.match_new_ask(order, &mut events);
        }

        events
    }

    fn match_new_bid(&mut self, mut new_bid: OrderEntry, events: &mut Vec<MatchEvent>) {
        // Taker matching loop
        while new_bid.qty > 0 {
            if let Some(best_ask_price) = self.best_ask {
                if new_bid.order_type == OrderType::Limit && best_ask_price as u64 > new_bid.price {
                    break; // Price doesn't cross
                }

                // Get the queue for the best ask
                let mut current_ask_key = self.asks[best_ask_price].head;

                while let Some(ask_key) = current_ask_key {
                    if new_bid.qty == 0 {
                        break;
                    }

                    // 🌟 1. 用 Block `{}` 限制借用範圍
                    let (qty_left, ask_id, ask_next) = {
                        // 喺呢個 Block 入面，我哋暫時借用 ask_order
                        let ask_order = self.orders.get_mut(ask_key).unwrap();
                        let match_qty = std::cmp::min(ask_order.qty, new_bid.qty);

                        new_bid.qty -= match_qty;
                        ask_order.qty -= match_qty;

                        events.push(MatchEvent::TradeExecuted {
                            maker_id: ask_order.id,
                            taker_id: new_bid.id,
                            price: ask_order.price,
                            qty: match_qty,
                            timestamp: new_bid.timestamp,
                        });

                        // 將判定需要用嘅資料 Copy 出嚟
                        (ask_order.qty, ask_order.id, ask_order.next)
                    };

                    current_ask_key = ask_next;

                    if qty_left == 0 {
                        self.remove_from_book(ask_key);
                        self.order_locations.remove(&ask_id);
                    }
                }

                // Update best_ask if the current level is empty
                if self.asks[best_ask_price].head.is_none() {
                    self.update_best_ask();
                }
            } else {
                break; // No asks left
            }
        }

        // Add remaining to book
        if new_bid.qty > 0 {
            if new_bid.order_type == OrderType::Limit {
                self.add_to_book(new_bid, events);
            } else {
                events.push(MatchEvent::OrderKilled {
                    id: new_bid.id,
                    killed_qty: new_bid.qty,
                });
            }
        }
    }

    fn match_new_ask(&mut self, mut new_ask: OrderEntry, events: &mut Vec<MatchEvent>) {
        while new_ask.qty > 0 {
            if let Some(best_bid_price) = self.best_bid {
                if new_ask.order_type == OrderType::Limit && (best_bid_price as u64) < new_ask.price
                {
                    break;
                }

                let mut current_bid_key = self.bids[best_bid_price].head;

                while let Some(bid_key) = current_bid_key {
                    if new_ask.qty == 0 {
                        break;
                    }

                    // 1. 限制借用範圍，提取需要嘅資料 (改返啱啲變數名做 bid)
                    let (qty_left, bid_id, bid_next) = {
                        let bid_order = self.orders.get_mut(bid_key).unwrap();
                        let match_qty = std::cmp::min(bid_order.qty, new_ask.qty);

                        new_ask.qty -= match_qty;
                        bid_order.qty -= match_qty;

                        events.push(MatchEvent::TradeExecuted {
                            maker_id: bid_order.id,
                            taker_id: new_ask.id,
                            price: bid_order.price,
                            qty: match_qty,
                            timestamp: new_ask.timestamp,
                        });

                        (bid_order.qty, bid_order.id, bid_order.next)
                    }; // bid_order 喺呢度正式死亡，self 解鎖！

                    // 2. 更新 current_bid_key，為下一次 loop 準備
                    // 呢度用啱啱抽出來嘅 bid_next，唔使再理 order_removed 呢個 flag
                    current_bid_key = bid_next;

                    // 3. 用抽出來嘅 qty_left 同 bid_id 去做判定同移除
                    if qty_left == 0 {
                        self.remove_from_book(bid_key);
                        self.order_locations.remove(&bid_id);
                    }
                }

                if self.bids[best_bid_price].head.is_none() {
                    self.update_best_bid();
                }
            } else {
                break;
            }
        }

        if new_ask.qty > 0 {
            if new_ask.order_type == OrderType::Limit {
                self.add_to_book(new_ask, events);
            } else {
                events.push(MatchEvent::OrderKilled {
                    id: new_ask.id,
                    killed_qty: new_ask.qty,
                });
            }
        }
    }

    fn add_to_book(&mut self, order: OrderEntry, events: &mut Vec<MatchEvent>) {
        let price_idx = order.price as usize;
        let side = order.side.clone();

        events.push(MatchEvent::OrderPlaced {
            id: order.id,
            price: order.price,
            qty: order.qty,
            side: order.side.clone(),
        });

        let slab_order = SlabOrder {
            id: order.id,
            price: order.price,
            qty: order.qty,
            side: order.side,
            order_type: order.order_type,
            timestamp: order.timestamp,
            next: None,
            prev: None, // Will be set below
        };

        // Insert into the memory pool
        let key = self.orders.insert(slab_order);
        self.order_locations.insert(order.id, key);

        // Update the Doubly Linked List for this price level
        let level = match side {
            Side::Bid => &mut self.bids[price_idx],
            Side::Ask => &mut self.asks[price_idx],
        };

        if let Some(tail_key) = level.tail {
            self.orders[tail_key].next = Some(key);
            self.orders[key].prev = Some(tail_key);
            level.tail = Some(key);
        } else {
            // First order at this price level
            level.head = Some(key);
            level.tail = Some(key);
        }

        // Update best bid/ask trackers
        match side {
            Side::Bid => {
                if self.best_bid.map_or(true, |b| price_idx > b) {
                    self.best_bid = Some(price_idx);
                }
            }
            Side::Ask => {
                if self.best_ask.map_or(true, |a| price_idx < a) {
                    self.best_ask = Some(price_idx);
                }
            }
        }
    }

    pub fn cancel_order(&mut self, order_id: u64) -> Vec<MatchEvent> {
        let mut events = Vec::new();

        if let Some(key) = self.order_locations.remove(&order_id) {
            let order = self.orders.get(key).unwrap().clone(); // Clone to avoid borrow checker issues

            self.remove_from_book(key);

            events.push(MatchEvent::OrderCancelled {
                id: order.id,
                cancelled_qty: order.qty,
            });

            // If we removed the last order at the best price, update trackers
            let price_idx = order.price as usize;
            match order.side {
                Side::Bid => {
                    if self.bids[price_idx].head.is_none() && self.best_bid == Some(price_idx) {
                        self.update_best_bid();
                    }
                }
                Side::Ask => {
                    if self.asks[price_idx].head.is_none() && self.best_ask == Some(price_idx) {
                        self.update_best_ask();
                    }
                }
            }
        } else {
            events.push(MatchEvent::CancelRejected {
                id: order_id,
                reason: CancelRejectReason::OrderNotFound,
            });
        }

        events
    }

    // Helper to remove an order from the linked list and the Slab
    fn remove_from_book(&mut self, key: usize) {
        let order = self.orders.remove(key);
        let price_idx = order.price as usize;

        let level = match order.side {
            Side::Bid => &mut self.bids[price_idx],
            Side::Ask => &mut self.asks[price_idx],
        };

        if let Some(prev_key) = order.prev {
            self.orders[prev_key].next = order.next;
        } else {
            level.head = order.next;
        }

        if let Some(next_key) = order.next {
            self.orders[next_key].prev = order.prev;
        } else {
            level.tail = order.prev;
        }
    }

    // Scan down to find the new best bid
    fn update_best_bid(&mut self) {
        if let Some(mut current) = self.best_bid {
            while current > 0 {
                current -= 1;
                if self.bids[current].head.is_some() {
                    self.best_bid = Some(current);
                    return;
                }
            }
        }
        self.best_bid = None;
    }

    // Scan up to find the new best ask
    fn update_best_ask(&mut self) {
        if let Some(mut current) = self.best_ask {
            while current < MAX_PRICE - 1 {
                current += 1;
                if self.asks[current].head.is_some() {
                    self.best_ask = Some(current);
                    return;
                }
            }
        }
        self.best_ask = None;
    }
}
