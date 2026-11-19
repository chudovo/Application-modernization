-- Step 1, expand. A nullable column without a default: PostgreSQL only changes the catalog, no row is rewritten.
-- Release 1 keeps working because it names its columns and never mentions this one.
ALTER TABLE payments ADD COLUMN amount_cents bigint;
