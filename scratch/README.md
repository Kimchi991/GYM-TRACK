# Scratch scripts

The tracked PowerShell scripts in this directory are legacy local-integration fixtures from the pre-Firebase authentication flow. They call endpoints such as `/auth/login` that are no longer part of the current API and must not be used against MonsterASP or any shared database.

Fixture passwords such as `SecurePassword@123` and `YourStrongPass@123` are local test values, not application credentials. New scripts must read real connection strings and tokens from environment variables or local secret configuration; never place cloud credentials in this directory.

The current Firebase/JWT and invite flows are covered by the maintained tests under `src/GymTrackPro.Tests/AuthSecurity` and `src/GymTrackPro.Mobile.Tests`.
