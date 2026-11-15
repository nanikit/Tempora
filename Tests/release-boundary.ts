import { assert, assertEquals } from "@std/assert";
import { join } from "@std/path/join";
import { stubProperty } from "@std/testing/unstable-stub-property";
import {
  type ArtifactJob,
  type ArtifactRun,
} from "../.github/scripts/artifact-runs.ts";

export const sourceSha = "a".repeat(40);
export const otherSha = "b".repeat(40);
export const repository = "test/Tempora";

export function scenario(
  name: string,
  body: (boundary: Boundary) => Promise<void>,
): void {
  Deno.test(name, async () => {
    const directory = await Deno.makeTempDir({ prefix: "tempora-artifacts-" });
    const previousDirectory = Deno.cwd();
    const previousOutput = Deno.env.get("GITHUB_OUTPUT");
    const boundary = new Boundary();
    using _command = stubProperty(
      Deno,
      "Command",
      class {
        constructor(
          private command: string | URL,
          private options: Deno.CommandOptions,
        ) {}
        output() {
          const { code, text } = boundary.command(
            String(this.command),
            this.options.args ?? [],
          );
          return Promise.resolve({
            success: code === 0,
            code,
            stdout: new TextEncoder().encode(text),
          });
        }
        spawn() {
          assertEquals(this.command, "godot");
          boundary.exportArgs = this.options.args ?? [];
          return { status: Promise.resolve({ code: boundary.exportExitCode }) };
        }
      } as unknown as typeof Deno.Command,
    );
    try {
      Deno.chdir(directory);
      Deno.env.set("GITHUB_OUTPUT", join(directory, "outputs"));
      await body(boundary);
      boundary.verify();
    } finally {
      Deno.chdir(previousDirectory);
      if (previousOutput === undefined) Deno.env.delete("GITHUB_OUTPUT");
      else Deno.env.set("GITHUB_OUTPUT", previousOutput);
      await Deno.remove(directory, { recursive: true });
    }
  });
}

export class Boundary {
  tags: string[] = [];
  gitCalls: string[][] = [];
  gitExitCode = 0;
  ghExitCode = 0;
  branch = "main";
  status = "";
  exportExitCode = 0;
  exportArgs: string[] = [];
  apiCalls: string[] = [];
  publishedTagCommit = sourceSha;
  private responses = new Map<
    string,
    { response: unknown; fields: string[] }[]
  >();
  private lastResponses = new Map<
    string,
    { response: unknown; fields: string[] }
  >();

  api(
    path: string,
    response: unknown,
    options: { method?: string; fields?: string[] } = {},
  ): void {
    const key = `${options.method ?? "GET"} repos/${repository}/${path}`;
    const queue = this.responses.get(key) ?? [];
    queue.push({ response, fields: options.fields ?? [] });
    this.responses.set(key, queue);
  }

  runs(runs: ArtifactRun[], manual: ArtifactRun[] = []): void {
    this.api(
      `actions/workflows/test.yml/runs?event=push&head_sha=${sourceSha}&per_page=100`,
      [{ workflow_runs: runs }],
    );
    this.api(
      "actions/workflows/test.yml/runs?event=workflow_dispatch&per_page=100",
      [{ workflow_runs: manual }],
    );
  }

  jobs(runId: number, jobs: ArtifactJob[] = [
    { id: 101, name: "Test", conclusion: "success" },
    { id: 102, name: "Export Game", conclusion: "success" },
  ]): void {
    this.api(`actions/runs/${runId}/jobs?filter=all&per_page=100`, [{
      jobs: jobs.slice(0, 1),
    }, { jobs: jobs.slice(1) }]);
  }

  artifacts(runId: number, artifacts = releaseArtifacts()): void {
    this.api(`actions/runs/${runId}/artifacts?per_page=100`, [{
      artifacts: artifacts.slice(0, 2),
    }, { artifacts: artifacts.slice(2) }]);
  }

  command(command: string, args: string[]): { code: number; text: string } {
    if (command === "git") {
      this.gitCalls.push(args);
      if (this.gitExitCode) return { code: this.gitExitCode, text: "" };
      if (args[0] === "branch") return { code: 0, text: this.branch };
      if (args[0] === "status") return { code: 0, text: this.status };
      if (args[0] === "push") return { code: 0, text: "" };
      if (args[0] === "ls-remote" && !args.includes("--refs")) {
        return {
          code: 0,
          text: `taghash\t${args[3]}\n${this.publishedTagCommit}\t${args[4]}`,
        };
      }
      assertEquals(args.slice(0, 4), [
        "ls-remote",
        "--tags",
        "--refs",
        "origin",
      ]);
      const tags = this.tags.map((tag) => `refs/tags/${tag}`).filter((tag) =>
        !args[4] || args[4] === tag
      );
      return { code: 0, text: tags.map((tag) => `hash\t${tag}`).join("\n") };
    }
    assertEquals(command, "gh");
    if (this.ghExitCode) return { code: this.ghExitCode, text: "" };
    assertEquals(args[0], "api");
    assertEquals(
      args[args.indexOf("-H") + 1],
      "X-GitHub-Api-Version: 2026-03-10",
    );
    const method = args[args.indexOf("--method") + 1];
    this.apiCalls.push(`${method} ${args[1]}`);
    const key = `${method} ${args[1]}`;
    const queued = this.responses.get(key)?.shift();
    if (method === "GET" && queued) this.lastResponses.set(key, queued);
    const expected = queued ??
      (method === "GET" ? this.lastResponses.get(key) : undefined);
    assert(expected, `Unexpected gh call: ${args.join(" ")}`);
    for (const field of expected.fields) {
      assert(args.includes(field), `Missing API field: ${field}`);
    }
    if (method === "GET" && args[1].includes("per_page=100")) {
      assert(args.includes("--paginate") && args.includes("--slurp"));
    }
    return {
      code: 0,
      text: expected.response === null ? "" : JSON.stringify(expected.response),
    };
  }

  verify(): void {
    for (const [key, queue] of this.responses) {
      if (!key.startsWith("GET ")) {
        assertEquals(queue.length, 0, `Unused API expectation: ${key}`);
      }
    }
  }
}

export function run(
  id: number,
  options: Partial<ArtifactRun> = {},
): ArtifactRun {
  return {
    id,
    head_sha: sourceSha,
    head_branch: "main",
    event: "push",
    display_title: "feat: improve playback",
    status: "completed",
    conclusion: "success",
    run_attempt: 1,
    path: ".github/workflows/test.yml",
    created_at: new Date().toISOString(),
    head_repository: { full_name: repository },
    ...options,
  };
}

export function outputs(): Promise<string> {
  return Deno.readTextFile(Deno.env.get("GITHUB_OUTPUT")!);
}

export function releaseArtifacts(
  options: { version?: string; firstId?: number } = {},
) {
  return [
    ...["Windows", "Linux", "macOS"].map((platform) =>
      `Tempora-v${options.version ?? "1.5.4"}-${platform}.zip`
    ),
    "build-info.json",
  ].map((name, index) => ({
    id: (options.firstId ?? 201) + index,
    name,
    expired: false,
  }));
}
