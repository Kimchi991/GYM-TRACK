# GymTrackPro normalized-email backfill

This release-only maintenance executable fills `Users.NormalizedEmail` with the
same `EmailNormalization.TryCanonicalize` implementation used by the API. That
implementation trims the source, applies Unicode FormKC compatibility
normalization, and performs invariant-culture uppercase normalization. The tool
does not reimplement that algorithm in SQL.

It is dry-run by default. It accepts the database connection only from
`GYMTRACKPRO_NORMALIZED_EMAIL_BACKFILL_CONNECTION_STRING`; command-line
connection values and unknown switches are rejected. The shared connection
policy requires the SQL Server provider, nonblank source and catalog, encrypted
transport, and certificate validation. The supplied string must explicitly
contain `Encrypt=True` (or `Encrypt=Mandatory`/`Strict`) and
`TrustServerCertificate=False`; omitted keys and permissive aliases such as
`Yes`/`No`, repeated keys, and conflicting spaced spellings are rejected rather
than inheriting driver defaults or last-value-wins behavior. A provider endpoint
that cannot satisfy this policy blocks the rollout.

## Release and maintenance prerequisites

- Complete explicit pre-stage preflight, verify a restorable backup, and enter
  the approved application/database write freeze.
- Apply and verify the reviewed B1 migration. The tool expects the nullable
  `Users.NormalizedEmail` column and its filtered binary-collation unique index.
- Run only a CI-built, tested, checksummed release artifact. Do not build or run
  from a mutable source checkout during the maintenance window.
- Use a read-only SQL principal for the dry run. Use a narrowly scoped write
  principal for confirmation and remove both connection-string environment
  values immediately afterward.

## Dry run

Inject the connection string through the approved one-process secret channel,
then invoke the verified artifact with no confirmation flag. A batch size is
optional and must be between 1 and 500; the default is 200.

```powershell
$env:GYMTRACKPRO_NORMALIZED_EMAIL_BACKFILL_CONNECTION_STRING = '<INJECTED_SQL_CONNECTION>'
dotnet '<VERIFIED_PUBLISH_DIRECTORY>\GymTrackPro.NormalizedEmailBackfill.dll'
```

Optional bounded batch override:

```powershell
dotnet '<VERIFIED_PUBLISH_DIRECTORY>\GymTrackPro.NormalizedEmailBackfill.dll' --batch-size 200
```

Output contains only mode, bounded categories, counts, completion state, and a
secret-free candidate fingerprint. It never prints an email, user ID, connection
value, server/catalog, or exception text. `INVALID_SOURCE`,
`EXISTING_MISMATCH`, or `COLLISION_GROUPS` greater than zero is a stop condition.
Resolve the underlying rows through a separately approved, audited data decision;
the tool deliberately does not pick a winner or overwrite an existing mismatched
normalized value.

The `FINGERPRINT` is an uppercase SHA-256 digest using algorithm domain
`GTP-NORMALIZED-EMAIL-SNAPSHOT-V1`. Rows are keyset-read and hashed in ascending
`UserID` order. Each row state is length-prefixed UTF-8 over the numeric ID, raw
email state, existing normalized state, and the result of the shared application
normalizer, with explicit null markers; the outer digest hashes those row digests
in the same order. It is deterministic across batch sizes and process cultures
and does not contain a reversible serialization of the row values. Record the
exact 64-character fingerprint with the count-only dry-run evidence.

## Confirmation and restart behavior

After a clean dry run, inject the approved write connection and pass the exact
fingerprint from that dry run as the value of `--confirm` to the same verified
artifact. A bare, malformed, or stale confirmation value is rejected before the
first write:

```powershell
dotnet '<VERIFIED_PUBLISH_DIRECTORY>\GymTrackPro.NormalizedEmailBackfill.dll' --confirm <EXACT_DRY_RUN_FINGERPRINT> --batch-size 200
```

Confirmation recomputes the complete fingerprint under the continuing write
freeze before the first mutation. Each batch is keyset-ordered by `UserID`, binds
every expected row to its pre-state and exact post-state digest, locks its
rows/range with
`UPDLOCK, HOLDLOCK` in a serializable transaction, re-runs the shared application
normalizer, and changes only a null `NormalizedEmail`. A batch commits atomically.
Already-canonical rows are skipped, so an interrupted or unknown-commit run is
safe to restart with a new dry run and its new fingerprint, then confirmation.
Execution-strategy retries discard tracked state between attempts, and a full
final scan must exactly match the expected post-state fingerprint and show no
pending or blocking rows before `COMPLETED=True`. Confirmation output withholds
the fingerprint; always obtain a confirmation value from a separate clean dry
run.

The bounded transactions intentionally allow committed progress before a later
batch fails. Do not manually reverse those rows. Preserve the write freeze,
investigate the count-only failure category, and rerun the same immutable
artifact. The filtered unique index catches a conflicting committed normalized
value; per-batch revalidation and the final scan catch invalid, mismatched, or
new null rows. A concurrent-write finding blocks rollout even when earlier
batches committed.

## Graceful cancellation

The first `Ctrl+C` is suppressed, cancels the active read or batch, and lets
normal unwinding attempt rollback of the current uncommitted transaction,
database-context disposal, and removal of the process-wide console handler. It
emits only the generic canceled message and exits with code `130`. Batches
committed before the signal remain valid restart-safe progress; inspect state and
obtain a new dry-run fingerprint before resuming confirmation.

A second `Ctrl+C` is not suppressed and may force immediate process termination,
so graceful cleanup is no longer guaranteed. Use it only when the first signal
cannot complete within the operational timeout, then inspect database state and
restart from a new dry run rather than assuming the active batch committed.

## Exit codes and cleanup

- `0`: dry-run analysis completed without blocking findings, or confirmation
  completed with a clean final scan
- `1`: execution/provider failure; stderr is generic
- `2`: argument, environment, endpoint, or transport policy rejection
- `3`: invalid source, existing mismatch, canonical collision, concurrent
  snapshot mismatch, concurrent conflict, or incomplete confirmed final state
- `130`: canceled by the first `Ctrl+C` after cancellation unwind

After every success or failure:

```powershell
Remove-Item Env:GYMTRACKPRO_NORMALIZED_EMAIL_BACKFILL_CONNECTION_STRING -ErrorAction SilentlyContinue
```

Record only the release/checksum, maintenance ticket, backup evidence, batch
size, timestamps, exit code, and count-only output. Then run explicit post-stage
preflight. Do not proceed to owner binding or application rollout until it passes.
