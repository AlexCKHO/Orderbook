// src/config.rs
use std::env;

pub struct AppConfig {
    pub brokers: String,
    pub cmd_topic: String,
    pub public_events_topic: String,
    pub ind_events_topic: String,
    pub group_id: String,
    pub concurrency: usize,
    pub use_historical_data: bool,
    pub historical_orders_grpc_addr: String,
}

impl AppConfig {
    pub fn from_env() -> Self {
        Self {
            brokers: env::var("REDPANDA_BROKERS").expect("Missing BROKERS"),
            cmd_topic: env::var("REDPANDA_CMD_TOPIC").unwrap_or_else(|_| "default".into()),
            public_events_topic: env::var("REDPANDA_EVENTS_PUBLIC_TOPIC")
                .unwrap_or_else(|_| "default".into()),
            ind_events_topic: env::var("REDPANDA_EVENTS_IND_TOPIC")
                .unwrap_or_else(|_| "default".into()),
            group_id: env::var("REDPANDA_GROUP_ID").unwrap_or_else(|_| "default".into()),
            concurrency: env::var("CONCURRENCY")
                .unwrap_or_else(|_| "1".into())
                .parse()
                .unwrap_or(1),
            use_historical_data: env::var("USE_HISTORICAL_DATA")
                .unwrap_or_else(|_| "false".into())
                .parse()
                .unwrap_or(false),
            historical_orders_grpc_addr: env::var("HISTORICAL_ORDERS_GRPC_ADDR")
                .unwrap_or_else(|_| "default".into()),
        }
    }
}
