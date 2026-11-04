-- ==============================================================================
-- PocketGoal - Pre-Login Test Rows Cleanup Script
-- ==============================================================================
-- This script safely cleans up pre-login test rows created before Supabase Auth existed.
-- Run this in the Supabase Dashboard SQL Editor ONLY after confirming you want to remove test data.
--
-- FOREIGN KEY CASCADE VERIFICATION:
-- The following constraints exist in the database with ON DELETE CASCADE:
--   - "FK_SavingGoals_UserProfiles_UserId" (SavingGoals -> UserProfiles)
--   - "FK_Savings_SavingGoals_SavingGoalId" (Savings -> SavingGoals)
--   - "FK_Expenses_UserProfiles_UserId" (Expenses -> UserProfiles)
--   - "FK_ExpenseCategories_UserProfiles_UserId" (ExpenseCategories -> UserProfiles)
-- Deleting orphan UserProfiles will automatically delete their goals, savings, and expenses.
-- ==============================================================================

-- Step 1: Preview rows that have no matching Supabase auth.users account
SELECT "Id", "Name", "Email", "CreatedAt" 
FROM "UserProfiles" 
WHERE "Id" NOT IN (SELECT id FROM auth.users);

-- Step 2: Delete orphaned pre-login UserProfiles (cascades to SavingGoals, Savings, Expenses)
DELETE FROM "UserProfiles" 
WHERE "Id" NOT IN (SELECT id FROM auth.users);

-- Step 3: Delete any orphaned custom expense categories
DELETE FROM "ExpenseCategories" 
WHERE "UserId" IS NOT NULL 
  AND "UserId" NOT IN (SELECT id FROM auth.users);
