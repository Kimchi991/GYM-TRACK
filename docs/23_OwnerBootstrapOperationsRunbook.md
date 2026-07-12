# Initial Owner Firebase Bootstrap Runbook

## Purpose and authority

This runbook binds the one independently verified SQL `Administrator` record to one
Firebase identity. It is a maintenance-only operation, not a web endpoint or routine user
management command. A project owner and database operator must both be present.

Never place the Firebase ID token, database connection string, UID, or email in command-line
arguments, tickets, chat, screenshots, shell history, or logs. The examples below contain
placeholders only. Inject secret environment values through the approved host secret channel.

## Pre-maintenance release artifact

The bootstrap must be built, tested, and published by the CI/release pipeline before the
maintenance window. The maintenance operator must never execute from a source checkout and
must not run `dotnet run`, `dotnet build`, `dotnet restore`, or `dotnet publish` during the
window. The `dotnet` host may be used only to launch an already-published DLL.

The release pipeline must:

1. Check out the approved commit by immutable commit SHA, restore from approved feeds, run the
   release test gates, and publish `tools/GymTrackPro.Bootstrap` into a new staging directory.
   Publishing is a CI/release operation, not a maintenance-shell operation.
2. Produce a clean publish directory containing no source, `obj`, package cache, or prior
   release output. Retain the exact published entry point (`GymTrackPro.Bootstrap.dll` or the
   approved platform executable), `.deps.json`, `.runtimeconfig.json`, managed and native
   dependencies, configuration/content files, and every other file emitted by publish.
3. Generate a deterministic SHA-256 manifest outside the publish directory. It must contain
   one entry for every regular file beneath the publish root, using a normalized relative path,
   byte length, and SHA-256 digest. The manifest must therefore cover the entry point,
   `.deps.json`, `.runtimeconfig.json`, and the complete dependency set. Empty, duplicate, or
   absolute paths are forbidden.
4. Store the artifact as an immutable release asset. Independently store the approved manifest,
   the manifest's own SHA-256 digest, release/version identifier, source commit SHA, CI run ID,
   target runtime, and approver identities in the release-control system. A manifest shipped
   only beside the artifact is not independent evidence.

For example, `dotnet publish` may appear in the controlled CI job, but never in the maintenance
instructions:

```powershell
dotnet publish tools/GymTrackPro.Bootstrap/GymTrackPro.Bootstrap.csproj `
  --configuration Release --runtime <APPROVED_RUNTIME> `
  --output <NEW_CI_PUBLISH_DIRECTORY>
```

Before injecting secrets, two operators must retrieve the immutable release asset and the
approved manifest through their independently governed channels. Recompute the manifest over
the extracted publish directory and require exact set equality: no missing files, no additional
files, no duplicate relative paths, and no length or SHA-256 mismatch. Independently verify the
approved manifest's recorded SHA-256 digest as well. Reject reparse points/symbolic links and
reject an entry point outside the verified publish root.

Any manifest, version, commit, runtime, or entry-point mismatch is a fail-closed stop condition.
Do not regenerate or approve a manifest in the maintenance shell, delete an unexpected file,
substitute a dependency, or continue because only the main DLL matched. Keep writes blocked,
discard the suspect extraction, and obtain the approved release asset again.

## Preconditions

1. Schedule a write-free maintenance window and block API/mobile writes.
2. Confirm the retained hardened API artifact and rollback procedure.
3. Verify the complete Bootstrap publish set against the independently recorded approved
   SHA-256 manifest as described above. Record the approved version, commit SHA, manifest
   digest, entry-point relative path/digest, target runtime, verifier identities, and timestamp.
4. Make the verified publish directory read-only for the maintenance account and retain the
   same directory and entry point for both dry-run and confirmation.
5. Take a database backup and verify that it can be listed/read by the restore operator.
6. Identify exactly one active SQL user whose role is `Administrator`, has no `MemberID`, has
   no Firebase UID, and whose email was independently verified with the project owner.
7. Confirm the exact production Firebase project ID from the Firebase console. Do not use a
   display name, project number, development project, or copied client API key.
8. Ensure no owner is already Firebase-bound. A second owner bootstrap is forbidden.
9. Record the maintenance ticket and intended SQL `UserID`; do not record identity secrets.

## Obtain one fresh identity proof

