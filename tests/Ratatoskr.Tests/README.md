# CloudEventBus Tests

This test project was created following TUnit best practices with comprehensive integration tests for all three CloudEventBus libraries.

## Project Structure

```
CloudEventBus.Tests/
├── Fixtures/                    # Shared test infrastructure
│   ├── PostgresContainerFixture.cs   # PostgreSQL TestContainer (shared per session)
│   ├── RabbitMqContainerFixture.cs   # RabbitMQ TestContainer (shared per session)
│   ├── TestDbContext.cs              # Test DbContext with IOutboxDbContext
│   ├── TestMessages.cs               # Test event/message types
│   ├── ServiceCollectionExtensions.cs # Helper methods for DI setup
│   └── FailingMessageSender.cs       # Mock sender for retry testing
├── Core/                       # Core library tests
│   ├── CloudEventBusTests.cs         # Tests for ICloudEventBus
│   ├── MessageTypeRegistryTests.cs   # Tests for message type registration
│   └── CloudEventsSerializerTests.cs # Tests for CloudEvents serialization
├── Outbox/                     # EfCoreOutbox tests
│   ├── OutboxStagingCollectionTests.cs # Tests for staging collection
│   ├── OutboxMessageEntityTests.cs    # Tests for entity state transitions
│   └── OutboxIntegrationTests.cs      # Integration tests with PostgreSQL
├── RabbitMq/                   # RabbitMQ tests
│   ├── RabbitMqIntegrationTests.cs    # Integration tests with RabbitMQ
│   └── RabbitMqConnectionManagerTests.cs # Connection management tests
├── Integration/                # End-to-end tests
│   └── EndToEndTests.cs              # Full flow: Outbox → RabbitMQ
└── Testing/                    # Testing utilities tests
    ├── InMemoryMessageSenderTests.cs # Tests for InMemoryMessageSender
    └── TestAssertionsTests.cs        # Tests for TestAssertions helpers

```

## Test Coverage

### Core Library (`Saithis.CloudEventBus`)
- Message type registration and resolution
- CloudEvents serialization (Structured and Binary modes)
- Event publishing with custom properties and extensions
- Type attribute fallback behavior

### EfCoreOutbox (`Saithis.CloudEventBus.EfCoreOutbox`)
- Staging collection operations
- Outbox entity state transitions
- Retry logic with exponential backoff
- Poison message handling
- Transactional consistency with database operations
- Batch processing

### RabbitMQ (`Saithis.CloudEventBus.RabbitMq`)
- Message delivery to exchanges
- Publisher confirms
- Custom routing keys
- AMQP property mapping
- CloudEvents header handling (binary mode)
- Connection pooling

### End-to-End Integration
- Full outbox-to-RabbitMQ flow
- Direct publishing to RabbitMQ
- Multiple message types
- Structured and Binary CloudEvents modes

### Testing Utilities
- InMemoryMessageSender functionality
- Test assertion helpers
- Thread-safety verification

## TUnit Features Used

- **Shared Container Fixtures**: Using `[ClassDataSource<T>(Shared = SharedType.PerTestSession)]` for efficient container reuse
- **Async Assertions**: All assertions use `await Assert.That(...)` pattern
- **Descriptive Test Names**: Following `Method_Scenario_ExpectedBehavior` convention
- **Hooks**: `[Before(Test)]` and `[After(Test)]` for setup/cleanup
- **Parallel Execution**: Tests are independent and run in parallel by default

## Known Issues to Fix

The tests were created based on the plan but have compilation errors due to API mismatches:

1. **OutboxBuilder API**: Need to verify constructor and Options property access
2. **CloudEventBusBuilder Methods**: `UseStructuredMode()` and `UseBinaryMode()` may not exist - need to check actual API
3. **Internal Types**: `OutboxMessageEntity` and `OutboxStagingCollection.Queue` are internal - tests need adjustment or types need to be made public for testing
4. **Multiple ClassDataSource**: TUnit may not support multiple `[ClassDataSource]` attributes - need alternative approach
5. **TUnit Assertion API**: Need to adjust assertion syntax for:
   - `ContainsKey` on different dictionary types
   - `WithExactExceptionType()` may not exist
   - `IsEqualTo` for some numeric types

## Next Steps

1. Review the actual CloudEventBusBuilder API and adjust CloudEventsSerializerTests
2. Review OutboxBuilder API and adjust ServiceCollectionExtensions
3. Make OutboxMessageEntity accessible for testing (make public or add test accessors)
4. Adjust tests to use single-container fixtures or find alternative multi-fixture approach
5. Update TUnit assertion syntax to match the actual API
6. Run `dotnet build` to verify all compilation errors are resolved
7. Run `dotnet run` to execute tests

## Running Tests

```bash
# Build tests
cd tests/CloudEventBus.Tests
dotnet build

# Run all tests
dotnet run

# Run specific test class
dotnet run --filter "ClassName~CloudEventBusTests"

# Run tests with coverage
dotnet run --coverage
```

## Dependencies

- TUnit 0.4.26
- Testcontainers.PostgreSql 4.3.0
- Testcontainers.RabbitMq 4.3.0
- Microsoft.Extensions.TimeProvider.Testing 9.0.0
- All CloudEventBus libraries (via project references)
