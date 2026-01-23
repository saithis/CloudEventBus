namespace Saithis.CloudEventBus.CloudEvents;

public static class CloudEventsConstants
{
    public const string SpecVersion = "1.0";
    public const string JsonContentType = "application/cloudevents+json";
    
    // Header prefixes for binary content mode
    public const string HeaderPrefix = "ce-";
    public const string IdHeader = "ce-id";
    public const string SourceHeader = "ce-source";
    public const string TypeHeader = "ce-type";
    public const string SpecVersionHeader = "ce-specversion";
    public const string TimeHeader = "ce-time";
    public const string DataContentTypeHeader = "ce-datacontenttype";
}
