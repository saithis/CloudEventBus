using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Saithis.CloudEventBus.RabbitMq;
using TUnit.Core;

namespace CloudEventBus.Tests.RabbitMq;

public class RabbitMqRetryHandlerTests
{
    [Test]
    public void GetRetryCount_WhenNoHeaders_ReturnsZero()
    {
        // Arrange
        var headers = new Dictionary<string, object?>();
        
        // Act
        var count = InvokeGetRetryCount(headers);
        
        // Assert
        count.Should().Be(0);
    }

    [Test]
    public void GetRetryCount_WhenXRetryCountHeaderPresent_ReturnsValue()
    {
        // Arrange
        var headers = new Dictionary<string, object?> { ["x-retry-count"] = 2 };
        
        // Act
        var count = InvokeGetRetryCount(headers);
        
        // Assert
        count.Should().Be(2);
    }

    [Test]
    public void GetRetryCount_WhenXDeathContainsRejected_ReturnsCount()
    {
        // Arrange
        var xDeath = new List<object>
        {
            new Dictionary<string, object>
            {
                ["reason"] = "rejected",
                ["count"] = 1L
            }
        };
        var headers = new Dictionary<string, object?> { ["x-death"] = xDeath };
        
        // Act
        var count = InvokeGetRetryCount(headers);
        
        // Assert
        count.Should().Be(1);
    }

    [Test]
    public void GetRetryCount_WhenXDeathContainsBothRejectedAndExpired_ShouldOnlyCountRejected()
    {
        // Arrange
        // This simulates the scenario where a message was rejected from main queue 
        // and then expired in the retry queue.
        var xDeath = new List<object>
        {
            new Dictionary<string, object>
            {
                ["reason"] = "expired",
                ["count"] = 1L
            },
            new Dictionary<string, object>
            {
                ["reason"] = "rejected",
                ["count"] = 1L
            }
        };
        var headers = new Dictionary<string, object?> { ["x-death"] = xDeath };
        
        // Act
        var count = InvokeGetRetryCount(headers);
        
        // Assert
        // CURRENT BUG: This returns 2, which causes (attempt 3/3)
        // EXPECTED: This should return 1, which causes (attempt 2/3)
        count.Should().Be(1);
    }

    private static int InvokeGetRetryCount(IDictionary<string, object?>? headers)
    {
        // Using reflection because the method is private and static, 
        // OR if it was internal we could call it directly thanks to InternalsVisibleTo.
        // Let's check if we can call it directly. It is internal static.
        return typeof(RabbitMqRetryHandler)
            .GetMethod("GetRetryCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.Invoke(null, [headers]) as int? ?? 0;
    }
}
