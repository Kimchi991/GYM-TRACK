# B1 identity migration operations

This directory contains the reviewed, idempotent SQL artifact for migration
`20260711204834_StageFirebaseIdentityAndAccountInvites`. The script is a
forward-only deployment artifact. It has not been executed against MonsterASP.

Reviewed tracked-script SHA-256:

`E6067C8E8F29B457EDEC04EEB5102886BC88CF8D8FBE972A805D72A418FCF051`

## Migration-history safety

Never delete, rename, or manually edit a row in `__EFMigrationsHistory` to force
this script to run. Before applying it, run the migration preflight in explicit
pre-stage mode and inspect the reported migration-history and schema-fingerprint
categories.

If migration ID `20260711204834_StageFirebaseIdentityAndAccountInvites` is
already present, this script deliberately skips its B1 statements. Post-stage
preflight must then prove the complete expected fingerprint, including
`AccountInvites.TokenHash` as `binary(32)` and the exact trusted
`CK_AccountInvites_RedemptionMetadata` constraint. That constraint permits only
three null redemption fields or three populated fields with a nonzero
`RedemptionOperationId`. If the ID is present with the earlier `char(44)` shape,
the legacy `CK_AccountInvites_UsedMetadataComplete` constraint, or any definition
that permits `Guid.Empty`, stop the rollout. Inspect that environment and its
data, then author and review a new compensating migration with a new migration
ID. Do not rewrite the applied migration or invent a target state from source
assumptions.
Constraint and index fingerprints are exact whole-object comparisons: ordered
keys or FK column pairs, direction, cardinality, type, filters, includes,
referential actions, trust/enablement, uniqueness, and integrity-relevant flags
must all match the reviewed B1 shape.
Every B1-sensitive column is likewise matched on exact type, length, precision,
scale, nullability, effective collation, default and rowversion state, identity
seed/increment/replication state, and the absence of unexpected computed,
storage, sparse, temporal/ledger, hidden, masking, or Always Encrypted metadata.

## Rollout sequence

1. Create and verify a restorable backup, enter the approved write-freeze window,
   and use a SQL principal limited to the authorized operation.
2. Run preflight in explicit pre-stage mode. Resolve duplicate canonical emails,
   unsupported roles, role/member-link anomalies, and other blocking findings.
   A legacy `GymGoer` row cannot be assigned a Member automatically; it requires
   environment-specific inspection and an approved mapping or role correction.
3. Verify that the B1 migration ID is absent. If it is present, follow the
   fingerprint rule above instead of applying this script.
4. Verify this tracked script's SHA-256, review it against the approved migration,
   and apply it once during the write freeze. Do not auto-migrate on API startup.
5. Run the checksummed `GymTrackPro.NormalizedEmailBackfill` release artifact
   with no flags for its default dry run. Supply its connection only through
   `GYMTRACKPRO_NORMALIZED_EMAIL_BACKFILL_CONNECTION_STRING`; see
   `tools/GymTrackPro.NormalizedEmailBackfill/README.md`. It calls the API's exact
   FormKC plus invariant-uppercase implementation, reports only bounded
   categories/counts, and stops on invalid sources, mismatches, or canonical
   conflicts.
6. After conflicts are resolved through an approved data decision, run the same
   immutable artifact with `--confirm <EXACT_DRY_RUN_FINGERPRINT>`. Missing,
   malformed, changed, or stale snapshots stop before the first write. It commits
   snapshot-bound serializable batches, changes null normalized values only, and
   is restart-safe. After any interruption, obtain a new dry-run fingerprint;
   rerun dry-run until no work remains, then remove its one-process connection
   environment value.
7. Run preflight in explicit post-stage mode. Every B1 schema fingerprint,
   normalized email, role/member link, invite invariant, migration-history row,
   and member projection-version row must pass before application rollout.
8. Execute the separately reviewed owner-binding procedure and deploy only the
   compatible hardened application artifact. B2 attendance schema and complete
   projection-version mutation wiring remain separate release gates.

`Users.NormalizedEmail` remains nullable in the B1 DDL only to permit this staged,
restart-safe rollout and compatible artifact rollback. Post-stage preflight treats
every missing value as blocking. A later separately rehearsed migration may make
the column non-null only after all target environments prove the backfill.

## Forward-only rollback policy

Do not run the migration's destructive `Down` path in production. Roll application
code back only to the named hardened compatibility artifact that tolerates the
additive B1 schema. Keep invite history, projection-version rows, nullable legacy
credential state, and identity bindings intact while a forward fix is prepared.

`StaleSessionHours` is inserted only when missing and is intentionally retained by
the reverse path. The migration cannot distinguish its seed from an operator-owned
pre-existing value, and deleting it could silently change attendance behavior.
Compatible rollback artifacts ignore or safely read the retained setting.

If a migration caused confirmed data corruption, stop writes and follow the
approved backup-restore procedure; do not use an unreviewed down migration as a
data-repair mechanism.

## Capstone-local sequence

For the capstone demo database, apply migrations in source order:

1. `20260711204834_StageFirebaseIdentityAndAccountInvites`
2. `20260712050837_AddAttendanceVoidingAndSource`

The second migration is additive and staged. It renames the original
`AttendanceLogs.AttendanceDate` datetime2 column to
`AttendanceDateLegacyDateTime`, keeps it nullable for historical inspection,
adds the new SQL `date` column, and backfills it with a date-only conversion.
No timezone shift is performed because the legacy value represented a calendar
date and was not proven to be a UTC instant. New attendance rows use the SQL
`date` column; the preserved legacy column is intentionally left null for them.

The attendance stage also changes the Member foreign key to `NO ACTION` so a
Member deletion cannot cascade through attendance or its operation/adjustment
audit history. Filtered unique indexes continue to enforce one active daily
visit and one active open session, while nonunique date/check-in indexes support
capstone reports. Checkout ordering applies to active rows only: retained voided
history may contain a legacy checkout at or before check-in, but post-attendance
preflight still blocks every non-void row whose checkout is not strictly after
check-in. Operation and adjustment checks constrain the modeled enum ordinals,
HTTP result metadata, completion ordering, and nonblank ledger text.

### Current capstone verification evidence

- Server strict build: 0 errors and 0 warnings.
- Full server suite: 652 passed out of 652.
- EF listed migrations through
  `20260712050837_AddAttendanceVoidingAndSource`.
- `dotnet ef migrations has-pending-model-changes` reported no pending changes.
- A disposable LocalDB execution rehearsal is environment-deferred because the
  available LocalDB environment could not create the isolated verification
  database. No MonsterASP execution was performed.

The final attendance-inclusive idempotent SQL export and its SHA-256 remain
pending. Generate them fresh from the final source after all migration edits are
complete. Do not reuse an earlier temporary export hash. The reviewed tracked
B1-only hash near the top of this document proves only the B1 script and must not
be presented as the hash of the combined B1-plus-attendance migration script.

Before the MonsterASP rehearsal, rotate any database credential that has appeared
in source, chat, history, screenshots, or shared evidence; store the replacement
only in an untracked secret source. Confirm encrypted SQL transport/TLS with the
provider. MonsterASP execution, post-migration smoke testing, and final handoff to
`feature/firebase-auth` remain pending operator gates.

This local capstone sequence does not require the production catalog-fingerprint,
write-freeze, immutable-artifact, or MonsterASP rehearsal steps above. Those are
future deployment tasks. It still requires a disposable/local database backup
before testing the migration, and the B1 identity migration must remain before
the attendance migration.
