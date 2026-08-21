IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] bigint NOT NULL IDENTITY,
        [ActorUserId] bigint NULL,
        [Action] nvarchar(100) NOT NULL,
        [EntityType] nvarchar(450) NULL,
        [EntityId] bigint NULL,
        [PreviousValues] nvarchar(max) NULL,
        [NewValues] nvarchar(max) NULL,
        [IpAddress] nvarchar(max) NULL,
        [DeviceInfo] nvarchar(max) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [CreatedByUserId] bigint NULL,
        [UpdatedByUserId] bigint NULL,
        [RowVersion] varbinary(max) NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [Clients] (
        [Id] bigint NOT NULL IDENTITY,
        [Name] nvarchar(200) NOT NULL,
        [Code] nvarchar(30) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [CreatedByUserId] bigint NULL,
        [UpdatedByUserId] bigint NULL,
        [RowVersion] varbinary(max) NULL,
        CONSTRAINT [PK_Clients] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [Departments] (
        [Id] bigint NOT NULL IDENTITY,
        [Name] nvarchar(150) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [CreatedByUserId] bigint NULL,
        [UpdatedByUserId] bigint NULL,
        [RowVersion] varbinary(max) NULL,
        CONSTRAINT [PK_Departments] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [LoginAttempts] (
        [Id] bigint NOT NULL IDENTITY,
        [UserNameTried] nvarchar(256) NOT NULL,
        [Succeeded] bit NOT NULL,
        [IpAddress] nvarchar(64) NULL,
        [UserAgent] nvarchar(512) NULL,
        [FailureReason] nvarchar(200) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [CreatedByUserId] bigint NULL,
        [UpdatedByUserId] bigint NULL,
        [RowVersion] varbinary(max) NULL,
        CONSTRAINT [PK_LoginAttempts] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [Modules] (
        [Id] bigint NOT NULL IDENTITY,
        [Name] nvarchar(200) NOT NULL,
        [ProjectId] bigint NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [CreatedByUserId] bigint NULL,
        [UpdatedByUserId] bigint NULL,
        [RowVersion] varbinary(max) NULL,
        CONSTRAINT [PK_Modules] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [Notifications] (
        [Id] bigint NOT NULL IDENTITY,
        [RecipientUserId] bigint NOT NULL,
        [Title] nvarchar(300) NOT NULL,
        [Body] nvarchar(2000) NULL,
        [LinkEntityType] nvarchar(50) NULL,
        [LinkEntityId] bigint NULL,
        [IsRead] bit NOT NULL,
        [ReadAt] datetimeoffset NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [CreatedByUserId] bigint NULL,
        [UpdatedByUserId] bigint NULL,
        [RowVersion] varbinary(max) NULL,
        CONSTRAINT [PK_Notifications] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [NumberSequences] (
        [Key] nvarchar(50) NOT NULL,
        [NextValue] bigint NOT NULL,
        [Version] int NOT NULL,
        CONSTRAINT [PK_NumberSequences] PRIMARY KEY ([Key])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [PauseReasons] (
        [Id] bigint NOT NULL IDENTITY,
        [Name] nvarchar(150) NOT NULL,
        [RequiresComment] bit NOT NULL,
        [IsBlocker] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [CreatedByUserId] bigint NULL,
        [UpdatedByUserId] bigint NULL,
        [RowVersion] varbinary(max) NULL,
        CONSTRAINT [PK_PauseReasons] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [Permissions] (
        [Id] bigint NOT NULL IDENTITY,
        [Key] nvarchar(100) NOT NULL,
        [Description] nvarchar(max) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [CreatedByUserId] bigint NULL,
        [UpdatedByUserId] bigint NULL,
        [RowVersion] varbinary(max) NULL,
        CONSTRAINT [PK_Permissions] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [Projects] (
        [Id] bigint NOT NULL IDENTITY,
        [Name] nvarchar(200) NOT NULL,
        [Code] nvarchar(30) NULL,
        [ClientId] bigint NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [CreatedByUserId] bigint NULL,
        [UpdatedByUserId] bigint NULL,
        [RowVersion] varbinary(max) NULL,
        CONSTRAINT [PK_Projects] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [Roles] (
        [Id] bigint NOT NULL IDENTITY,
        [Name] nvarchar(100) NOT NULL,
        [Description] nvarchar(max) NULL,
        [IsSystemRole] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [CreatedByUserId] bigint NULL,
        [UpdatedByUserId] bigint NULL,
        [RowVersion] varbinary(max) NULL,
        CONSTRAINT [PK_Roles] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [Teams] (
        [Id] bigint NOT NULL IDENTITY,
        [Name] nvarchar(150) NOT NULL,
        [DepartmentId] bigint NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [CreatedByUserId] bigint NULL,
        [UpdatedByUserId] bigint NULL,
        [RowVersion] varbinary(max) NULL,
        CONSTRAINT [PK_Teams] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] bigint NOT NULL IDENTITY,
        [UserName] nvarchar(100) NOT NULL,
        [Email] nvarchar(256) NOT NULL,
        [DisplayName] nvarchar(200) NOT NULL,
        [PasswordHash] nvarchar(max) NOT NULL,
        [IsActive] bit NOT NULL,
        [LastLoginAt] datetimeoffset NULL,
        [FailedLoginCount] int NOT NULL,
        [LockoutEndAt] datetimeoffset NULL,
        [DepartmentId] bigint NULL,
        [TeamId] bigint NULL,
        [WorkforceState] int NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [CreatedByUserId] bigint NULL,
        [UpdatedByUserId] bigint NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [RolePermissions] (
        [RoleId] bigint NOT NULL,
        [PermissionId] bigint NOT NULL,
        CONSTRAINT [PK_RolePermissions] PRIMARY KEY ([RoleId], [PermissionId]),
        CONSTRAINT [FK_RolePermissions_Permissions_PermissionId] FOREIGN KEY ([PermissionId]) REFERENCES [Permissions] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_RolePermissions_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [RefreshTokens] (
        [Id] bigint NOT NULL IDENTITY,
        [UserId] bigint NOT NULL,
        [TokenHash] nchar(64) NOT NULL,
        [ExpiresAt] datetimeoffset NOT NULL,
        [RevokedAt] datetimeoffset NULL,
        [ReplacedByTokenHash] nchar(64) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [CreatedByUserId] bigint NULL,
        [UpdatedByUserId] bigint NULL,
        [RowVersion] varbinary(max) NULL,
        CONSTRAINT [PK_RefreshTokens] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RefreshTokens_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [Requests] (
        [Id] bigint NOT NULL IDENTITY,
        [RequestNumber] nvarchar(30) NOT NULL,
        [Title] nvarchar(300) NOT NULL,
        [Description] nvarchar(max) NOT NULL,
        [Type] int NOT NULL,
        [ProjectId] bigint NULL,
        [ClientId] bigint NULL,
        [ModuleId] bigint NULL,
        [RequestedUrgency] int NOT NULL,
        [BusinessImpact] nvarchar(max) NULL,
        [ExpectedResult] nvarchar(max) NULL,
        [CurrentResult] nvarchar(max) NULL,
        [ReproductionSteps] nvarchar(max) NULL,
        [RelatedRequestId] bigint NULL,
        [RequestedByUserId] bigint NOT NULL,
        [RequestedAt] datetimeoffset NOT NULL,
        [TargetDate] datetimeoffset NULL,
        [Status] int NOT NULL,
        [GeneratedTaskId] bigint NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [CreatedByUserId] bigint NULL,
        [UpdatedByUserId] bigint NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_Requests] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Requests_Users_RequestedByUserId] FOREIGN KEY ([RequestedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [ShiftSessions] (
        [Id] bigint NOT NULL IDENTITY,
        [UserId] bigint NOT NULL,
        [ShiftStart] datetimeoffset NOT NULL,
        [ShiftEnd] datetimeoffset NULL,
        [StartDeviceInfo] nvarchar(512) NULL,
        [StartIpAddress] nvarchar(64) NULL,
        [EndedImproperly] bit NOT NULL,
        [EndedByUserId] bigint NULL,
        [EndNote] nvarchar(500) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [CreatedByUserId] bigint NULL,
        [UpdatedByUserId] bigint NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_ShiftSessions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ShiftSessions_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [Tasks] (
        [Id] bigint NOT NULL IDENTITY,
        [TaskNumber] nvarchar(30) NOT NULL,
        [RequestId] bigint NULL,
        [Title] nvarchar(300) NOT NULL,
        [Description] nvarchar(max) NOT NULL,
        [ProjectId] bigint NULL,
        [ClientId] bigint NULL,
        [ModuleId] bigint NULL,
        [Type] int NOT NULL,
        [Priority] int NOT NULL,
        [Status] int NOT NULL,
        [PrimaryAssigneeUserId] bigint NULL,
        [ReviewerUserId] bigint NULL,
        [QCUserId] bigint NULL,
        [EstimatedEffortHours] decimal(9,2) NULL,
        [DueDate] datetimeoffset NULL,
        [AcceptanceCriteria] nvarchar(max) NULL,
        [Resolution] nvarchar(max) NULL,
        [ProgressPercent] int NOT NULL,
        [QueueOrder] int NOT NULL,
        [ParentTaskId] bigint NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [CreatedByUserId] bigint NULL,
        [UpdatedByUserId] bigint NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_Tasks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Tasks_Tasks_ParentTaskId] FOREIGN KEY ([ParentTaskId]) REFERENCES [Tasks] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Tasks_Users_PrimaryAssigneeUserId] FOREIGN KEY ([PrimaryAssigneeUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [UserRoles] (
        [UserId] bigint NOT NULL,
        [RoleId] bigint NOT NULL,
        CONSTRAINT [PK_UserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_UserRoles_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_UserRoles_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [Attachments] (
        [Id] bigint NOT NULL IDENTITY,
        [OriginalFileName] nvarchar(400) NOT NULL,
        [StoredPath] nvarchar(500) NOT NULL,
        [ContentType] nvarchar(200) NOT NULL,
        [SizeBytes] bigint NOT NULL,
        [Sha256] nchar(64) NOT NULL,
        [UploadedByUserId] bigint NOT NULL,
        [RequestId] bigint NULL,
        [TaskId] bigint NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [CreatedByUserId] bigint NULL,
        [UpdatedByUserId] bigint NULL,
        [RowVersion] varbinary(max) NULL,
        CONSTRAINT [PK_Attachments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Attachments_Requests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [Requests] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [RequestClarifications] (
        [Id] bigint NOT NULL IDENTITY,
        [RequestId] bigint NOT NULL,
        [AskedByUserId] bigint NOT NULL,
        [Question] nvarchar(2000) NOT NULL,
        [AskedAt] datetimeoffset NOT NULL,
        [AnsweredByUserId] bigint NULL,
        [Answer] nvarchar(2000) NULL,
        [AnsweredAt] datetimeoffset NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [CreatedByUserId] bigint NULL,
        [UpdatedByUserId] bigint NULL,
        [RowVersion] varbinary(max) NULL,
        CONSTRAINT [PK_RequestClarifications] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RequestClarifications_Requests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [Requests] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [ActivityEvents] (
        [Id] bigint NOT NULL IDENTITY,
        [UserId] bigint NOT NULL,
        [ShiftSessionId] bigint NULL,
        [OccurredAt] datetimeoffset NOT NULL,
        [ResultingState] int NULL,
        [Label] nvarchar(300) NOT NULL,
        [RelatedTaskId] bigint NULL,
        [Note] nvarchar(1000) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [CreatedByUserId] bigint NULL,
        [UpdatedByUserId] bigint NULL,
        [RowVersion] varbinary(max) NULL,
        CONSTRAINT [PK_ActivityEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ActivityEvents_ShiftSessions_ShiftSessionId] FOREIGN KEY ([ShiftSessionId]) REFERENCES [ShiftSessions] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ActivityEvents_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [AssignmentHistories] (
        [Id] bigint NOT NULL IDENTITY,
        [TaskId] bigint NOT NULL,
        [FromUserId] bigint NULL,
        [ToUserId] bigint NULL,
        [AssignedByUserId] bigint NOT NULL,
        [AssignedAt] datetimeoffset NOT NULL,
        [Reason] nvarchar(1000) NULL,
        [WorkTaskId] bigint NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [CreatedByUserId] bigint NULL,
        [UpdatedByUserId] bigint NULL,
        [RowVersion] varbinary(max) NULL,
        CONSTRAINT [PK_AssignmentHistories] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AssignmentHistories_Tasks_WorkTaskId] FOREIGN KEY ([WorkTaskId]) REFERENCES [Tasks] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [QCReviews] (
        [Id] bigint NOT NULL IDENTITY,
        [TaskId] bigint NOT NULL,
        [ReviewerUserId] bigint NOT NULL,
        [ReviewedAt] datetimeoffset NOT NULL,
        [Result] int NOT NULL,
        [Comments] nvarchar(4000) NULL,
        [AcceptanceCriteriaResults] nvarchar(max) NULL,
        [Environment] nvarchar(200) NULL,
        [BuildVersion] nvarchar(100) NULL,
        [AttemptNumber] int NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [CreatedByUserId] bigint NULL,
        [UpdatedByUserId] bigint NULL,
        [RowVersion] varbinary(max) NULL,
        CONSTRAINT [PK_QCReviews] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_QCReviews_Tasks_TaskId] FOREIGN KEY ([TaskId]) REFERENCES [Tasks] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [ScopeChanges] (
        [Id] bigint NOT NULL IDENTITY,
        [TaskId] bigint NOT NULL,
        [RequestedByUserId] bigint NOT NULL,
        [RequestedAt] datetimeoffset NOT NULL,
        [Description] nvarchar(2000) NOT NULL,
        [Reason] nvarchar(1000) NULL,
        [EstimatedImpactHours] decimal(9,2) NULL,
        [DeadlineImpact] datetimeoffset NULL,
        [ApprovedByUserId] bigint NULL,
        [ApprovedAt] datetimeoffset NULL,
        [WorkTaskId] bigint NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [CreatedByUserId] bigint NULL,
        [UpdatedByUserId] bigint NULL,
        [RowVersion] varbinary(max) NULL,
        CONSTRAINT [PK_ScopeChanges] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ScopeChanges_Tasks_WorkTaskId] FOREIGN KEY ([WorkTaskId]) REFERENCES [Tasks] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [StatusHistories] (
        [Id] bigint NOT NULL IDENTITY,
        [TaskId] bigint NOT NULL,
        [FromStatus] int NOT NULL,
        [ToStatus] int NOT NULL,
        [ChangedByUserId] bigint NOT NULL,
        [ChangedAt] datetimeoffset NOT NULL,
        [Reason] nvarchar(1000) NULL,
        [WasOverride] bit NOT NULL,
        [WorkTaskId] bigint NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [CreatedByUserId] bigint NULL,
        [UpdatedByUserId] bigint NULL,
        [RowVersion] varbinary(max) NULL,
        CONSTRAINT [PK_StatusHistories] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StatusHistories_Tasks_WorkTaskId] FOREIGN KEY ([WorkTaskId]) REFERENCES [Tasks] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [TaskActivities] (
        [Id] bigint NOT NULL IDENTITY,
        [TaskId] bigint NOT NULL,
        [Type] int NOT NULL,
        [ActorUserId] bigint NOT NULL,
        [OccurredAt] datetimeoffset NOT NULL,
        [Description] nvarchar(1000) NOT NULL,
        [WorkTaskId] bigint NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [CreatedByUserId] bigint NULL,
        [UpdatedByUserId] bigint NULL,
        [RowVersion] varbinary(max) NULL,
        CONSTRAINT [PK_TaskActivities] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TaskActivities_Tasks_WorkTaskId] FOREIGN KEY ([WorkTaskId]) REFERENCES [Tasks] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [TaskCollaborators] (
        [TaskId] bigint NOT NULL,
        [UserId] bigint NOT NULL,
        [AddedAt] datetimeoffset NOT NULL,
        [AddedByUserId] bigint NOT NULL,
        CONSTRAINT [PK_TaskCollaborators] PRIMARY KEY ([TaskId], [UserId]),
        CONSTRAINT [FK_TaskCollaborators_Tasks_TaskId] FOREIGN KEY ([TaskId]) REFERENCES [Tasks] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_TaskCollaborators_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [TaskComments] (
        [Id] bigint NOT NULL IDENTITY,
        [TaskId] bigint NOT NULL,
        [AuthorUserId] bigint NOT NULL,
        [Category] int NOT NULL,
        [Body] nvarchar(4000) NOT NULL,
        [VisibleToRequester] bit NOT NULL,
        [WorkTaskId] bigint NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [CreatedByUserId] bigint NULL,
        [UpdatedByUserId] bigint NULL,
        [RowVersion] varbinary(max) NULL,
        CONSTRAINT [PK_TaskComments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TaskComments_Tasks_WorkTaskId] FOREIGN KEY ([WorkTaskId]) REFERENCES [Tasks] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [TaskDependencies] (
        [Id] bigint NOT NULL IDENTITY,
        [TaskId] bigint NOT NULL,
        [RelatedTaskId] bigint NOT NULL,
        [Type] int NOT NULL,
        [WorkTaskId] bigint NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [CreatedByUserId] bigint NULL,
        [UpdatedByUserId] bigint NULL,
        [RowVersion] varbinary(max) NULL,
        CONSTRAINT [PK_TaskDependencies] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TaskDependencies_Tasks_WorkTaskId] FOREIGN KEY ([WorkTaskId]) REFERENCES [Tasks] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE TABLE [WorkSessions] (
        [Id] bigint NOT NULL IDENTITY,
        [TaskId] bigint NOT NULL,
        [UserId] bigint NOT NULL,
        [SessionStart] datetimeoffset NOT NULL,
        [SessionEnd] datetimeoffset NULL,
        [Status] int NOT NULL,
        [EndPauseReasonId] bigint NULL,
        [EndComment] nvarchar(max) NULL,
        [EndedByInterruption] bit NOT NULL,
        [InterruptedByTaskId] bigint NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [CreatedByUserId] bigint NULL,
        [UpdatedByUserId] bigint NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_WorkSessions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WorkSessions_Tasks_TaskId] FOREIGN KEY ([TaskId]) REFERENCES [Tasks] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_WorkSessions_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ActivityEvents_ShiftSessionId] ON [ActivityEvents] ([ShiftSessionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ActivityEvents_UserId_OccurredAt] ON [ActivityEvents] ([UserId], [OccurredAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AssignmentHistories_TaskId_AssignedAt] ON [AssignmentHistories] ([TaskId], [AssignedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AssignmentHistories_WorkTaskId] ON [AssignmentHistories] ([WorkTaskId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Attachments_RequestId] ON [Attachments] ([RequestId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Attachments_Sha256] ON [Attachments] ([Sha256]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Attachments_TaskId] ON [Attachments] ([TaskId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_CreatedAt] ON [AuditLogs] ([CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_EntityType_EntityId] ON [AuditLogs] ([EntityType], [EntityId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Clients_Name] ON [Clients] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Departments_Name] ON [Departments] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LoginAttempts_CreatedAt] ON [LoginAttempts] ([CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LoginAttempts_UserNameTried_CreatedAt] ON [LoginAttempts] ([UserNameTried], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Modules_ProjectId_Name] ON [Modules] ([ProjectId], [Name]) WHERE [ProjectId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Notifications_RecipientUserId_IsRead_CreatedAt] ON [Notifications] ([RecipientUserId], [IsRead], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PauseReasons_Name] ON [PauseReasons] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Permissions_Key] ON [Permissions] ([Key]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Projects_ClientId_Name] ON [Projects] ([ClientId], [Name]) WHERE [ClientId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_QCReviews_TaskId_AttemptNumber] ON [QCReviews] ([TaskId], [AttemptNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_UserId_RevokedAt] ON [RefreshTokens] ([UserId], [RevokedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_RefreshToken_TokenHash] ON [RefreshTokens] ([TokenHash]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_RequestClarifications_RequestId_AskedAt] ON [RequestClarifications] ([RequestId], [AskedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Requests_RequestedByUserId] ON [Requests] ([RequestedByUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Requests_RequestNumber] ON [Requests] ([RequestNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Requests_Status] ON [Requests] ([Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_RolePermissions_PermissionId] ON [RolePermissions] ([PermissionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Roles_Name] ON [Roles] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ScopeChanges_TaskId] ON [ScopeChanges] ([TaskId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ScopeChanges_WorkTaskId] ON [ScopeChanges] ([WorkTaskId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ShiftSessions_ShiftEnd] ON [ShiftSessions] ([ShiftEnd]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ShiftSessions_UserId_ShiftStart] ON [ShiftSessions] ([UserId], [ShiftStart]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_ShiftSession_OneOpenPerUser] ON [ShiftSessions] ([UserId]) WHERE [ShiftEnd] IS NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_StatusHistories_TaskId_ChangedAt] ON [StatusHistories] ([TaskId], [ChangedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_StatusHistories_WorkTaskId] ON [StatusHistories] ([WorkTaskId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TaskActivities_TaskId_OccurredAt] ON [TaskActivities] ([TaskId], [OccurredAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TaskActivities_WorkTaskId] ON [TaskActivities] ([WorkTaskId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TaskCollaborators_UserId] ON [TaskCollaborators] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TaskComments_TaskId_CreatedAt] ON [TaskComments] ([TaskId], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TaskComments_WorkTaskId] ON [TaskComments] ([WorkTaskId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TaskDependencies_TaskId_RelatedTaskId_Type] ON [TaskDependencies] ([TaskId], [RelatedTaskId], [Type]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TaskDependencies_WorkTaskId] ON [TaskDependencies] ([WorkTaskId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Tasks_ParentTaskId] ON [Tasks] ([ParentTaskId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Tasks_PrimaryAssigneeUserId_QueueOrder] ON [Tasks] ([PrimaryAssigneeUserId], [QueueOrder]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Tasks_PrimaryAssigneeUserId_Status] ON [Tasks] ([PrimaryAssigneeUserId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Tasks_Status] ON [Tasks] ([Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Tasks_TaskNumber] ON [Tasks] ([TaskNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Teams_DepartmentId_Name] ON [Teams] ([DepartmentId], [Name]) WHERE [DepartmentId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserRoles_RoleId] ON [UserRoles] ([RoleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_UserName] ON [Users] ([UserName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WorkSessions_TaskId] ON [WorkSessions] ([TaskId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_WorkSession_OneActivePerUser] ON [WorkSessions] ([UserId]) WHERE [Status] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821203255_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260821203255_InitialCreate', N'8.0.8');
END;
GO

COMMIT;
GO

