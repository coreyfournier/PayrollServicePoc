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
