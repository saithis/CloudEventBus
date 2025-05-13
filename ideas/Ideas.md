
# Setup

## Code Structure

https://app.code2flow.com/

```
Send Message;
if(Via Outbox?) {
  Dbcontext.OutboxMessages.Add();
  Dbcontext.SaveChangesAsync();
  OutboxTriggerInterceptor;
  IMessageSerializer;
  OutboxMessageEntity.Create();
  Add to DbContext;
  OutboxProcessor;
  Load Messages from DB;
  Deserialize Properties;
} else {
  ICloudEventBus;
  IMessageSerializer;
}
IMessageSender;
```

## Rabbit independent

* [Message("id")] or AddMessage<type>("id")
   * used to know id for publish
   * used to find type for receive

## Rabbit specific

* Manually create exchanges, queues and bindings
  * with rabbitmq client directly
  * full control
  * explicit
* Publish<Message>("exchange", "routeKey") or AddMessage<type>("id").PublishToRabbitMqExchange("exchange", "routeKey")
  * Builds lookup dict for Sender to know the exchange
* ListenToRabbitMqQueue("queue", x => x.Add<Message>("id").Add<Message2>("id"))
  * Adds rabbit queue listener
  * Registers IMessageHandler<Message>'s
    * queue listener gets message
    * finds type by id from Add<Message>() types
    * finds the handler from type
    * calls it

# AsyncApi

* Get Publish<Message>("exchange", "routeKey") config 
   * for sending info
* Get ListenToRabbitMqQueue("queue", x => x.Add<Message>().Add<Message2>()) config 
   * for receive info

