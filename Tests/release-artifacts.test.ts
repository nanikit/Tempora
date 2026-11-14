import { assert, assertEquals, assertRejects } from "@std/assert";
import { join } from "@std/path/join";
import { findTestResult } from "../.github/scripts/find-test-result.ts";
import { prepareArtifacts } from "../.github/scripts/prepare-artifacts.ts";
import { readReleaseInfo } from "../.github/scripts/read-release-info.ts";
import { exportGame } from "../scripts/export.ts";
import { release } from "../scripts/release.ts";

import {
  otherSha,
  outputs,
  repository,
  run,
  scenario,
  sourceSha,
} from "./release-boundary.ts";

scenario(
  "Published versions are skipped and app and export versions stay aligned",
  async (boundary) => {
    boundary.tags = ["v1.5.3", "v1.5.4"];
    await seedProject();
    await prepareArtifacts(sourceSha);
    assertEquals(await readInfo(), { source_sha: sourceSha, version: "1.5.5" });
    assertEquals(
      await Deno.readTextFile("project.godot"),
      'config/version="1.5.5"\r\n',
    );
    assertEquals(
      await Deno.readTextFile("export_presets.cfg"),
      [
        'application/file_version="1.5.5.0"',
        'application/product_version="1.5.5"',
        'application/short_version="1.5.5"',
        'application/version="1.5.5"',
      ].join("\r\n"),
    );
    assertEquals(await outputs(), `version=1.5.5\ncommit=${sourceSha}\n`);
  },
);

scenario("Rebuilding retains the prepared version", async (boundary) => {
  boundary.tags = ["v1.5.3"];
  await seedProject();
  await prepareArtifacts(sourceSha);
  boundary.tags.push("v1.5.4");
  await prepareArtifacts(sourceSha);
  assertEquals((await readInfo()).version, "1.5.4");
  assertEquals(boundary.gitCalls.length, 1);
});

scenario(
  "Exports retain the supplied prerelease version and build metadata",
  async (boundary) => {
    await seedProject();
    await prepareArtifacts(sourceSha, { version: "1.6.0-rc.2+build.7" });
    await seedArchives("1.6.0-rc.2+build.7");
    await readReleaseInfo(sourceSha, ".");
    assertEquals(
      await outputs(),
      `version=1.6.0-rc.2+build.7\ncommit=${sourceSha}\ntag=v1.6.0-rc.2+build.7\n`,
    );
    assertEquals(boundary.gitCalls.length, 1);
  },
);

scenario(
  "Repeated preparation reuses successful tests from a previous run",
  async (boundary) => {
    boundary.runs([], [run(10, {
      event: "workflow_dispatch",
      conclusion: "failure",
      head_sha: otherSha,
      display_title: `Test ${sourceSha}`,
    })]);
    boundary.jobs(10, [{ id: 101, name: "gdUnit4", conclusion: "success" }]);
    await findTestResult(repository, sourceSha, { currentRunId: 20 });
    assertEquals(await outputs(), "test-passed=true\nrun-id=10\n");
  },
);

scenario(
  "Packaging retries retain successful tests from an earlier attempt",
  async (boundary) => {
    boundary.runs([run(10, { conclusion: "failure", run_attempt: 2 })]);
    boundary.jobs(10, [
      { id: 101, name: "Test", conclusion: "success" },
      { id: 102, name: "Export Game", conclusion: "failure" },
      { id: 103, name: "Export Game", conclusion: "cancelled" },
    ]);
    await findTestResult(repository, sourceSha, { currentRunId: 20 });
    assertEquals(await outputs(), "test-passed=true\nrun-id=10\n");
  },
);

scenario(
  "Workflow reruns retain tests passed in an earlier attempt of the same run",
  async (boundary) => {
    boundary.runs([
      run(10, { status: "in_progress", conclusion: null, run_attempt: 2 }),
    ]);
    boundary.jobs(10, [
      { id: 101, name: "Test", conclusion: "success" },
      { id: 104, name: "Test", conclusion: null },
    ]);
    await findTestResult(repository, sourceSha, { currentRunId: 10 });
    assertEquals(await outputs(), "test-passed=true\nrun-id=10\n");
  },
);

scenario(
  "Publication verifies the source and leaves prepared archives unchanged",
  async () => {
    await seedRelease();
    const before = await archiveBytes();
    await readReleaseInfo(sourceSha, ".");
    assertEquals(await outputs(), "tag=v1.5.4\n");
    assertEquals(await archiveBytes(), before);
  },
);

scenario(
  "Artifacts from another commit cannot be published",
  async (boundary) => {
    await seedRelease({ commit: otherSha });
    await assertRejects(
      () => readReleaseInfo(sourceSha, "."),
      Error,
      "different commit",
    );
    assertEquals(boundary.gitCalls.length, 0);
  },
);

