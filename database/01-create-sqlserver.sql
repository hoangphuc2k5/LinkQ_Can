-- Tương đương migration InitialCreate của Weighing.Server.Api (dùng khi muốn tạo tay thay vì EF migration).
IF DB_ID(N'WeighingDb') IS NULL CREATE DATABASE WeighingDb;
GO
USE WeighingDb;
GO
CREATE TABLE dbo.WeighingRecords (
    Id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_WeighingRecords PRIMARY KEY,
    LicensePlate    NVARCHAR(20)  NOT NULL,
    WeightKg        DECIMAL(18,3) NOT NULL,
    Direction       INT           NOT NULL,   -- 1 = Xe vào, 2 = Xe ra
    CargoStatus     INT           NOT NULL,   -- 1 = Có hàng, 2 = Không có hàng
    WeighedAt       DATETIME2     NOT NULL,   -- UTC
    ClientId        NVARCHAR(50)  NOT NULL,
    CreatedAtServer DATETIME2     NOT NULL,   -- UTC
    ServerSequence  BIGINT        NOT NULL
);
CREATE UNIQUE INDEX IX_WeighingRecords_ServerSequence ON dbo.WeighingRecords (ServerSequence);
CREATE INDEX IX_WeighingRecords_WeighedAt ON dbo.WeighingRecords (WeighedAt);
CREATE INDEX IX_WeighingRecords_ClientId  ON dbo.WeighingRecords (ClientId);

-- Một dòng duy nhất: vừa là bộ đếm sequence, vừa là khóa tuần tự hóa các push (UPDATE giữ X-lock đến commit).
CREATE TABLE dbo.SyncCounters (Id INT NOT NULL CONSTRAINT PK_SyncCounters PRIMARY KEY, Value BIGINT NOT NULL);
INSERT INTO dbo.SyncCounters (Id, Value) VALUES (1, 0);
GO
