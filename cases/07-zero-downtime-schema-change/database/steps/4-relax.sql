-- Step 4, relax. Release 4 stops writing the old column, and the old column is NOT NULL.
-- Without this step the first insert of Release 4 fails.
ALTER TABLE payments ALTER COLUMN amount DROP NOT NULL;