scenario(
  "A claimed version is rejected without relabeling the prepared app",
  async (boundary) => {
    boundary.tags = ["v1.5.4"];
    await seedRelease();
    const before = await archiveBytes();
    await assertRejects(
      () => readReleaseInfo(sourceSha, "."),
      Error,
      "Release tag already exists",
    );
    assertEquals(await archiveBytes(), before);
    assertEquals((await readInfo()).version, "1.5.4");
  },
);

scenario(
  "The current first attempt and runs for other commits are not reused",
  async (boundary) => {
    boundary.runs([
      run(10),
      run(11, { head_sha: otherSha }),
      run(12, { head_branch: "feature" }),
    ], [
      run(13, {
        event: "workflow_dispatch",
      }),
    ]);
    boundary.jobs(13, [{
      id: 101,
      name: "Test",
      conclusion: "success",
      steps: [{ name: `Checkout source ${otherSha}` }],
    }]);
    await findTestResult(repository, sourceSha, { currentRunId: 10 });
    await assertRejects(outputs, Deno.errors.NotFound);
  },
);

scenario(
  "Failed remote tag lookup produces no publication output",
  async (boundary) => {
    await seedRelease();
    boundary.gitExitCode = 128;
    await assertRejects(
      () => readReleaseInfo(sourceSha, "."),
      Error,
      "exit code 128",
    );
    await assertRejects(outputs, Deno.errors.NotFound);
  },
);

scenario("An empty archive stops publication", async () => {
  await seedRelease();
  await Deno.writeTextFile("Tempora-v1.5.4-macOS.zip", "");
  await assertRejects(
    () => readReleaseInfo(sourceSha, "."),
    Error,
    "Missing release archive",
  );
});

scenario(
  "Rebuilding rejects a version prepared for another commit",
  async () => {
    await seedProject();
    await seedRelease({ commit: otherSha });
    await assertRejects(
      () => prepareArtifacts(sourceSha),
      Error,
      "different commit",
    );
    assertEquals(
      await Deno.readTextFile("project.godot"),
      'config/version="1.5.3"\r\n',
    );
  },
);

scenario(
  "Release atomically pushes both branches from a clean main branch",
  async (boundary) => {
    await release();
    assertEquals(boundary.gitCalls.at(-1), [
      "push",
      "--atomic",
      "origin",
      "HEAD:refs/heads/main",
      "HEAD:refs/heads/release",
    ]);
  },
);

scenario(
  "Release does not push outside main or with uncommitted changes",
  async (boundary) => {
    boundary.branch = "feature";
    await assertRejects(release, Error, "main branch");
    boundary.branch = "main";
    boundary.status = " M project.godot";
    await assertRejects(release, Error, "pending changes");
    assert(!boundary.gitCalls.some((args) => args[0] === "push"));
  },
);

scenario(
  "Export creates output directories containing spaces and propagates failure codes",
  async (boundary) => {
    boundary.exportExitCode = 7;
    const output = join("output directory", "Tempora.exe");
    const code = await exportGame("godot", {
      preset: "Windows test",
      mode: "debug",
      output,
    });
    assertEquals(code, 7);
    assert((await Deno.stat("output directory")).isDirectory);
    assertEquals(boundary.exportArgs, [
      "--headless",
      "--xr-mode",
      "off",
      "--path",
      ".",
      "--export-debug",
      "Windows test",
      output,
    ]);
  },
);

async function seedProject(): Promise<void> {
  await Deno.writeTextFile("project.godot", 'config/version="1.5.3"\r\n');
  await Deno.writeTextFile(
    "export_presets.cfg",
    [
      'application/file_version=""',
      'application/product_version=""',
      'application/short_version=""',
      'application/version=""',
    ].join("\r\n"),
  );
}

async function seedRelease(options: { commit?: string } = {}): Promise<void> {
  await Deno.writeTextFile(
    "build-info.json",
    JSON.stringify({
      source_sha: options.commit ?? sourceSha,
      version: "1.5.4",
    }),
  );
  await seedArchives("1.5.4");
}

async function seedArchives(version: string): Promise<void> {
  for (const platform of ["Windows", "Linux", "macOS"]) {
    await Deno.writeTextFile(
      `Tempora-v${version}-${platform}.zip`,
      `prepared ${platform} bytes`,
    );
  }
}

function archiveBytes(): Promise<Uint8Array[]> {
  return Promise.all(
    ["Windows", "Linux", "macOS"].map((platform) =>
      Deno.readFile(`Tempora-v1.5.4-${platform}.zip`)
    ),
  );
}

async function readInfo(): Promise<{ source_sha: string; version: string }> {
  return JSON.parse(await Deno.readTextFile("build-info.json"));
}
