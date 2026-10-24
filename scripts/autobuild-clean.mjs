// Usage: imported by autobuild.mjs - the cleaning of old generated files at the start of a real run; runs nothing on import
import { runClean } from "./clean.mjs";

const megabytes = (bytes) => (bytes / (1024 * 1024)).toFixed(1);

export function cleanAtStart({ flags, dryRun, say, clean = runClean }) {
  if (dryRun || flags["no-clean"]) return;
  try {
    for (const row of clean({ apply: true }).rows) {
      say(`czyszczenie: ${row.category} ${row.files} plików, ${megabytes(row.bytes)} MB${row.skipped ? `, pominięto ${row.skipped}` : ""}`);
    }
  } catch (err) {
    say(`czyszczenie: błąd - ${String(err?.message ?? err).split(/\r?\n/)[0].slice(0, 160)}`);
  }
}
