// src/config.rs
use std::env;

pub struct AppConfig {
    pub brokers: String,
    pub topic: String,
    pub group_id: String,
    pub concurrency: usize,
}

impl AppConfig {
    pub fn from_env() -> Self {
        Self {
            brokers: env::var("REDPANDA_BROKERS").expect("Missing BROKERS"),
            topic: env::var("REDPANDA_TOPIC").unwrap_or_else(|_| "default".into()),
            group_id: env::var("REDPANDA_GROUP_ID").unwrap_or_else(|_| "default".into()),
            concurrency: env::var("CONCURRENCY")
                .unwrap_or_else(|_| "1".into())
                .parse()
                .unwrap_or(1),
        }
    }
}
