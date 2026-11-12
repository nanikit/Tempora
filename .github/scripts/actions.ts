import { required } from "../../scripts/process.ts";

export function validateCommit(commit: string): void {
  if (!/^[0-9a-f]{40}$/.test(commit)) {
    throw new Error("Commit must be a full lowercase SHA.");
  }
}

export async function writeOutputs(
  values: Record<string, string | number>,
): Promise<void> {
  const path = required(Deno.env.get("GITHUB_OUTPUT"), "GITHUB_OUTPUT");
  const lines = Object.entries(values).map(([key, value]) =>
    `${key}=${value}\n`
  ).join("");
  await Deno.writeTextFile(path, lines, { append: true });
}
