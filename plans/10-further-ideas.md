# Plan 10: Refine further ideas

* Support multiple handlers for the same message
* Think about AsyncApi support. Especially in combination with incoming messages
* Think about a UI for status and requeue of poisoned messages
* Dead letter queue configuration
* Observabillity (metrics, traces)
* Testing:
  - InMemoryMessageSender that collects messages for assertions
  - FakeOutboxProcessor that processes synchronously for tests
  - Test helpers like bus.ShouldHavePublished<T>(predicate)
* Consider using System.Threading.Channels - Instead of the polling + distributed lock approach, you could use channels for in-process triggering with the database as the fallback for crash recovery.
* Message versioning - You'll eventually need to handle schema evolution. Consider adding a Version field early or using a type naming convention like order.created.v2.
* The distributed lock dependency - Requiring users to set up IDistributedLockProvider is friction. Consider making it optional or providing a default (e.g., database-based advisory locks for PostgreSQL/SQL Server).
* Documentation