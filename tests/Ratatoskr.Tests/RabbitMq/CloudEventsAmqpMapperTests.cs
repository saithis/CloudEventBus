using System.Text;
using AwesomeAssertions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Ratatoskr.CloudEvents;
using Ratatoskr.RabbitMq;

namespace Ratatoskr.Tests.RabbitMq;

public class CloudEventsAmqpMapperTests
{
    private readonly CloudEventsAmqpMapper _mapper = new(new CloudEventsOptions());

    [Test]
    public void MapBinaryModeIncoming_ShouldMapTraceContext()
    {
        // Arrange
        var headers = new Dictionary<string, object?>
        {
            { "traceparent", "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01" },
            { "tracestate", "rojo=00f067aa0ba902b7" },
            { "cloudEvents_id", "123" },
            { "cloudEvents_source", "/unit-test" },
            { "cloudEvents_type", "test.event" }
        };
        
        var basicProperties = new BasicProperties 
        { 
            Headers = headers,
            ContentType = "application/json"
        };
        
        var body = Encoding.UTF8.GetBytes("{}");
        var incoming = new BasicDeliverEventArgs("tag", 1, false, "ex", "rk", basicProperties, body);

        // Act
        var result = _mapper.MapIncoming(incoming);

        // Assert
        result.props.TraceParent.Should().Be("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");
        result.props.TraceState.Should().Be("rojo=00f067aa0ba902b7");
    }

    [Test]
    public void MapStructuredModeIncoming_ShouldMapTraceContext()
    {
        // Arrange
        var cloudEventJson = "{\"id\":\"123\",\"source\":\"/unit-test\",\"type\":\"test.event\",\"specversion\":\"1.0\",\"data\":{\"foo\":\"bar\"}}";
        var body = Encoding.UTF8.GetBytes(cloudEventJson);
        
        var headers = new Dictionary<string, object?>
        {
            { "traceparent", "00-structured-trace-id-01" },
            { "tracestate", "structured=true" }
        };

        var basicProperties = new BasicProperties 
        { 
            Headers = headers,
            ContentType = "application/cloudevents+json"
        };
        
        var incoming = new BasicDeliverEventArgs("tag", 1, false, "ex", "rk", basicProperties, body);

        // Act
        var result = _mapper.MapIncoming(incoming);

        // Assert
        result.props.TraceParent.Should().Be("00-structured-trace-id-01");
        result.props.TraceState.Should().Be("structured=true");
    }
}
