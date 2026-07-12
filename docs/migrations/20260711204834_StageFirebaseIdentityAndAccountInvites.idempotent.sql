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
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701191518_InitialCreate'
)
BEGIN
    CREATE TABLE [Members] (
        [MemberID] int NOT NULL IDENTITY,
        [FirstName] nvarchar(50) NOT NULL,
        [LastName] nvarchar(50) NOT NULL,
        [Gender] nvarchar(10) NOT NULL,
        [BirthDate] datetime2 NOT NULL,
        [PhoneNumber] nvarchar(20) NOT NULL,
        [Email] nvarchar(100) NULL,
        [Address] nvarchar(255) NULL,
        [EmergencyContact] nvarchar(100) NOT NULL,
        [ProfilePicture] nvarchar(max) NULL,
        [QRCode] nvarchar(100) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [DateRegistered] datetime2 NOT NULL,
        [LastModified] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        CONSTRAINT [PK_Members] PRIMARY KEY ([MemberID])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701191518_InitialCreate'
)
BEGIN
    CREATE TABLE [MembershipPlans] (
        [PlanID] int NOT NULL IDENTITY,
        [PlanName] nvarchar(50) NOT NULL,
        [DurationDays] int NOT NULL,
        [Price] decimal(18,2) NOT NULL,
        [Description] nvarchar(255) NULL,
        [Status] nvarchar(20) NOT NULL,
        [LastModified] datetime2 NOT NULL,
        CONSTRAINT [PK_MembershipPlans] PRIMARY KEY ([PlanID])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701191518_InitialCreate'
)
BEGIN
    CREATE TABLE [Users] (
        [UserID] int NOT NULL IDENTITY,
        [Username] nvarchar(50) NOT NULL,
        [Email] nvarchar(255) NOT NULL,
        [PasswordHash] nvarchar(255) NOT NULL,
        [FirstName] nvarchar(100) NOT NULL,
        [LastName] nvarchar(100) NOT NULL,
        [Role] int NOT NULL,
        [IsActive] bit NOT NULL,
        [EmailVerified] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [LastLoginAt] datetime2 NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([UserID])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701191518_InitialCreate'
)
BEGIN
    CREATE TABLE [WalkInVisitors] (
        [VisitorID] int NOT NULL IDENTITY,
        [VisitorName] nvarchar(100) NOT NULL,
        [VisitDate] datetime2 NOT NULL,
        [FeePaid] decimal(18,2) NOT NULL,
        [Purpose] nvarchar(255) NULL,
        CONSTRAINT [PK_WalkInVisitors] PRIMARY KEY ([VisitorID])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701191518_InitialCreate'
)
BEGIN
    CREATE TABLE [AttendanceLogs] (
        [AttendanceID] int NOT NULL IDENTITY,
        [MemberID] int NOT NULL,
        [AttendanceDate] datetime2 NOT NULL,
        [CheckInTime] datetime2 NOT NULL,
        [CheckOutTime] datetime2 NULL,
        [LastModified] datetime2 NOT NULL,
        CONSTRAINT [PK_AttendanceLogs] PRIMARY KEY ([AttendanceID]),
        CONSTRAINT [FK_AttendanceLogs_Members_MemberID] FOREIGN KEY ([MemberID]) REFERENCES [Members] ([MemberID]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701191518_InitialCreate'
)
BEGIN
    CREATE TABLE [Notifications] (
        [NotificationID] int NOT NULL IDENTITY,
        [MemberID] int NOT NULL,
        [Title] nvarchar(100) NOT NULL,
        [Message] nvarchar(max) NOT NULL,
        [Status] int NOT NULL,
        [ScheduledTime] datetime2 NOT NULL,
        [SentTime] datetime2 NULL,
        CONSTRAINT [PK_Notifications] PRIMARY KEY ([NotificationID]),
        CONSTRAINT [FK_Notifications_Members_MemberID] FOREIGN KEY ([MemberID]) REFERENCES [Members] ([MemberID]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701191518_InitialCreate'
)
BEGIN
    CREATE TABLE [Subscriptions] (
        [SubscriptionID] int NOT NULL IDENTITY,
        [MemberID] int NOT NULL,
        [PlanID] int NOT NULL,
        [StartDate] datetime2 NOT NULL,
        [EndDate] datetime2 NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [LastModified] datetime2 NOT NULL,
        CONSTRAINT [PK_Subscriptions] PRIMARY KEY ([SubscriptionID]),
        CONSTRAINT [FK_Subscriptions_Members_MemberID] FOREIGN KEY ([MemberID]) REFERENCES [Members] ([MemberID]) ON DELETE CASCADE,
        CONSTRAINT [FK_Subscriptions_MembershipPlans_PlanID] FOREIGN KEY ([PlanID]) REFERENCES [MembershipPlans] ([PlanID]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701191518_InitialCreate'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [LogID] int NOT NULL IDENTITY,
        [UserID] int NOT NULL,
        [Action] nvarchar(100) NOT NULL,
        [Details] nvarchar(max) NOT NULL,
        [Timestamp] datetime2 NOT NULL,
        [IPAddress] nvarchar(50) NOT NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([LogID]),
        CONSTRAINT [FK_AuditLogs_Users_UserID] FOREIGN KEY ([UserID]) REFERENCES [Users] ([UserID]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701191518_InitialCreate'
)
BEGIN
    CREATE TABLE [MembershipPauses] (
        [PauseID] int NOT NULL IDENTITY,
        [SubscriptionID] int NOT NULL,
        [PauseStartDate] datetime2 NOT NULL,
        [PauseEndDate] datetime2 NULL,
        [Reason] nvarchar(255) NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        CONSTRAINT [PK_MembershipPauses] PRIMARY KEY ([PauseID]),
        CONSTRAINT [FK_MembershipPauses_Subscriptions_SubscriptionID] FOREIGN KEY ([SubscriptionID]) REFERENCES [Subscriptions] ([SubscriptionID]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701191518_InitialCreate'
)
BEGIN
    CREATE TABLE [Payments] (
        [PaymentID] int NOT NULL IDENTITY,
        [MemberID] int NOT NULL,
        [SubscriptionID] int NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Discount] decimal(18,2) NOT NULL,
        [FinalAmount] decimal(18,2) NOT NULL,
        [PaymentMethod] int NOT NULL,
        [PaymentStatus] int NOT NULL,
        [ReceiptNumber] nvarchar(50) NOT NULL,
        [ReferenceNumber] nvarchar(100) NULL,
        [DatePaid] datetime2 NOT NULL,
        [LastModified] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        CONSTRAINT [PK_Payments] PRIMARY KEY ([PaymentID]),
        CONSTRAINT [FK_Payments_Members_MemberID] FOREIGN KEY ([MemberID]) REFERENCES [Members] ([MemberID]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Payments_Subscriptions_SubscriptionID] FOREIGN KEY ([SubscriptionID]) REFERENCES [Subscriptions] ([SubscriptionID]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701191518_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AttendanceLogs_MemberID] ON [AttendanceLogs] ([MemberID]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701191518_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_UserID] ON [AuditLogs] ([UserID]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701191518_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Members_Email] ON [Members] ([Email]) WHERE [Email] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701191518_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Members_PhoneNumber] ON [Members] ([PhoneNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701191518_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Members_QRCode] ON [Members] ([QRCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701191518_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_MembershipPauses_SubscriptionID] ON [MembershipPauses] ([SubscriptionID]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701191518_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_MembershipPlans_PlanName] ON [MembershipPlans] ([PlanName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701191518_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Notifications_MemberID] ON [Notifications] ([MemberID]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701191518_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Payments_MemberID] ON [Payments] ([MemberID]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701191518_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Payments_ReceiptNumber] ON [Payments] ([ReceiptNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701191518_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Payments_SubscriptionID] ON [Payments] ([SubscriptionID]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701191518_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Subscriptions_MemberID] ON [Subscriptions] ([MemberID]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701191518_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Subscriptions_PlanID] ON [Subscriptions] ([PlanID]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701191518_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701191518_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_Username] ON [Users] ([Username]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701191518_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260701191518_InitialCreate', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701192339_AddUserTokens'
)
BEGIN
    ALTER TABLE [Users] ADD [ResetToken] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701192339_AddUserTokens'
)
BEGIN
    ALTER TABLE [Users] ADD [ResetTokenExpires] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701192339_AddUserTokens'
)
BEGIN
    ALTER TABLE [Users] ADD [VerificationToken] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701192339_AddUserTokens'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260701192339_AddUserTokens', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701193502_MakeAuditLogUserNullable'
)
BEGIN
    ALTER TABLE [AuditLogs] DROP CONSTRAINT [FK_AuditLogs_Users_UserID];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701193502_MakeAuditLogUserNullable'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AuditLogs]') AND [c].[name] = N'UserID');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [AuditLogs] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [AuditLogs] ALTER COLUMN [UserID] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701193502_MakeAuditLogUserNullable'
)
BEGIN
    ALTER TABLE [AuditLogs] ADD CONSTRAINT [FK_AuditLogs_Users_UserID] FOREIGN KEY ([UserID]) REFERENCES [Users] ([UserID]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701193502_MakeAuditLogUserNullable'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260701193502_MakeAuditLogUserNullable', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260702013356_AddSystemSettings'
)
BEGIN
    CREATE TABLE [SystemSettings] (
        [SettingKey] nvarchar(100) NOT NULL,
        [SettingValue] nvarchar(max) NOT NULL,
        [GroupName] nvarchar(100) NOT NULL,
        [Description] nvarchar(255) NULL,
        [LastModified] datetime2 NOT NULL,
        CONSTRAINT [PK_SystemSettings] PRIMARY KEY ([SettingKey])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260702013356_AddSystemSettings'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'SettingKey', N'Description', N'GroupName', N'LastModified', N'SettingValue') AND [object_id] = OBJECT_ID(N'[SystemSettings]'))
        SET IDENTITY_INSERT [SystemSettings] ON;
    EXEC(N'INSERT INTO [SystemSettings] ([SettingKey], [Description], [GroupName], [LastModified], [SettingValue])
    VALUES (N''AllowedImageTypes'', N''Comma-separated list of approved image file extensions.'', N''Security'', ''2026-07-02T00:00:00.0000000Z'', N''.jpg,.jpeg,.png''),
    (N''ContactNumber'', N''Gym contact helpline phone number.'', N''General'', ''2026-07-02T00:00:00.0000000Z'', N''+639170000000''),
    (N''Currency'', N''Currency code used for financial billing transactions.'', N''General'', ''2026-07-02T00:00:00.0000000Z'', N''PHP''),
    (N''GymName'', N''Name of the gym facility.'', N''General'', ''2026-07-02T00:00:00.0000000Z'', N''GymTrackPro''),
    (N''MaxUploadSize'', N''Maximum member photo upload limit size in bytes (e.g. 5MB = 5242880).'', N''Security'', ''2026-07-02T00:00:00.0000000Z'', N''5242880''),
    (N''PasswordPolicyRegex'', N''Regex pattern validating password strength rules.'', N''Security'', ''2026-07-02T00:00:00.0000000Z'', N''^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$''),
    (N''QRPrefix'', N''Format prefix added to automatically generated member QR codes.'', N''Membership'', ''2026-07-02T00:00:00.0000000Z'', N''GTP-''),
    (N''ReceiptPrefix'', N''Format prefix added to payment invoice transaction receipts.'', N''Payments'', ''2026-07-02T00:00:00.0000000Z'', N''REC-''),
    (N''ReminderDaysBeforeExpiration'', N''Days ahead of membership expiration to raise alerts or send reminders.'', N''Membership'', ''2026-07-02T00:00:00.0000000Z'', N''3''),
    (N''Timezone'', N''System local timezone identifier.'', N''General'', ''2026-07-02T00:00:00.0000000Z'', N''Asia/Manila'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'SettingKey', N'Description', N'GroupName', N'LastModified', N'SettingValue') AND [object_id] = OBJECT_ID(N'[SystemSettings]'))
        SET IDENTITY_INSERT [SystemSettings] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260702013356_AddSystemSettings'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260702013356_AddSystemSettings', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711204834_StageFirebaseIdentityAndAccountInvites'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Users]') AND [c].[name] = N'PasswordHash');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Users] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [Users] ALTER COLUMN [PasswordHash] nvarchar(255) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711204834_StageFirebaseIdentityAndAccountInvites'
)
BEGIN
    ALTER TABLE [Users] ADD [FirebaseUid] nvarchar(128) COLLATE Latin1_General_100_BIN2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711204834_StageFirebaseIdentityAndAccountInvites'
)
BEGIN
    ALTER TABLE [Users] ADD [MemberID] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711204834_StageFirebaseIdentityAndAccountInvites'
)
BEGIN
    ALTER TABLE [Users] ADD [NormalizedEmail] nvarchar(255) COLLATE Latin1_General_100_BIN2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711204834_StageFirebaseIdentityAndAccountInvites'
)
BEGIN
    EXEC(N'ALTER TABLE [Users] ADD CONSTRAINT [CK_Users_FirebaseUidNotBlank] CHECK ([FirebaseUid] IS NULL OR LEN([FirebaseUid]) > 0)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711204834_StageFirebaseIdentityAndAccountInvites'
)
BEGIN
    EXEC(N'ALTER TABLE [Users] ADD CONSTRAINT [CK_Users_NormalizedEmailNotBlank] CHECK ([NormalizedEmail] IS NULL OR LEN([NormalizedEmail]) > 0)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711204834_StageFirebaseIdentityAndAccountInvites'
)
BEGIN
    EXEC(N'ALTER TABLE [Users] ADD CONSTRAINT [CK_Users_Role] CHECK ([Role] IN (0, 1, 2))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711204834_StageFirebaseIdentityAndAccountInvites'
)
BEGIN
    EXEC(N'ALTER TABLE [Users] ADD CONSTRAINT [CK_Users_RoleMemberLink] CHECK (([Role] = 2 AND [MemberID] IS NOT NULL) OR ([Role] IN (0, 1) AND [MemberID] IS NULL))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711204834_StageFirebaseIdentityAndAccountInvites'
)
BEGIN
    CREATE TABLE [MemberProjectionVersions] (
        [MemberID] int NOT NULL,
        [Version] bigint NOT NULL DEFAULT CAST(0 AS bigint),
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_MemberProjectionVersions] PRIMARY KEY ([MemberID]),
        CONSTRAINT [CK_MemberProjectionVersions_VersionRange] CHECK ([Version] >= 0 AND [Version] <= 2199023255551),
        CONSTRAINT [FK_MemberProjectionVersions_Members_MemberID] FOREIGN KEY ([MemberID]) REFERENCES [Members] ([MemberID]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711204834_StageFirebaseIdentityAndAccountInvites'
)
BEGIN
    INSERT INTO [MemberProjectionVersions] ([MemberID], [Version]) SELECT [MemberID], 0 FROM [Members];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711204834_StageFirebaseIdentityAndAccountInvites'
)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [SystemSettings] WHERE [SettingKey] = N'StaleSessionHours') INSERT INTO [SystemSettings] ([SettingKey], [SettingValue], [GroupName], [Description], [LastModified]) VALUES (N'StaleSessionHours', N'16', N'Attendance', N'Hours after which an open attendance session is considered stale.', CAST('2026-07-02T00:00:00.0000000' AS datetime2));
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711204834_StageFirebaseIdentityAndAccountInvites'
)
BEGIN
    CREATE TABLE [AccountInvites] (
        [AccountInviteID] int NOT NULL IDENTITY,
        [TargetMemberID] int NULL,
        [TargetUserID] int NULL,
        [TokenHash] binary(32) NOT NULL,
        [NormalizedEmail] nvarchar(255) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [IntendedRole] int NOT NULL,
        [Purpose] nvarchar(100) NOT NULL,
        [CreatedByUserID] int NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        [UsedAtUtc] datetime2 NULL,
        [RevokedAtUtc] datetime2 NULL,
        [UsedByFirebaseUid] nvarchar(128) COLLATE Latin1_General_100_BIN2 NULL,
        [RedemptionOperationId] uniqueidentifier NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_AccountInvites] PRIMARY KEY ([AccountInviteID]),
        CONSTRAINT [CK_AccountInvites_ExactlyOneTarget] CHECK (([TargetMemberID] IS NOT NULL AND [TargetUserID] IS NULL) OR ([TargetMemberID] IS NULL AND [TargetUserID] IS NOT NULL)),
        CONSTRAINT [CK_AccountInvites_ExpiryAfterCreation] CHECK ([ExpiresAtUtc] > [CreatedAtUtc]),
        CONSTRAINT [CK_AccountInvites_NormalizedEmailNotBlank] CHECK (LEN([NormalizedEmail]) > 0),
        CONSTRAINT [CK_AccountInvites_PurposeNotBlank] CHECK (LEN(LTRIM(RTRIM([Purpose]))) > 0),
        CONSTRAINT [CK_AccountInvites_RevokedTimestampAfterCreation] CHECK ([RevokedAtUtc] IS NULL OR [RevokedAtUtc] >= [CreatedAtUtc]),
        CONSTRAINT [CK_AccountInvites_TargetRole] CHECK (([TargetMemberID] IS NOT NULL AND [IntendedRole] = 2) OR ([TargetUserID] IS NOT NULL AND [IntendedRole] IN (0, 1))),
        CONSTRAINT [CK_AccountInvites_RedemptionMetadata] CHECK (([UsedAtUtc] IS NULL AND [UsedByFirebaseUid] IS NULL AND [RedemptionOperationId] IS NULL) OR ([UsedAtUtc] IS NOT NULL AND [UsedByFirebaseUid] IS NOT NULL AND [RedemptionOperationId] IS NOT NULL AND [RedemptionOperationId] <> CAST('00000000-0000-0000-0000-000000000000' AS uniqueidentifier))),
        CONSTRAINT [CK_AccountInvites_UsedOrRevoked] CHECK ([UsedAtUtc] IS NULL OR [RevokedAtUtc] IS NULL),
        CONSTRAINT [CK_AccountInvites_UsedBeforeExpiry] CHECK ([UsedAtUtc] IS NULL OR [UsedAtUtc] < [ExpiresAtUtc]),
        CONSTRAINT [CK_AccountInvites_UsedTimestampAfterCreation] CHECK ([UsedAtUtc] IS NULL OR [UsedAtUtc] >= [CreatedAtUtc]),
        CONSTRAINT [CK_AccountInvites_UsedUidNotBlank] CHECK ([UsedByFirebaseUid] IS NULL OR LEN([UsedByFirebaseUid]) > 0),
        CONSTRAINT [FK_AccountInvites_Members_TargetMemberID] FOREIGN KEY ([TargetMemberID]) REFERENCES [Members] ([MemberID]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AccountInvites_Users_CreatedByUserID] FOREIGN KEY ([CreatedByUserID]) REFERENCES [Users] ([UserID]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AccountInvites_Users_TargetUserID] FOREIGN KEY ([TargetUserID]) REFERENCES [Users] ([UserID]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711204834_StageFirebaseIdentityAndAccountInvites'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Users_NormalizedEmail] ON [Users] ([NormalizedEmail]) WHERE [NormalizedEmail] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711204834_StageFirebaseIdentityAndAccountInvites'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Users_FirebaseUid] ON [Users] ([FirebaseUid]) WHERE [FirebaseUid] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711204834_StageFirebaseIdentityAndAccountInvites'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Users_MemberID] ON [Users] ([MemberID]) WHERE [MemberID] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711204834_StageFirebaseIdentityAndAccountInvites'
)
BEGIN
    CREATE INDEX [IX_AccountInvites_CreatedByUserID] ON [AccountInvites] ([CreatedByUserID]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711204834_StageFirebaseIdentityAndAccountInvites'
)
BEGIN
    CREATE INDEX [IX_AccountInvites_NormalizedEmail] ON [AccountInvites] ([NormalizedEmail]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711204834_StageFirebaseIdentityAndAccountInvites'
)
BEGIN
    CREATE INDEX [IX_AccountInvites_TargetMemberID] ON [AccountInvites] ([TargetMemberID]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711204834_StageFirebaseIdentityAndAccountInvites'
)
BEGIN
    CREATE INDEX [IX_AccountInvites_TargetUserID] ON [AccountInvites] ([TargetUserID]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711204834_StageFirebaseIdentityAndAccountInvites'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_AccountInvites_RedemptionOperationId] ON [AccountInvites] ([RedemptionOperationId]) WHERE [RedemptionOperationId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711204834_StageFirebaseIdentityAndAccountInvites'
)
BEGIN
    CREATE UNIQUE INDEX [UX_AccountInvites_TokenHash] ON [AccountInvites] ([TokenHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711204834_StageFirebaseIdentityAndAccountInvites'
)
BEGIN
    ALTER TABLE [Users] ADD CONSTRAINT [FK_Users_Members_MemberID] FOREIGN KEY ([MemberID]) REFERENCES [Members] ([MemberID]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711204834_StageFirebaseIdentityAndAccountInvites'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260711204834_StageFirebaseIdentityAndAccountInvites', N'10.0.9');
END;

COMMIT;
GO

