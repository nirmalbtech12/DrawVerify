using Xunit;

// SQLite file-backed tests share the global Microsoft.Data.Sqlite connection pool.
// A parallel test calling SqliteConnection.ClearAllPools() while another test has
// an in-flight ExecuteReader can dispose the native handle out from under it,
// surfacing as ObjectDisposedException: 'SQLitePCL.sqlite3' (e.g. TSD-DIRECT-DENIAL).
// Sequential execution removes that race; suite runs in ~2s so cost is negligible.
// Precedent: DrawVerify.OcctService.Tests/AssemblyInfo.cs, DrawVerify.App.Tests.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
