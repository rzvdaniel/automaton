# API Key Implementation - Database Migration

## Required Database Migration

After implementing the API key system, you need to create and apply a database migration to add the `ApiKeys` table.

### Step 1: Create Migration

Run this command in the `Automaton.Studio.Server` project directory:

```bash
cd src/Automaton.Studio.Server
dotnet ef migrations add AddApiKeysTable
```

### Step 2: Apply Migration

```bash
dotnet ef database update
```

### Step 3: Verify Migration

The migration should create the following table:

```sql
CREATE TABLE `ApiKeys` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `Name` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `HashedKey` varchar(500) CHARACTER SET utf8mb4 NOT NULL,
    `KeyPrefix` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
    `UserId` char(36) COLLATE ascii_general_ci NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `LastUsedAt` datetime(6) NULL,
    `ExpiresAt` datetime(6) NULL,
    `IsActive` tinyint(1) NOT NULL DEFAULT 1,
    `Scope` int NOT NULL DEFAULT 1,
    `Description` varchar(500) CHARACTER SET utf8mb4 NULL,
    CONSTRAINT `PK_ApiKeys` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ApiKeys_AspNetUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE
);

CREATE UNIQUE INDEX `IX_ApiKeys_HashedKey` ON `ApiKeys` (`HashedKey`);
CREATE INDEX `IX_ApiKeys_UserId` ON `ApiKeys` (`UserId`);
CREATE INDEX `IX_ApiKeys_UserId_IsActive` ON `ApiKeys` (`UserId`, `IsActive`);
CREATE INDEX `IX_ApiKeys_LastUsedAt` ON `ApiKeys` (`LastUsedAt`);
```

## Manual Migration (if needed)

If Entity Framework migrations don't work, you can manually execute this SQL:

```sql
CREATE TABLE ApiKeys (
    Id CHAR(36) NOT NULL,
    Name VARCHAR(100) NOT NULL,
    HashedKey VARCHAR(500) NOT NULL,
    KeyPrefix VARCHAR(50) NOT NULL,
    UserId CHAR(36) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    LastUsedAt DATETIME NULL,
    ExpiresAt DATETIME NULL,
    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
    Scope INT NOT NULL DEFAULT 1,
    Description VARCHAR(500) NULL,
    PRIMARY KEY (Id),
    UNIQUE KEY IX_ApiKeys_HashedKey (HashedKey),
    KEY IX_ApiKeys_UserId (UserId),
    KEY IX_ApiKeys_UserId_IsActive (UserId, IsActive),
    KEY IX_ApiKeys_LastUsedAt (LastUsedAt),
    CONSTRAINT FK_ApiKeys_AspNetUsers_UserId 
        FOREIGN KEY (UserId) REFERENCES AspNetUsers (Id) ON DELETE CASCADE
);
```