1. Sign out of Firebase on the controlled operator device.
2. Sign in again as the intended owner and complete email verification.
3. Force-refresh the Firebase ID token immediately before each dry-run or confirmation.
4. The token must have been issued within five minutes and its `auth_time` must show a sign-in
   within ten minutes. If either window expires, sign in and force-refresh again.
5. Inject that single ID token through `GYMTRACKPRO_BOOTSTRAP_FIREBASE_ID_TOKEN`. The tool
   validates OIDC metadata/signing keys, RS256 plus `kid`, exact issuer and single audience,
   signature, lifetime, `sub`/optional `user_id`, recent `iat`/`auth_time`, and verified email.
   UID and normalized email are derived from this token; operators never provide them
   independently.

## Required environment configuration

Configure the following in the maintenance process only:

```text
DOTNET_ENVIRONMENT=Production
OwnerBootstrap__Enabled=true
OwnerBootstrap__AllowedEnvironment=Production
FirebaseAuthentication__ProjectId=<EXACT_FIREBASE_PROJECT_ID>
GYMTRACKPRO_BOOTSTRAP_FIREBASE_ID_TOKEN=<INJECTED_ONE_TIME_ID_TOKEN>
ConnectionStrings__DefaultConnection=<INJECTED_PRODUCTION_CONNECTION_STRING>
```

The bootstrap uses the same fail-closed SQL Server connection policy as the
migration tools. The injected value must name a nonblank server and catalog, use
mandatory/strict encryption, and validate the server certificate. It must
explicitly include `Encrypt=True` (or `Encrypt=Mandatory`/`Strict`) and
`TrustServerCertificate=False`; omitting either key is rejected even if current
driver defaults appear secure. Repeating either key, including a second spaced
alias, is also rejected instead of accepting last-value-wins parsing. If the
MonsterASP endpoint cannot satisfy that policy, stop and obtain a
provider-approved trusted endpoint; do not weaken TLS.

`ASPNETCORE_ENVIRONMENT`, if present, must exactly equal `Production`; remove conflicting
values. Keep the token and database secret out of `.env` files and repository configuration.

## Dry run

Select the exact entry point named in the approved manifest and release record. Do not copy it
out of the verified directory. For a framework-dependent publish, invoke the verified DLL with
the runtime host:

```powershell
$bootstrapRoot = (Resolve-Path -LiteralPath '<VERIFIED_READ_ONLY_PUBLISH_DIRECTORY>').Path
$bootstrapEntrypoint = Join-Path $bootstrapRoot 'GymTrackPro.Bootstrap.dll'
dotnet $bootstrapEntrypoint `
  --user-id <VERIFIED_SQL_USER_ID> --environment Production --dry-run
```

If the approved release instead names a platform executable, invoke that exact verified file:

```powershell
$bootstrapRoot = (Resolve-Path -LiteralPath '<VERIFIED_READ_ONLY_PUBLISH_DIRECTORY>').Path
$bootstrapEntrypoint = Join-Path $bootstrapRoot 'GymTrackPro.Bootstrap.exe'
& $bootstrapEntrypoint `
  --user-id <VERIFIED_SQL_USER_ID> --environment Production --dry-run
```

Use exactly one of these forms as dictated by the release record. Immediately before execution,
recheck the entry-point SHA-256 digest against the approved manifest. Do not use a PATH-resolved
copy, a source-project command, or an entry point from a different extraction.

Expected result:

```text
UserID=<ID>; Role=Administrator; WouldApply=True; Applied=False
```

Any rejection is a stop condition. Do not change SQL rows, relax validation, substitute an
email match, or enable a web bootstrap endpoint. Resolve the discrepancy and repeat the
preconditions with a newly refreshed token.

## Confirm once

Obtain and inject a newly force-refreshed token. Recompute and verify the complete publish-set
manifest again, then reuse the same `$bootstrapEntrypoint` and verified directory used for the
dry run. For the approved DLL form, run:

```powershell
dotnet $bootstrapEntrypoint `
  --user-id <VERIFIED_SQL_USER_ID> --environment Production --confirm
```

For the approved executable form, run:

```powershell
& $bootstrapEntrypoint `
  --user-id <VERIFIED_SQL_USER_ID> --environment Production --confirm
```

