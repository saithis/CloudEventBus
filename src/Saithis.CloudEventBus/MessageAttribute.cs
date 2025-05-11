namespace Saithis.CloudEventBus;

[AttributeUsage(AttributeTargets.Class)]
public class MessageAttribute(string name, string topic, string routeKey) : Attribute
{
    public string Name { get; } = name;
    public string Topic { get; } = topic;
    public string RouteKey { get; } = routeKey;
}