global using Xunit;
global using Shouldly;

// LiteDB's BsonMapper.Global has non-thread-safe lazy state initialization.
// Disable cross-class parallelism to avoid race conditions during type registration.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
