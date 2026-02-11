# Subscription mechanics wolverine:

apikey.subscriptions (quorum queue)
	x-dead-letter-exchange:	apikey.subscriptions.dlq
	arguments:	
		x-queue-type:	quorum
	durable:	true
	binding to sso.authz.events with user-roles-changed routeKey

apikey.subscriptions.dlq (exchange)
	Type 	topic
	Features 	
		durable:	true
	binding to apikey.subscriptions.dlq with apikey.subscriptions.dlq routeKey
	
apikey.subscriptions.dlq (classic queue)
	x-dead-letter-exchange:	apikey.wolverine-dead-letter-queue
	arguments:	
		x-queue-type:	classic
	durable:	true
	queue storage version:	2
	binding to apikey.subscriptions.dlq with apikey.subscriptions.dlq routeKey
	
apikey.wolverine-dead-letter-queue (exchange)
	Type 	fanout
	Features 	
		durable:	true
	binding to apikey.wolverine-dead-letter-queue with apikey.wolverine-dead-letter-queue routeKey
	
apikey.wolverine-dead-letter-queue (classic queue)
	arguments:	
		x-queue-type:	classic
	durable:	true
	queue storage version:	2
	binding to apikey.wolverine-dead-letter-queue with apikey.wolverine-dead-letter-queue routeKey
