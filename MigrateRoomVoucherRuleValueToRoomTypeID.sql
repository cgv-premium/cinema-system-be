-- Data migration: convert Room voucher RuleValue from RoomID to RoomTypeID
--
-- BACKGROUND:
--   Room vouchers previously stored VoucherRules.RuleValue = RoomID.
--   The rule engine now validates by RoomType, so RuleValue must hold RoomTypeID.
--   Existing Room voucher rows must be rewritten or they will stop matching.
--
-- WHAT THIS DOES:
--   For every VoucherRules row with RuleType = 'Room', it looks up the RoomTypeID
--   of the room whose RoomID equals the current RuleValue, and overwrites RuleValue
--   with that RoomTypeID.
--
-- SAFETY NOTES:
--   * Run this ONCE, together with the code deployment that switches RoomValidator
--     to RoomTypeID. Do NOT run it twice (a second run would try to resolve a
--     RoomTypeID as if it were a RoomID).
--   * Only rows whose RuleValue is a valid integer AND maps to an existing Room are
--     updated. Rows that cannot be resolved are left untouched and reported below,
--     so they can be reviewed manually.
--   * This script is NOT executed automatically. Review, back up, then run manually.
--
-- RECOMMENDED: take a backup of the VoucherRules table first, e.g.
--   SELECT * INTO VoucherRules_Backup_RoomTypeMigration FROM VoucherRules WHERE RuleType = 'Room';

BEGIN TRANSACTION;

-- STEP 1: Report any Room rules that CANNOT be resolved (non-integer value or unknown
-- RoomID). This MUST run before the UPDATE, because afterwards RuleValue holds a
-- RoomTypeID and would no longer join to Rooms.RoomID. These rows are left unchanged
-- and should be reviewed manually.
SELECT vr.RuleID, vr.VoucherID, vr.RuleValue
FROM VoucherRules AS vr
LEFT JOIN Rooms AS r
    ON r.RoomID = TRY_CAST(vr.RuleValue AS INT)
WHERE vr.RuleType = 'Room'
  AND r.RoomID IS NULL;

-- STEP 2: Rewrite RoomID -> RoomTypeID for resolvable Room rules.
UPDATE vr
SET vr.RuleValue = CAST(r.RoomTypeID AS NVARCHAR(100))
FROM VoucherRules AS vr
INNER JOIN Rooms AS r
    ON r.RoomID = TRY_CAST(vr.RuleValue AS INT)
WHERE vr.RuleType = 'Room'
  AND TRY_CAST(vr.RuleValue AS INT) IS NOT NULL;

-- Review the STEP 1 output above, then COMMIT if correct or ROLLBACK to undo.
COMMIT TRANSACTION;
-- ROLLBACK TRANSACTION;
