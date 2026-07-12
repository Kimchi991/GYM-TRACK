# GymTrackPro migration preflight

This release-only maintenance executable performs SELECT-only analysis around
the staged Firebase identity migration. It emits only a declared mode, status,
categories, severities, and counts. It never applies migrations or prints
identities, emails, invite material, connection metadata, row values, or provider
exceptions. Exact email and invite checks stream the necessary values inside the
process and use the same application normalization/validation rules as the API;
only aggregate counts leave the process.

The command requires exactly one explicit phase argument: `--mode pre-stage` or
`--mode post-stage`. It accepts the SQL connection string only through
`GYMTRACKPRO_PREFLIGHT_CONNECTION_STRING`; command-line connection values,
missing modes, duplicate arguments, and unknown switches are rejected. The
shared SQL connection policy requires the SQL Server provider, nonblank server
and database, encrypted transport, and certificate validation. The supplied
string must explicitly contain `Encrypt=True` (or `Encrypt=Mandatory`/`Strict`)
and `TrustServerCertificate=False`; omission is rejected even when a client
library currently has secure defaults. Repeated or conflicting spellings of
either transport key are also rejected. Use a SQL login with SELECT-only
permissions. Enforced `ApplicationIntent=ReadOnly` is advisory and is not a
substitute for a read-only principal.

At process initialization, every immutable hardcoded catalog query also passes a
conservative single-statement `SELECT`/CTE scanner that rejects comments,
`SELECT INTO`, and mutation, DDL, administration, security, or external-source
tokens. This is defense in depth for the compiled fixed-query catalog, not a
general SQL parser and not a substitute for the SELECT-only database principal.

## Release-artifact prerequisite

Build, test, and publish this tool in the controlled CI/release pipeline from the
approved immutable commit. Store its complete clean publish directory as an
immutable release asset and independently retain an approved manifest covering
every published file's relative path, length, and SHA-256 digest, plus the source
commit, release identifier, runtime, and CI run.

Before injecting the connection secret, verify exact file-set equality against
that independently governed manifest and verify the manifest digest. Reject any
missing, additional, duplicate, changed, or symbolic-link/reparse-point entry.
Run the entry point directly from the verified, read-only publish directory. Do
not use `dotnet run`, `dotnet build`, `dotnet restore`, or `dotnet publish` from a
mutable source checkout during the maintenance window. Use the same verified
artifact for both phases.

## Pre-stage mode

Run this before applying migration
`20260711204834_StageFirebaseIdentityAndAccountInvites`:

```powershell
$preflightEntrypoint = '<VERIFIED_PUBLISH_DIRECTORY>\GymTrackPro.Preflight.dll'
$env:GYMTRACKPRO_PREFLIGHT_CONNECTION_STRING = '<INJECTED_READ_ONLY_SQL_CONNECTION>'
dotnet $preflightEntrypoint --mode pre-stage
```

Pre-stage mode requires the exact base-schema fingerprint and exactly these four
`__EFMigrationsHistory` rows, all with EF product version `10.0.9`:

- `20260701191518_InitialCreate`
- `20260701192339_AddUserTokens`
- `20260701193502_MakeAuditLogUserNullable`
- `20260702013356_AddSystemSettings`

Any B1 schema marker, the B1 history row, missing/changed history, or an
unexpected/newer migration blocks this phase. Missing normalized-email and
projection-version values are informational before their staging structures
exist, but invalid/duplicate canonical email groups, unsupported roles, legacy
Gym Goer rows without an approved Member mapping, identity anomalies, and
applicable attendance/data-integrity findings remain blocking.

## Post-stage mode

Run this only after the reviewed B1 migration has been applied and the approved
normalized-email backfill has completed:

```powershell
dotnet $preflightEntrypoint --mode post-stage
```

Post-stage mode requires the same four history rows plus exactly
`20260711204834_StageFirebaseIdentityAndAccountInvites`, again at product version
`10.0.9`, with no unknown or newer rows. It also requires the complete B1
fingerprint. Every B1-sensitive column is matched on exact effective type,
maximum length, precision, scale, nullability, collation, rowversion and default
shape; identity columns additionally require seed `1`, increment `1`, and no
`NOT FOR REPLICATION`. Unexpected computed, persisted, row-GUID, FILESTREAM,
ANSI-padding, sparse, column-set, generated-always, hidden, masking, or Always
Encrypted metadata also fails closed. Named checks, foreign keys and delete
actions, filtered/supporting indexes, AccountInvites shape, and
MemberProjectionVersions key/default/range/FK/rowversion shape must also match.
Every Member must have exactly one valid projection-version row.

Primary keys, foreign keys, and indexes are compared as whole catalog objects,
not as independently matching column rows. The gate verifies exact `dbo`
table/name/type, ordered key or parent-to-principal column pairs, key direction,
cardinality, uniqueness, filter state/definition, update/delete behavior,
enabled/trusted/replication state, hypothetical and duplicate-key settings, and
the absence of unexpected included columns.

The post-stage data gate also blocks missing, invalid, mismatched, or duplicate
application-canonical emails; duplicate UIDs/member links; invalid role/member
relationships; malformed or duplicate invite security state (redemption metadata
must be all null or all populated with a nonzero operation GUID); unresolved open
invites targeting missing/deleted Members; deleted Members with active Gym Goer
users; invalid settings; and blocking attendance/subscription anomalies.

Attendance analysis supports both the legacy shape and a later fully staged
void/supersession shape. A partial void/supersession shape fails closed. Closed
historical attendance for deleted Members is reported as informational evidence;
an open, active row for a deleted Member is blocking.

## Output and decisions

`PREFLIGHT_MODE` confirms the selected phase. `PREFLIGHT_STATUS=PASS` means every
blocking category is zero; it does not mean every informational count is zero.
The main category groups are schema/history fingerprints, exact identity
normalization and role/member integrity, invite integrity, projection-version
coverage, settings, attendance readiness/anomalies, and subscription calendar
values. Preserve the complete count-only output with the artifact/manifest
identity and maintenance ticket. Never edit `__EFMigrationsHistory`, delete data,
or choose a duplicate winner merely to make a category pass.

Exit codes:

- `0`: no blocking findings (informational backfill counts may still be present)
- `1`: execution failed; stderr remains generic
- `2`: command/environment validation failed
- `3`: one or more blocking count categories are non-zero

Run this against a restored staging or sanitized production-like backup first.
The command itself does not prove migration compatibility, locking behavior, or
concurrency on the MonsterASP production tier.

After every success or failure, remove the one-process secret:

```powershell
Remove-Item Env:GYMTRACKPRO_PREFLIGHT_CONNECTION_STRING -ErrorAction SilentlyContinue
```

## Reviewed migration script

The reviewed, idempotent Wave B1 SQL script is intentionally tracked at:

`docs/migrations/20260711204834_StageFirebaseIdentityAndAccountInvites.idempotent.sql`

SHA-256:

`E6067C8E8F29B457EDEC04EEB5102886BC88CF8D8FBE972A805D72A418FCF051`

The checksum covers the tracked copy. The script contains no connection string,
server name, login, password, Firebase identity, or invite material. It has not
been executed against MonsterASP; re-check the checksum and review the target
database's migration history before any separately authorized application.
