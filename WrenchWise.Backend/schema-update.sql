-- WrenchWise schema update: add missing columns and tables
-- Run this against your production PostgreSQL database

-- 1. Add Parts JSON column to MaintenanceRecords
ALTER TABLE "MaintenanceRecords" ADD COLUMN IF NOT EXISTS "Parts" jsonb;

-- 2. Add Rotations JSON column to TireRecords
ALTER TABLE "TireRecords" ADD COLUMN IF NOT EXISTS "Rotations" jsonb;

-- 3. Add ColorHex to Vehicles (if missing)
ALTER TABLE "Vehicles" ADD COLUMN IF NOT EXISTS "ColorHex" text DEFAULT '#594AE2';

-- 4. Create VehicleProjects table
CREATE TABLE IF NOT EXISTS "VehicleProjects" (
    "Id" uuid NOT NULL PRIMARY KEY,
    "VehicleId" uuid NOT NULL,
    "Title" text NOT NULL DEFAULT '',
    "Description" text NOT NULL DEFAULT '',
    "EstimatedCost" numeric NOT NULL DEFAULT 0,
    "ActualCost" numeric NOT NULL DEFAULT 0,
    "TargetDate" date,
    "Status" text NOT NULL DEFAULT '',
    "UpdatedUtc" timestamp with time zone NOT NULL DEFAULT now()
);

-- 5. Create VehicleDocuments table
CREATE TABLE IF NOT EXISTS "VehicleDocuments" (
    "Id" uuid NOT NULL PRIMARY KEY,
    "VehicleId" uuid NOT NULL,
    "DocumentType" text NOT NULL DEFAULT '',
    "Provider" text NOT NULL DEFAULT '',
    "PolicyNumber" text NOT NULL DEFAULT '',
    "EffectiveDate" date,
    "ExpirationDate" date,
    "PremiumCost" numeric NOT NULL DEFAULT 0,
    "Notes" text NOT NULL DEFAULT '',
    "FilePath" text NOT NULL DEFAULT '',
    "FileName" text NOT NULL DEFAULT '',
    "UpdatedUtc" timestamp with time zone NOT NULL DEFAULT now()
);

-- 6. Create ActivityLog table
CREATE TABLE IF NOT EXISTS "ActivityLog" (
    "Id" uuid NOT NULL PRIMARY KEY,
    "TimestampUtc" timestamp with time zone NOT NULL DEFAULT now(),
    "Category" text NOT NULL DEFAULT '',
    "Message" text NOT NULL DEFAULT '',
    "Details" text NOT NULL DEFAULT '',
    "Severity" text NOT NULL DEFAULT 'Info',
    "VehicleId" uuid,
    "EntityId" uuid
);

CREATE INDEX IF NOT EXISTS "IX_ActivityLog_TimestampUtc" ON "ActivityLog" ("TimestampUtc");

-- 7. Add TripId to FuelRecords and MaintenanceRecords
ALTER TABLE "FuelRecords" ADD COLUMN IF NOT EXISTS "TripId" uuid;
ALTER TABLE "MaintenanceRecords" ADD COLUMN IF NOT EXISTS "TripId" uuid;

-- 8. Create Trips table
CREATE TABLE IF NOT EXISTS "Trips" (
    "Id" uuid NOT NULL PRIMARY KEY,
    "VehicleId" uuid NOT NULL,
    "Name" text NOT NULL DEFAULT '',
    "StartDate" date NOT NULL,
    "StartOdometer" integer NOT NULL,
    "EndDate" date,
    "EndOdometer" integer,
    "UpdatedUtc" timestamp with time zone NOT NULL DEFAULT now()
);

-- 9. Create TripExpenses table
CREATE TABLE IF NOT EXISTS "TripExpenses" (
    "Id" uuid NOT NULL PRIMARY KEY,
    "TripId" uuid NOT NULL,
    "ExpenseDate" date NOT NULL,
    "Category" text NOT NULL DEFAULT '',
    "Amount" numeric NOT NULL DEFAULT 0,
    "Notes" text NOT NULL DEFAULT '',
    "UpdatedUtc" timestamp with time zone NOT NULL DEFAULT now()
);
