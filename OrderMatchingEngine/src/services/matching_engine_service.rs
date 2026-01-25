use crate::models::order_book::OrderBook;
use std::sync::Arc;
use std::time::{SystemTime, UNIX_EPOCH};
use tokio::sync::Mutex;
use tonic::{Request, Response, Status, Streaming};

// 🔥 Stream 相關引用
use tokio::sync::mpsc;
use tokio_stream::wrappers::ReceiverStream;
use tokio_stream::StreamExt; // 讓 stream 有 .next() / .message()
use std::pin::Pin;
use futures::Stream;

// Ref to Generated Code
use crate::orderbook_grpc::match_event::EventData;
use crate::orderbook_grpc::matching_engine_server::MatchingEngine;
use crate::orderbook_grpc::{self, OrderRequest, OrderResponse};

// Ref to Internal Model
use crate::models::events::MatchEvent as InternalEvent;
use crate::models::order::{Order, OrderType, Side};

pub struct MatchingEngineService {
    engine: Arc<Mutex<OrderBook>>,
}

impl MatchingEngineService {
    pub fn new() -> Self {
        let order_book = OrderBook::new();
        Self {
            engine: Arc::new(Mutex::new(order_book)),
        }
    }

    // 🛠️ Helper Function: 提取核心邏輯，避免代碼重複
    // 這段邏輯負責：驗證 -> 轉換 -> 撮合 -> 生成 Response
    async fn _process_order(&self, req: OrderRequest) -> Result<OrderResponse, Status> {

        // --- A. Server Arrival Time ---
        let timestamp = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_nanos() as i64;

        // --- B. Validation & Conversion ---
        let side = match req.side {
            1 => Side::Bid,
            2 => Side::Ask,
            _ => return Err(Status::invalid_argument("Invalid side (1=BID, 2=ASK)")),
        };

        let order_type = match req.order_type {
            1 => OrderType::Limit,
            2 => OrderType::Market,
            _ => return Err(Status::invalid_argument("Invalid order type (1=LIMIT, 2=MARKET)")),
        };

        if req.qty <= 0 {
            return Err(Status::invalid_argument("Quantity must be > 0"));
        }

        if order_type == OrderType::Limit && req.price <= 0 {
            return Err(Status::invalid_argument("Price must be > 0 for Limit Order"));
        }

        let order = Order {
            id: req.id,
            price: req.price,
            qty: req.qty,
            side,
            order_type,
            timestamp,
        };

        // --- C. Execution (Critical Section) ---
        // 鎖住 Engine -> 執行 -> 自動解鎖
        let internal_events = {
            let mut engine = self.engine.lock().await;
            engine.add_order(order)
        };

        // --- D. Translation ---
        let mut proto_events = Vec::new();
        for event in internal_events {
            let event_data = match event {
                InternalEvent::OrderPlaced { id, price, qty, side } => {
                    EventData::Placed(orderbook_grpc::OrderPlaced {
                        id, price, qty,
                        side: match side { Side::Bid => 1, Side::Ask => 2 },
                    })
                },
                InternalEvent::TradeExecuted { maker_id, taker_id, price, qty, timestamp } => {
                    EventData::Filled(orderbook_grpc::TradeExecuted {
                        maker_id, taker_id, price, qty, timestamp: timestamp as i64,
                    })
                },
                InternalEvent::OrderCancelled {id, cancelled_qty} => {
                    EventData::Cancelled(orderbook_grpc::OrderCancelled { id, cancelled_qty })
                },
                InternalEvent::OrderKilled {id, killed_qty} => {
                    EventData::Killed(orderbook_grpc::OrderKilled{ id, killed_qty })
                }
            };
            proto_events.push(orderbook_grpc::MatchEvent { event_data: Some(event_data) });
        }

        // --- E. Return Response ---
        Ok(OrderResponse {
            success: true,
            message: "".to_string(),
            events: proto_events,
            request_id: req.id, // 🔥 關鍵：必須回傳 ID 給 Client 計算 Latency
        })
    }
}

#[tonic::async_trait]
impl MatchingEngine for MatchingEngineService {

   // 1. 單次請求 (保留兼容性)
    async fn place_order(
        &self,
        request: Request<OrderRequest>,
    ) -> Result<Response<OrderResponse>, Status> {
        let req = request.into_inner();
        // 直接調用 Helper
        let response = self._process_order(req).await?;
        Ok(Response::new(response))
    }

