import { parse } from "@std/semver/parse";
import { readCommand, required } from "../../scripts/process.ts";
import { validateCommit, writeOutputs } from "./actions.ts";

if (import.meta.main) {
  await prepareArtifacts(required(Deno.args[0], "commit"), {
    version: Deno.args[1],
  });
}

export async function prepareArtifacts(
  commit: string,
  options: { version?: string } = {},
): Promise<void> {
  validateCommit(commit);
  const project = await Deno.readTextFile("project.godot");
  const version = await readPreparedVersion(commit) ?? options.version ??
    await resolveBuildVersion(project);
  const { major, minor, patch } = parse(version);
  const shortVersion = `${major}.${minor}.${patch}`;
  await Deno.writeTextFile(
    "project.godot",
    project.replace(
      /^config\/version="[^"]+"/m,
      () => `config/version="${version}"`,
    ),
  );

  const presets = await Deno.readTextFile("export_presets.cfg");
  const versions: Record<string, string> = {
    file_version: `${shortVersion}.0`,
    product_version: version,
    short_version: shortVersion,
    version,
  };
  await Deno.writeTextFile(
    "export_presets.cfg",
    presets.replace(
      /application\/(file_version|product_version|short_version|version)="[^"]*"/g,
      (_, key: string) => `application/${key}="${versions[key]}"`,
    ),
  );

  await Deno.writeTextFile(
    "build-info.json",
    JSON.stringify({ source_sha: commit, version }, null, 2) + "\n",
  );
  await writeOutputs({ version, commit });
}

async function readPreparedVersion(
  commit: string,
): Promise<string | undefined> {
  let text: string;
  try {
    text = await Deno.readTextFile("build-info.json");
  } catch (error) {
    if (error instanceof Deno.errors.NotFound) return undefined;
    throw error;
  }
  const info = JSON.parse(text);
  if (info.source_sha !== commit) {
    throw new Error("Build metadata belongs to a different commit.");
  }
  return info.version;
}

async function resolveBuildVersion(project: string): Promise<string> {
  let candidate = project.match(/^config\/version="([^"]+)"\r?$/m)?.[1];
  if (!candidate) {
    throw new Error("project.godot must define application config/version.");
  }
  let parsed = parse(candidate);
  const remote = await readCommand("git", [
    "ls-remote",
    "--tags",
    "--refs",
    "origin",
  ]);
  const tags = new Set(
    remote.split(/\r?\n/).map((line) => line.split(/\s+/)[1]),
  );
  while (tags.has(`refs/tags/v${candidate}`)) {
    candidate = `${parsed.major}.${parsed.minor}.${parsed.patch + 1}`;
    parsed = parse(candidate);
  }
  return candidate;
}
