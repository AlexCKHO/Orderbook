use std::env;
use std::path::PathBuf;

fn main() -> Result<(), Box<dyn std::error::Error>> {
    // 1. Get the absolute path to /Users/alex/Projects/OrderMatching/OrderMatchingEngine/
    let manifest_dir = PathBuf::from(env::var("CARGO_MANIFEST_DIR")?);

    let proto_dir = manifest_dir.parent().unwrap().join("Protos");
    let proto_file = proto_dir.join("orderbook.proto");

    println!("cargo:rerun-if-changed={}", proto_file.display());

    tonic_build::configure()
        .compile_protos(
            &[proto_file],    // Path to the file
            &[proto_dir],     // The "include" directory for imports
        )?;

    Ok(())
}