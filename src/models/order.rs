#[derive(Debug, Clone, Copy, PartialEq)]
pub enum Side {
    Bid,
    Ask,
}

// 2. 定義訂單類型
#[derive(Debug, Clone, Copy, PartialEq)]
pub enum OrderType {
    Limit,
    Market,
}

#[derive(Debug, Clone)]
pub struct Order {
    pub id: u64,
    pub price: u64,
    pub qty: u64,
    pub side: Side,
    pub order_type: OrderType,

}

#[derive(Debug, Clone)]
pub struct OrderEntry {
    pub id: u64,
    pub price: u64,
    pub qty: u64,
    
    
    
}

impl OrderEntry {
    pub fn new(order: &Order) -> Self {
        Self {
            id: order.id,
            price: order.price,
            qty: order.qty,
        }
    }
}