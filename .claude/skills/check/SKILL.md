---
name: check
description: Run the repository verification script and summarise the result in at most five lines. Fixes nothing.
argument-hint: "[--quick|--all|--projects a,b]"
model: haiku
effort: low
allowed-tools: Bash, Read
---

Run `node scripts/verify.mjs $ARGUMENTS` from the repository root and report what it said.

With no arguments the script auto-detects the changed areas from git. `--quick` is format + build only;
`--all` is everything; `--projects a,b` limits it to the named projects. Use a generous timeout - test
runs start Docker containers.

## Report (at most 5 lines)

1. `ok` or `failed`, and which command was run.
2. Which steps failed (`format`, `build`, `test`, `web-typecheck`, `web-lint`, `web-build`,
   `web-test`, `api`) - from the `VERIFY_RESULT:` line, not from your reading of the log.
3. The first few failing items, one per line: test name, compiler error, or lint rule, with the file.

No `VERIFY_RESULT:` line at all → say the script did not finish and quote its last few lines.

Do not fix anything, do not edit a file, do not re-run to see whether it passes this time, and do not
run `dotnet` or `npm` directly - `scripts/verify.mjs` is the single definition of green.
