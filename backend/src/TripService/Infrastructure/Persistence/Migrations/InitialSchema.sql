CREATE TABLE [InboxState] (
    [Id] bigint NOT NULL IDENTITY,
    [MessageId] uniqueidentifier NOT NULL,
    [ConsumerId] uniqueidentifier NOT NULL,
    [LockId] uniqueidentifier NOT NULL,
    [RowVersion] rowversion NULL,
    [Received] datetime2 NOT NULL,
    [ReceiveCount] int NOT NULL,
    [ExpirationTime] datetime2 NULL,
    [Consumed] datetime2 NULL,
    [Delivered] datetime2 NULL,
    [LastSequenceNumber] bigint NULL,
    CONSTRAINT [PK_InboxState] PRIMARY KEY ([Id]),
    CONSTRAINT [AK_InboxState_MessageId_ConsumerId] UNIQUE ([MessageId], [ConsumerId])
);
GO


CREATE TABLE [OutboxState] (
    [OutboxId] uniqueidentifier NOT NULL,
    [LockId] uniqueidentifier NOT NULL,
    [RowVersion] rowversion NULL,
    [Created] datetime2 NOT NULL,
    [Delivered] datetime2 NULL,
    [LastSequenceNumber] bigint NULL,
    CONSTRAINT [PK_OutboxState] PRIMARY KEY ([OutboxId])
);
GO


CREATE TABLE [Trips] (
    [Id] uniqueidentifier NOT NULL,
    [RiderId] nvarchar(100) NOT NULL,
    [Pickup] nvarchar(max) NOT NULL,
    [Destination] nvarchar(max) NOT NULL,
    [Status] nvarchar(50) NOT NULL,
    [DriverId] nvarchar(100) NULL,
    [ReservationId] uniqueidentifier NULL,
    [Quote] nvarchar(max) NULL,
    [Method] nvarchar(max) NOT NULL,
    [PaymentToken] nvarchar(max) NULL,
    [Rejections] int NOT NULL,
    [ExcludedDrivers] nvarchar(max) NOT NULL,
    [Deadline] datetimeoffset NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [UpdatedAt] datetimeoffset NOT NULL,
    [Reason] nvarchar(max) NULL,
    [Version] bigint NOT NULL,
    [History] nvarchar(max) NOT NULL,
    [RequestHash] nvarchar(max) NOT NULL,
    [IdempotencyKey] nvarchar(100) NOT NULL,
    [CorrelationId] nvarchar(max) NOT NULL,
    [WorkflowRetries] int NOT NULL,
    [NextAttemptAt] datetimeoffset NULL,
    CONSTRAINT [PK_Trips] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [OutboxMessage] (
    [SequenceNumber] bigint NOT NULL IDENTITY,
    [EnqueueTime] datetime2 NULL,
    [SentTime] datetime2 NOT NULL,
    [Headers] nvarchar(max) NULL,
    [Properties] nvarchar(max) NULL,
    [InboxMessageId] uniqueidentifier NULL,
    [InboxConsumerId] uniqueidentifier NULL,
    [OutboxId] uniqueidentifier NULL,
    [MessageId] uniqueidentifier NOT NULL,
    [ContentType] nvarchar(256) NOT NULL,
    [MessageType] nvarchar(max) NOT NULL,
    [Body] nvarchar(max) NOT NULL,
    [ConversationId] uniqueidentifier NULL,
    [CorrelationId] uniqueidentifier NULL,
    [InitiatorId] uniqueidentifier NULL,
    [RequestId] uniqueidentifier NULL,
    [SourceAddress] nvarchar(256) NULL,
    [DestinationAddress] nvarchar(256) NULL,
    [ResponseAddress] nvarchar(256) NULL,
    [FaultAddress] nvarchar(256) NULL,
    [ExpirationTime] datetime2 NULL,
    CONSTRAINT [PK_OutboxMessage] PRIMARY KEY ([SequenceNumber]),
    CONSTRAINT [FK_OutboxMessage_InboxState_InboxMessageId_InboxConsumerId] FOREIGN KEY ([InboxMessageId], [InboxConsumerId]) REFERENCES [InboxState] ([MessageId], [ConsumerId]),
    CONSTRAINT [FK_OutboxMessage_OutboxState_OutboxId] FOREIGN KEY ([OutboxId]) REFERENCES [OutboxState] ([OutboxId])
);
GO


CREATE INDEX [IX_InboxState_Delivered] ON [InboxState] ([Delivered]);
GO


CREATE INDEX [IX_OutboxMessage_EnqueueTime] ON [OutboxMessage] ([EnqueueTime]);
GO


CREATE INDEX [IX_OutboxMessage_ExpirationTime] ON [OutboxMessage] ([ExpirationTime]);
GO


CREATE UNIQUE INDEX [IX_OutboxMessage_InboxMessageId_InboxConsumerId_SequenceNumber] ON [OutboxMessage] ([InboxMessageId], [InboxConsumerId], [SequenceNumber]) WHERE [InboxMessageId] IS NOT NULL AND [InboxConsumerId] IS NOT NULL;
GO


CREATE UNIQUE INDEX [IX_OutboxMessage_OutboxId_SequenceNumber] ON [OutboxMessage] ([OutboxId], [SequenceNumber]) WHERE [OutboxId] IS NOT NULL;
GO


CREATE INDEX [IX_OutboxState_Created] ON [OutboxState] ([Created]);
GO


CREATE UNIQUE INDEX [IX_Trips_RiderId] ON [Trips] ([RiderId]) WHERE [Status] IN ('Requested','AwaitingRiderConfirmation','ReservingDriver','AwaitingDriverAcceptance','AcceptingDriver','DriverAccepted','EnRoute','PickedUp');
GO


CREATE UNIQUE INDEX [IX_Trips_RiderId_IdempotencyKey] ON [Trips] ([RiderId], [IdempotencyKey]);
GO


CREATE INDEX [IX_Trips_Status_UpdatedAt] ON [Trips] ([Status], [UpdatedAt]);
GO


