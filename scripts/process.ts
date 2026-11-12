export async function readCommand(
  command: string,
  args: string[],
): Promise<string> {
  const result = await new Deno.Command(command, {
    args,
    stdin: "null",
    stdout: "piped",
    stderr: "inherit",
  }).output();
  if (!result.success) {
    throw new Error(
      `${command} ${args.join(" ")} failed with exit code ${result.code}.`,
    );
  }
  return new TextDecoder().decode(result.stdout).trim();
}

export function required(value: string | undefined, name: string): string {
  if (!value) throw new Error(`${name} is required.`);
  return value;
}
