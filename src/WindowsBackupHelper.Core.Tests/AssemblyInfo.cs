// Repository tests each open a real temp-file SQLite connection (deliberately not in-memory --
// see SqliteTestDatabase). Running test classes in parallel (xUnit's default) occasionally races
// inside SQLitePCLRaw's native handle bookkeeping when many connections open/close concurrently,
// causing rare, unreproducible failures unrelated to the code under test. The suite is small
// enough that running sequentially costs nothing worth trading for that flakiness.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
