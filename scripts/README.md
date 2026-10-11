# scripts overview

- always run typescript `.ts` files with bun
- always run scripts from the repository root

# script index

- `dotnet-test.ps1` - run dotnet tests.  output sent to .trx file.  hides nearly all output, check status code for success/fail.
  - `-Filter` - passes a `--filter` option to dotnet test
- `test-errors.ts` - checks the results.trx file, on success exit = 0, on error exit = non-zero and test errors are printed.
- `release-metadata.sh` - run with bash; computes stable/beta metadata from the GitHub event,
  branch, and workflow run number, writing outputs to `GITHUB_OUTPUT`.
- `publish-release.sh` - run with bash; publishes the versioned mod ZIP with the GitHub CLI,
  preserving an existing release on reruns.
- `release-retention.py` - reads paginated GitHub releases JSON from stdin; prints stale tags
  for `--channel main --keep 10` or `--channel feature --keep 5`. Does not delete anything itself.
- `check-release-policy.py` - run with `python3` from the repo root; offline release policy
  checks, including a mocked GitHub CLI. Also runs in CI before publication.
