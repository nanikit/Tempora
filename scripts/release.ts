import { readCommand } from "./process.ts";

if (import.meta.main) await release();

export async function release(): Promise<void> {
  if (await readCommand("git", ["branch", "--show-current"]) !== "main") {
    throw new Error("Run mise release from the main branch.");
  }
  if (await readCommand("git", ["status", "--porcelain"])) {
    throw new Error("Commit or stash pending changes before releasing.");
  }

  // Keep both branches on the same commit if either update is rejected.
  await readCommand("git", [
    "push",
    "--atomic",
    "origin",
    "HEAD:refs/heads/main",
    "HEAD:refs/heads/release",
  ]);
}