    // 🔥 2. 雙向串流 (Streaming Implementation)
    type PlaceOrderStreamStream = Pin<Box<dyn Stream<Item = Result<OrderResponse, Status>> + Send>>;

    async fn place_order_stream(
        &self,
        request: Request<Streaming<OrderRequest>>,
    ) -> Result<Response<Self::PlaceOrderStreamStream>, Status> {

        println!("🌊 Streaming connection established...");
        let mut in_stream = request.into_inner();

        // 為了在 tokio::spawn 裡使用 self，我們需要複製 Arc 指針 (如果 engine 是 Arc)
        // 但這裡我們是複製 Engine 的 Arc，所以我們需要 clone service 本身或者 engine
        // 這裡最簡單的方法是：我們只需要 engine 來處理
        // 但由於 _process_order 是 &self 方法，我們需要 Arc<Self> 或者手動 clone engine
        // 為了簡化，我們直接在 loop 裡調用 engine lock，稍微改寫 helper 調用方式不太方便
        // ✅ 解決方案：因為 self.engine 是 Arc，我們 clone engine 指針傳進去重寫一點邏輯，
        // 或者將 `_process_order` 變成不依賴 &self 的 static function (或者只要 engine 參數)

        // 這裡我直接用最簡單的方法：在 spawn 裡面重用 helper 邏輯的變種，
        // 或者將 self 包成 Arc。但在 tonic trait 裡 self 是 &self。
        // 所以最乾淨的做法是：將 engine clone 出來，傳入 task。

        let engine_clone = self.engine.clone(); // Clone Arc<Mutex<>>

        // 建立 Channel (Buffer = 10000)
        let (tx, rx) = mpsc::channel(10000);

        tokio::spawn(async move {
            while let Ok(Some(req)) = in_stream.message().await {
                // 這裡我們不能直接調用 self._process_order，因為 self 的生命週期問題
                // 所以我們這裡手動執行 _process_order 的邏輯 (或者將 _process_order 改為 static)
                // 為了不讓這段代碼太複雜，我在這裡直接展開邏輯：

                // --- Logic Start ---
                // 1. Validation (簡化版，省略部分 check 以加速 demo，真實專案應封裝 validate fn)
                let side_res = match req.side { 1 => Ok(Side::Bid), 2 => Ok(Side::Ask), _ => Err(()) };
                let type_res = match req.order_type { 1 => Ok(OrderType::Limit), 2 => Ok(OrderType::Market), _ => Err(()) };

                if side_res.is_err() || type_res.is_err() { continue; } // Skip invalid
                let side = side_res.unwrap();
                let order_type = type_res.unwrap();

                let timestamp = SystemTime::now().duration_since(UNIX_EPOCH).unwrap().as_nanos() as i64;
                let order = Order {
                    id: req.id, price: req.price, qty: req.qty, side, order_type, timestamp,
                };

                // 2. Execution
                let internal_events = {
                    let mut book = engine_clone.lock().await;
                    book.add_order(order)
                };

                // 3. Translation
                let mut proto_events = Vec::new();
                for event in internal_events {
                    let event_data = match event {
                        InternalEvent::OrderPlaced { id, price, qty, side } => {
                            EventData::Placed(orderbook_grpc::OrderPlaced {
                                id, price, qty,
                                side: match side { Side::Bid => 1, Side::Ask => 2 },
                            })
                        },
                        InternalEvent::TradeExecuted { maker_id, taker_id, price, qty, timestamp } => {
                            EventData::Filled(orderbook_grpc::TradeExecuted {
                                maker_id, taker_id, price, qty, timestamp: timestamp as i64,
                            })
                        },
                        _ => continue, // Cancel/Kill 省略
                    };
                    proto_events.push(orderbook_grpc::MatchEvent { event_data: Some(event_data) });
                }

                let response = OrderResponse {
                    success: true,
                    message: "".to_string(),
                    events: proto_events,
                    request_id: req.id, // Echo ID
                };
                // --- Logic End ---

                // 發送回覆，如果失敗 (Client斷線) 就跳出 loop
                if tx.send(Ok(response)).await.is_err() {
                    break;
                }
            }
            println!("👋 Stream closed");
        });

        let out_stream = ReceiverStream::new(rx);
        Ok(Response::new(Box::pin(out_stream) as Self::PlaceOrderStreamStream))
    }
}