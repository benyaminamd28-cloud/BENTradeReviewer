# Version 2 release readiness

## Status
The current public repository is the earlier baseline. Do not publish v2 from this branch until the newer local BENTradeReviewer project has been reviewed and synced to this repository.

## Required before v2.0.0
- [ ] Sync the current working project source, including the current Trade Analysis UI, from the development machine.
- [ ] Review the full diff against the existing public baseline.
- [ ] Remove local databases, recordings, secrets, credentials, and user-specific data from the release commit and Git history as applicable.
- [ ] Confirm application version metadata and assembly/file versions are consistent with 2.0.0.
- [ ] Verify README and user-facing documentation against the actual v2 feature set.
- [ ] Run restore, Release x64 build, and automated tests on Windows.
- [ ] Run a manual smoke test of the packaged Windows x64 application.
- [ ] Confirm the generated ZIP contains the runnable app and required runtime dependencies.
- [ ] Create annotated tag `v2.0.0` only after all checks pass.
- [ ] Publish the GitHub Release with release notes and the verified Windows x64 ZIP.

## Automation added on the release-preparation branch
- Windows CI workflow for restore, Release x64 build, tests, and a downloadable build artifact.
- Tag-triggered release workflow that builds, tests, packages, and attaches a ZIP to a GitHub Release.
- A PowerShell build script that checks native `dotnet` exit codes and stops on failure.
- Ignore rules for build output, local database files, logs, backups, and artifacts.

## Important
The workflow has not been validated against the newer local project. Treat a green run on the older baseline as an infrastructure check only, not as proof that v2 is ready.
