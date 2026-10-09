#!/usr/bin/env node
// Usage: node scripts/compose.mjs <build|up|down|reset|status> [--ref <ref>]
//   build [--ref <ref>]  build the six skarbiec-local images from a git archive of <ref> (default HEAD); never touches running containers
//   up                   docker compose up -d --wait on the existing images (no build)
//   down                 stop and remove the containers, keep the volumes
//   reset                down plus remove the stack's volumes
//   status               container states, the sha each runs, the sha of the newest built image
//   build prints COMPOSE_RESULT: {...} (ok, revision, error) as its last stdout line; exit 0 ok, 2 failed

import { spawnSync } from "node:child_process";
import { mkdirSync, mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const COMPOSE_FILE = path.join(REPO_ROOT, "deploy", "compose", "compose.yaml");
const PROJECT = "skarbiec-local";
const STACK_LABEL = "skarbiec.stack=local";
const REVISION_LABEL = "org.opencontainers.image.revision";

const IMAGES = [
  { name: "identity", dockerfile: "services/Identity/Skarbiec.Identity/Dockerfile" },
  { name: "portfolio", dockerfile: "services/Portfolio/Skarbiec.Portfolio/Dockerfile" },
  { name: "marketdata", dockerfile: "services/MarketData/Skarbiec.MarketData/Dockerfile" },
  { name: "reporting", dockerfile: "services/Reporting/Skarbiec.Reporting/Dockerfile" },
  { name: "gateway", dockerfile: "gateway/Skarbiec.Gateway/Dockerfile" },
  { name: "web", dockerfile: "web/Dockerfile" },
];
const imageTag = (name) => `skarbiec-local/${name}:latest`;

function run(cmd, argv, { cwd = REPO_ROOT, inherit = false } = {}) {
  const res = spawnSync(cmd, argv, {
    cwd,
    encoding: "utf8",
    windowsHide: true,
    maxBuffer: 512 * 1024 * 1024,
    stdio: inherit ? "inherit" : "pipe",
  });
  const all = `${res.stdout ?? ""}\n${res.stderr ?? ""}`;
  return {
    ok: !res.error && res.status === 0,
    out: (res.stdout ?? "").trim(),
    tail: all.split(/\r?\n/).filter((l) => l.trim()).slice(-15).join("\n"),
    error: res.error?.message ?? "",
  };
}

const compose = (argv, opts) => run("docker", ["compose", "-p", PROJECT, "-f", COMPOSE_FILE, ...argv], opts);

function build(ref) {
  const rev = run("git", ["rev-parse", "--verify", `${ref}^{commit}`]);
  if (!rev.ok) throw new Error(`unknown ref ${ref}`);
  const revision = rev.out;

  const docker = run("docker", ["info", "--format", "{{.ServerVersion}}"]);
  if (!docker.ok) throw new Error("the Docker daemon is not reachable; start Docker Desktop");

  const work = mkdtempSync(path.join(tmpdir(), "skarbiec-compose-"));
  try {
    const archive = path.join(work, "src.tar");
    const src = path.join(work, "src");
    const archived = run("git", ["archive", "--format=tar", "-o", archive, revision]);
    if (!archived.ok) throw new Error(`git archive ${ref} failed: ${archived.tail}`);
    mkdirSync(src, { recursive: true });
    // Relative paths: GNU tar reads "C:" as a remote host.
    const untar = run("tar", ["-xf", path.join("..", "src.tar")], { cwd: src });
    if (!untar.ok) throw new Error(`tar extract failed: ${untar.tail || untar.error}`);

    for (const image of IMAGES) {
      console.log(`building ${image.name} @ ${revision.slice(0, 7)} ...`);
      const built = run("docker", [
        "build",
        "-f", path.join(src, ...image.dockerfile.split("/")),
        "-t", imageTag(image.name),
        "--label", STACK_LABEL,
        "--label", `${REVISION_LABEL}=${revision}`,
        src,
      ]);
      if (!built.ok) throw new Error(`docker build ${image.name} failed:\n${built.tail || built.error}`);
    }
  } finally {
    rmSync(work, { recursive: true, force: true, maxRetries: 3 });
  }

  run("docker", ["image", "prune", "-f", "--filter", `label=${STACK_LABEL}`]);
  return revision;
}

function missingImages() {
  return IMAGES.filter((i) => !run("docker", ["image", "inspect", imageTag(i.name)]).ok).map((i) => i.name);
}

function revisionOf(imageRef) {
  const res = run("docker", ["image", "inspect", imageRef, "--format", `{{index .Config.Labels "${REVISION_LABEL}"}}`]);
  return res.ok && res.out && res.out !== "<no value>" ? res.out : null;
}

function status() {
  const ids = run("docker", ["ps", "-a", "-q", "--filter", `label=com.docker.compose.project=${PROJECT}`]);
  if (!ids.ok) throw new Error(ids.tail || ids.error);
  if (!ids.out) {
    console.log("no containers (run: node scripts/compose.mjs up)");
    return true;
  }
  const inspect = run("docker", ["inspect", ...ids.out.split(/\s+/)]);
  if (!inspect.ok) throw new Error(inspect.tail || inspect.error);
  const rows = JSON.parse(inspect.out)
    .map((c) => ({
      service: c.Config.Labels["com.docker.compose.service"],
      state: c.State.Health ? `${c.State.Status} (${c.State.Health.Status})` : c.State.Status,
      image: c.Config.Image,
      imageId: c.Image,
    }))
    .sort((a, b) => a.service.localeCompare(b.service));

  let outdated = 0;
  for (const row of rows) {
    let note = "";
    if (row.image.startsWith("skarbiec-local/")) {
      const running = revisionOf(row.imageId);
      const newest = revisionOf(row.image);
      const newestId = run("docker", ["image", "inspect", row.image, "--format", "{{.Id}}"]).out;
      // A replaced image may already be gone from the daemon, so its revision is unknown.
      note = `runs ${running?.slice(0, 7) ?? "replaced image"}, newest ${newest?.slice(0, 7) ?? "none"}`;
      if (row.imageId !== newestId) {
        outdated++;
        note += "  OUTDATED";
      }
    }
    console.log(`${row.service.padEnd(11)} ${row.state.padEnd(22)} ${note}`);
  }
  if (outdated) console.log(`${outdated} container(s) run an outdated image; node scripts/compose.mjs up recreates them`);
  return true;
}

const [command, ...rest] = process.argv.slice(2);
let exitCode = 0;
try {
  switch (command) {
    case "build": {
      let ref = "HEAD";
      for (let i = 0; i < rest.length; i++) {
        if (rest[i] === "--ref" && rest[i + 1]) ref = rest[++i];
        else throw new Error(`unexpected argument: ${rest[i]}`);
      }
      try {
        const revision = build(ref);
        console.log(`COMPOSE_RESULT: ${JSON.stringify({ ok: true, revision })}`);
      } catch (err) {
        console.log(`COMPOSE_RESULT: ${JSON.stringify({ ok: false, error: err.message })}`);
        exitCode = 2;
      }
      break;
    }
    case "up": {
      const missing = missingImages();
      if (missing.length) throw new Error(`no image for ${missing.join(", ")}; run: node scripts/compose.mjs build`);
      exitCode = compose(["up", "-d", "--wait"], { inherit: true }).ok ? 0 : 2;
      break;
    }
    case "down":
      exitCode = compose(["down"], { inherit: true }).ok ? 0 : 2;
      break;
    case "reset":
      exitCode = compose(["down", "--volumes"], { inherit: true }).ok ? 0 : 2;
      break;
    case "status":
      status();
      break;
    default:
      throw new Error("usage: node scripts/compose.mjs <build [--ref <ref>]|up|down|reset|status>");
  }
} catch (err) {
  console.error(err.message);
  exitCode = 2;
}
process.exit(exitCode);
