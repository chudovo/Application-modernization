-- Step 3, constrain. Run only when the backfill is complete and no Release 1 instance is left.
-- SET NOT NULL on its own scans the whole table while holding an exclusive lock. A CHECK constraint added as
-- NOT VALID takes the lock for an instant; VALIDATE scans without blocking reads and writes; and with the valid
-- constraint in place PostgreSQL (12 and later) accepts SET NOT NULL without another scan.
ALTER TABLE payments ADD CONSTRAINT payments_amount_cents_present CHECK (amount_cents IS NOT NULL) NOT VALID;
ALTER TABLE payments VALIDATE CONSTRAINT payments_amount_cents_present;
ALTER TABLE payments ALTER COLUMN amount_cents SET NOT NULL;
ALTER TABLE payments DROP CONSTRAINT payments_amount_cents_present;
