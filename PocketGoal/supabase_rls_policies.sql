-- ==============================================================================
-- PocketGoal - Supabase Row Level Security (RLS) & Default auth.uid() Setup
-- ==============================================================================
-- IMPORTANT RUN ORDER:
-- 1. Step 1: Run pre-login cleanup SQL in Supabase SQL editor (if needed)
-- 2. Step 2: Apply EF Core migration (AddUserIdToSavings) so Savings has UserId
-- 3. Step 3: Run this script in the Supabase dashboard SQL editor
-- ==============================================================================

-- 1. Set default auth.uid() on all user tables
ALTER TABLE "SavingGoals" ALTER COLUMN "UserId" SET DEFAULT auth.uid();
ALTER TABLE "Savings" ALTER COLUMN "UserId" SET DEFAULT auth.uid();
ALTER TABLE "Expenses" ALTER COLUMN "UserId" SET DEFAULT auth.uid();
ALTER TABLE "ExpenseCategories" ALTER COLUMN "UserId" SET DEFAULT auth.uid();

-- 2. Enable Row Level Security (RLS) on all application tables
ALTER TABLE "UserProfiles" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "SavingGoals" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "Savings" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "ExpenseCategories" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "Expenses" ENABLE ROW LEVEL SECURITY;

-- 3. UserProfiles Policies
DROP POLICY IF EXISTS "Users can view own profile" ON "UserProfiles";
CREATE POLICY "Users can view own profile" 
  ON "UserProfiles" FOR SELECT 
  USING (auth.uid() = "Id");

DROP POLICY IF EXISTS "Users can update own profile" ON "UserProfiles";
CREATE POLICY "Users can update own profile" 
  ON "UserProfiles" FOR UPDATE 
  USING (auth.uid() = "Id");

DROP POLICY IF EXISTS "Users can insert own profile" ON "UserProfiles";
CREATE POLICY "Users can insert own profile" 
  ON "UserProfiles" FOR INSERT 
  WITH CHECK (auth.uid() = "Id");

-- 4. SavingGoals Policies
DROP POLICY IF EXISTS "Users can view own goals" ON "SavingGoals";
CREATE POLICY "Users can view own goals" 
  ON "SavingGoals" FOR SELECT 
  USING (auth.uid() = "UserId");

DROP POLICY IF EXISTS "Users can manage own goals" ON "SavingGoals";
CREATE POLICY "Users can manage own goals" 
  ON "SavingGoals" FOR ALL 
  USING (auth.uid() = "UserId")
  WITH CHECK (auth.uid() = "UserId");

-- 5. Savings Policies
DROP POLICY IF EXISTS "Users can view own savings" ON "Savings";
CREATE POLICY "Users can view own savings" 
  ON "Savings" FOR SELECT 
  USING (auth.uid() = "UserId");

DROP POLICY IF EXISTS "Users can manage own savings" ON "Savings";
CREATE POLICY "Users can manage own savings" 
  ON "Savings" FOR ALL 
  USING (auth.uid() = "UserId")
  WITH CHECK (auth.uid() = "UserId");

-- 6. ExpenseCategories Policies
-- Allows viewing system default categories (where UserId is NULL) and custom categories (where UserId matches auth.uid())
DROP POLICY IF EXISTS "Users can view system and own categories" ON "ExpenseCategories";
CREATE POLICY "Users can view system and own categories" 
  ON "ExpenseCategories" FOR SELECT 
  USING ("UserId" IS NULL OR auth.uid() = "UserId");

DROP POLICY IF EXISTS "Users can manage own categories" ON "ExpenseCategories";
CREATE POLICY "Users can manage own categories" 
  ON "ExpenseCategories" FOR ALL 
  USING (auth.uid() = "UserId")
  WITH CHECK (auth.uid() = "UserId");

-- 7. Expenses Policies
DROP POLICY IF EXISTS "Users can view own expenses" ON "Expenses";
CREATE POLICY "Users can view own expenses" 
  ON "Expenses" FOR SELECT 
  USING (auth.uid() = "UserId");

DROP POLICY IF EXISTS "Users can manage own expenses" ON "Expenses";
CREATE POLICY "Users can manage own expenses" 
  ON "Expenses" FOR ALL 
  USING (auth.uid() = "UserId")
  WITH CHECK (auth.uid() = "UserId");
