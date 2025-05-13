﻿namespace Saithis.CloudEventBus.Core;

public interface IMessageSender
{
    Task SendAsync(byte[] content, MessageProperties props, CancellationToken cancellationToken);
}