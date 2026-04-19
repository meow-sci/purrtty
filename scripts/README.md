# scripts overview

- always run typescript `.ts` files with bun
- always run scripts from `catty-ksa/` dir

# script index

- `dotnet-test.ps1` - run dotnet tests.  output sent to .trx file.  hides nearly all output, check status code for success/fail.
  - `-Filter` - passes a `--filter` option to dotnet test
- `test-errors.ts` - checks the results.trx file, on success exit = 0, on error exit = non-zero and test errors are printed.
