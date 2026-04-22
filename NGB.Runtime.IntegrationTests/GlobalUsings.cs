// Keep IntegrationTests concise: most tests use UoW transaction helpers and
// persistence-level request DTOs (relationship graph paging, traversal direction, etc.).

global using NGB.Runtime.UnitOfWork;
global using NGB.Persistence.Readers.Documents;

// Some integration tests prefer a simple CreateScope() helper.
// It creates an owned Host+Scope pair bound to the current PostgresTestFixture connection string.
global using static NGB.Runtime.IntegrationTests.Infrastructure.TestScopeFactory;

// Test helpers: allow throwing XunitException for invariant test-state failures.
global using Xunit.Sdk;
