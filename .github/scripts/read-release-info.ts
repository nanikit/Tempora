import { join } from "@std/path/join";
import { parse } from "@std/semver/parse";
import { readCommand, required } from "../../scripts/process.ts";
import { validateCommit, writeOutputs } from "./actions.ts";

if (import.meta.main) {
  await readReleaseInfo(
    required(Deno.args[0], "commit"),
    required(Deno.args[1], "directory"),
  );
}

export async function readReleaseInfo(
  commit: string,
  directory: string,
): Promise<void> {
  validateCommit(commit);
  const info = JSON.parse(
    await Deno.readTextFile(join(directory, "build-info.json")),
  );
  if (info.source_sha !== commit) {
    throw new Error("Release artifacts belong to a different commit.");
  }
  const version: string = info.version;
  parse(version);
  for (const platform of ["Windows", "Linux", "macOS"]) {
    const archive = join(directory, `Tempora-v${version}-${platform}.zip`);
    const stat = await Deno.stat(archive);
    if (!stat.isFile || stat.size === 0) {
      throw new Error(`Missing release archive: ${archive}`);
    }
  }
  const tag = `v${version}`;
  const existing = await readCommand("git", [
    "ls-remote",
    "--tags",
    "--refs",
    "origin",
    `refs/tags/${tag}`,
  ]);
  if (existing) {
    throw new Error(
      `Release tag already exists: ${tag}. Prepared artifacts will not be relabeled.`,
    );
  }
  await writeOutputs({ tag });
}
