fn main() -> Result<(), Box<dyn std::error::Error>> {
    // 1. Define file paths
    // "../Protos/orderbook.proto" indicates looking for the Protos folder in the parent directory
    let proto_file = "../Protos/orderbook.proto";
    let proto_dir = "../Protos";

    // 2. [CRITICAL] Instruct Cargo to re-run this build script if the external file changes
    // If you omit this line, Rust won't detect changes to the .proto file and will continue using the old code!
    println!("cargo:rerun-if-changed={}", proto_file);

    // 3. Compilation
    // Using configure().compile() here provides more flexibility than the standard compile_protos()
    tonic_build::configure()
        .build_server(true) // We are the Server side, so this must be set to true
        .compile_protos(
            &[proto_file], // The list of files to compile
            &[proto_dir],  // The root directory for imports (used to resolve imports if your proto file depends on others)
        )?;

    Ok(())
}