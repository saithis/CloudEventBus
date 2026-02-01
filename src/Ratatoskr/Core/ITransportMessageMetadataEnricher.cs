namespace Ratatoskr.Core;

public interface ITransportMessageMetadataEnricher
{
    void Enrich(PublishInformation publishInformation, MessageProperties properties);
}