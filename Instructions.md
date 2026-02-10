Create a POC to show how Dapr Works with kafka and mongo db. Create as much as you can with out asking questions.
Create a c# web based application that is themed around employee payroll. It should support CRUD operations around:
1. Basic employee demographics. Noting if the employee is salary or hourly, with thier pay rate.
1. Clocking in and out with the number of hours calculated
1. Tax information for state and federal
1. Deductions from payroll.

Application is to be implementined using Domain Driven Design.

It must support the following operations:
1. Changes to data should trigger events for other to listen to.
1. Kafka should be running in a container.
1. Mongo Db should be running in a container.
1. Any database tables should be created upon startup
1. Any topics should be created on startup
1. Database changes should be transactionally consistant with Kafka. Meaning, saving to the database and emitting an event must always occur.
1. Seed the database with at least 5 mock employee records.


# Features
## Feature 1 - Listner
1. Add mysql as a container
1. Create a new backend api called ListnerApi that will run in a docker container.
	1. It needs to support code first migrations that are applied when the application starts.
	1. It will subscribe to the employee kafka topic.			
	1. When a change from the topic is received, save them to an employee table in mysql in an idempodent fashion.
	1. The employee table needs to track the last date and time stamped on the message, if a new message is older than what is in the database, it should be ignored.
	1. The api should use graphql and support subscriptions.
	1. When data is persisted to mysql, it should send a notification to any subscribers.
1. Create a new front end call ListnerClient.
	1. The client subscribes to the ListnerApi for any employee changes.
	1. A stream of changes is displayed in the UI as they occur.

