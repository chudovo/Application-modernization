-- Step 5, contract. Run only when no instance of Release 3 or older is left: they still write this column.
-- From here on there is no way back to an older release without restoring data.
ALTER TABLE payments DROP COLUMN amount;