Do not switch entry-point forms between dry-run and confirmation. A changed directory, file set,
version, manifest digest, or entry-point digest invalidates the dry run and stops confirmation.

Expected result:

```text
UserID=<ID>; Role=Administrator; WouldApply=True; Applied=True
```

Do not invoke confirmation a second time. If the terminal disconnects or output is lost,
treat the outcome as unknown and follow the recovery section before taking another action.

## Evidence to retain

Record only non-secret operational evidence in the maintenance ticket or release-control system:

- approved release/version, source commit SHA, CI run ID, target runtime, and artifact location;
- approved manifest SHA-256, verified entry-point relative path and SHA-256, full-set verification
  result, verifier identities, and verification timestamps before dry-run and confirmation;
- database-backup identifier and restore-verification evidence;
- dry-run and confirmation timestamps, result categories, target SQL `UserID`, and bootstrap
  correlation handle, without token, UID, email, or connection-string values; and
- the resulting `InitialOwnerFirebaseBound` audit row identifier, action, timestamp, target user,
  and correlation match plus the identities of the two operators who verified it.

Preserve the independently approved manifest and release record with the ticket. Do not attach
secret-bearing terminal output or create a new post-execution manifest as substitute evidence.

## Verification before reopening writes

1. Read the target `Users` row through the approved administration channel and verify:
   active Administrator, no `MemberID`, `EmailVerified = true`, non-null Firebase UID, and the
   expected normalized email. Do not copy the UID into the ticket.
2. Verify exactly one `AuditLogs` row for action `InitialOwnerFirebaseBound` and the target
   user. Confirm its timestamp falls inside the maintenance window and its details contain
   only the bootstrap correlation handle.
3. Confirm exactly one Administrator is Firebase-bound.
4. Start the hardened API with production configuration and use a newly issued owner token
   for the owner-policy smoke test. Do not log or capture the bearer token.
5. Reopen writes only after the database and policy checks succeed.

## Failure and recovery

- **Artifact or manifest verification fails:** do not execute either mode. Keep maintenance mode
  enabled, preserve the mismatch evidence, discard the extraction, and obtain the independently
  approved release again. Never edit the manifest or artifact locally to make comparison pass.
- **Token/OIDC validation fails:** no database operation should begin. Check project ID,
  outbound OIDC availability, email verification, clock accuracy, and freshness; then obtain
  a new token. Never disable validation.
- **Dry run rejects:** no mutation is expected. Keep maintenance mode enabled and reconcile
  the SQL target, normalized-email backfill, existing UID links, and existing owner state.
- **Confirmation reports failure before commit:** keep maintenance mode enabled, inspect the
  target and audit row read-only, and retry only after proving no commit occurred.
- **Confirmation response is lost or commit status is ambiguous:** do not blindly rerun. Check
  the exact target binding and `InitialOwnerFirebaseBound` audit evidence. Escalate to the
  technical lead if they disagree.
- **Wrong binding or confirmed corruption:** stop all writes, preserve logs/audit evidence,
  and restore the verified pre-operation backup under the incident procedure. Do not edit the
  UID directly as an improvised rollback.
- **Smoke test fails after a verified binding:** keep maintenance mode enabled and roll back
  only to the retained UID-policy-compatible artifact; do not deploy a build that weakens
  issuer, audience, active-user, or no-auto-provision enforcement.

## Mandatory secret cleanup

Immediately after verification—or after any failure—remove the bootstrap process copies of
all secrets and remove the bootstrap enablement/token values from the hosting control panel:

```powershell
Remove-Item Env:GYMTRACKPRO_BOOTSTRAP_FIREBASE_ID_TOKEN -ErrorAction SilentlyContinue
Remove-Item Env:ConnectionStrings__DefaultConnection -ErrorAction SilentlyContinue
Remove-Item Env:OwnerBootstrap__Enabled -ErrorAction SilentlyContinue
Remove-Item Env:OwnerBootstrap__AllowedEnvironment -ErrorAction SilentlyContinue
```

The normal API may still require its independently governed production connection-string
setting; do not delete that standing API setting when cleaning only the bootstrap process.
Close the maintenance shell, clear any approved temporary secret object at its source, and
confirm the bootstrap tool is not installed as a standing web service or scheduled job. Keep
only the non-secret maintenance ticket, timestamps, result category, and audit verification.
