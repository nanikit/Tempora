import { dirname } from "@std/path/dirname";
import { required } from "./process.ts";

if (import.meta.main) {
  Deno.exit(
    await exportGame(required(Deno.args[0], "Godot executable"), {
      preset: required(Deno.args[1], "preset"),
      mode: required(Deno.args[2], "mode"),
      output: required(Deno.args[3], "output"),
    }),
  );
}

export async function exportGame(
  executable: string,
  options: { preset: string; mode: string; output: string },
): Promise<number> {
  await Deno.mkdir(dirname(options.output), { recursive: true });
  const result = await new Deno.Command(executable, {
    args: [
      "--headless",
      "--xr-mode",
      "off",
      "--path",
      ".",
      `--export-${options.mode}`,
      options.preset,
      options.output,
    ],
    stdin: "inherit",
    stdout: "inherit",
    stderr: "inherit",
  }).spawn().status;
  return result.code;
}
