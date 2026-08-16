// Keep IntegrationTests concise: most tests use UoW transaction helpers and
// persistence-level request DTOs (relationship graph paging, traversal direction, etc.).

global using NGB.Runtime.UnitOfWork;
global using NGB.Persistence.Readers.Documents;

// Some integration tests prefer a simple CreateScope(fixture) helper.
// The explicit fixture keeps parallel PostgreSQL collections isolated.
global using static NGB.Runtime.IntegrationTests.Infrastructure.TestScopeFactory;

// Test helpers: allow throwing XunitException for invariant test-state failures.
global using Xunit.Sdk;
