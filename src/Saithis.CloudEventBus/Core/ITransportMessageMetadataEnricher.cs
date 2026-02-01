namespace Saithis.CloudEventBus.Core;

public interface ITransportMessageMetadataEnricher
{
    void Enrich(PublishInformation publishInformation, MessageProperties properties);
}