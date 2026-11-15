import { assertEquals, assertRejects } from "@std/assert";
import {
  planRelease,
  prepareRelease,
  type ReleaseEvent,
} from "../.github/scripts/plan-release.ts";
import { type ArtifactRun } from "../.github/scripts/artifact-runs.ts";
import {
  type Boundary,
  otherSha,
  repository,
  run,
  scenario,
  sourceSha,
} from "./release-boundary.ts";

scenario(
  "A release request publishes artifacts that finished first",
  async (boundary) => {
    const event = releaseEvent(boundary);
    boundary.runs([run(10)]);
    boundary.artifacts(10);
    assertEquals(await planRelease(repository, event), publication());
  },
);

scenario(
  "A pending build releases the runner and its completion resumes publication",
  async (boundary) => {
    const request = releaseEvent(boundary);
    boundary.runs([run(10, { status: "in_progress", conclusion: null })]);
    assertEquals(await planRelease(repository, request), { action: "none" });
    const completion = releaseEvent(boundary, {
      completed: run(10),
      currentId: 60,
    });
    boundary.runs([run(10)]);
    boundary.artifacts(10);
    const resumed = await planRelease(repository, completion);
    assertEquals(resumed, {
      action: "resume",
      commit: sourceSha,
      requestId: 50,
    });
    boundary.api("actions/runs/50/rerun", null, { method: "POST" });
    await prepareRelease(repository, resumed);
    assertEquals(
      await planRelease(repository, releaseEvent(boundary)),
      publication(),
    );
  },
);

scenario(
  "A completion before any release request does not publish",
  async (boundary) => {
    const event = releaseEvent(boundary, {
      completed: run(10),
      requested: false,
    });
    assertEquals(await planRelease(repository, event), { action: "none" });
  },
);

scenario(
  "Completion events leave queued and active requests alone",
  async (boundary) => {
    for (const status of ["queued", "pending", "waiting", "in_progress"]) {
      const event = releaseEvent(boundary, {
        completed: run(10),
        requestStatus: status,
      });
      boundary.runs([run(10)]);
      boundary.artifacts(10);
      assertEquals(await planRelease(repository, event), { action: "none" });
    }
  },
);

scenario(
  "A resumed request retains its identity when a newer request exists",
  async (boundary) => {
    const event = releaseEvent(boundary, {
      otherRequests: [run(70, {
        head_branch: "release",
        path: ".github/workflows/release.yml",
        display_title: "feat: improve playback",
      })],
    });
    boundary.runs([run(10)]);
    boundary.artifacts(10);
    boundary.jobs(70, []);
    assertEquals(await planRelease(repository, event), publication());
  },
);

scenario(
  "Manual builds publish the requested source even after the release branch moves",
  async (boundary) => {
    const completed = run(20, {
      event: "workflow_dispatch",
      head_sha: otherSha,
    });
    const event = releaseEvent(boundary, {
      completed,
      otherRequests: [
        run(90, {
          head_branch: "release",
          head_sha: otherSha,
          path: ".github/workflows/release.yml",
          display_title: "feat: improve playback",
        }),
        run(91, {
          event: "workflow_run",
          path: ".github/workflows/release.yml",
          display_title: "Release",
        }),
      ],
    });
    boundary.runs([], [completed]);
    boundary.jobs(20, [{
      id: 101,
      name: "Test",
      conclusion: "success",
      steps: [{ name: `Checkout source ${sourceSha}` }],
    }]);
    boundary.artifacts(20);
    assertEquals(await planRelease(repository, event), {
      action: "resume",
      commit: sourceSha,
      requestId: 50,
    });
    assertEquals(
      await planRelease(repository, releaseEvent(boundary)),
      publication({ runId: 20 }),
    );
  },
);

scenario(
  "Missing preparation is dispatched once and later events wait for completion",
  async (boundary) => {
    const event = releaseEvent(boundary);
    boundary.runs([]);
    const plan = await planRelease(repository, event);
    assertEquals(plan, {
      action: "dispatch",
      commit: sourceSha,
      requestId: 50,
    });
    boundary.api(`commits/${sourceSha}`, {
      commit: { message: "feat: improve playback\n\nCommit body" },
    });
    boundary.api("actions/workflows/test.yml/dispatches", null, {
      method: "POST",
      fields: [
        "ref=main",
        `inputs[commit]=${sourceSha}`,
        "inputs[title]=feat: improve playback",
      ],
    });
    await prepareRelease(repository, plan);

    const replay = releaseEvent(boundary, {
      priorJobs: [{
        id: 201,
        name: "Prepare release 50: dispatch",
        conclusion: "success",
      }],
    });
    boundary.runs([]);
    assertEquals(await planRelease(repository, replay), { action: "none" });
  },
);

scenario(
  "Packaging retries retain tests and allow earlier manual attempts",
  async (boundary) => {
    const failed = run(10, { conclusion: "failure", run_attempt: 4 });
    const completion = releaseEvent(boundary, { completed: failed });
    boundary.runs([failed]);
    boundary.jobs(10, [
      { id: 101, name: "Test", conclusion: "success" },
      { id: 102, name: "Export Game", conclusion: "success" },
      { id: 103, name: "Package Artifacts", conclusion: "success" },
      { id: 104, name: "Package Artifacts", conclusion: "failure" },
    ]);
    assertEquals(await planRelease(repository, completion), {
      action: "resume",
      commit: sourceSha,
      requestId: 50,
    });
    const plan = await planRelease(repository, releaseEvent(boundary));
    assertEquals(plan, {
      action: "retry",
      commit: sourceSha,
      requestId: 50,
      runId: 10,
      attempt: 4,
      jobId: 104,
    });
    boundary.api("actions/jobs/104/rerun", null, { method: "POST" });
    await prepareRelease(repository, plan);
  },
);

scenario(
  "Expired artifacts rebuild from export without retesting",
  async (boundary) => {
    const event = releaseEvent(boundary);
    boundary.runs([run(10)]);
    boundary.artifacts(10, { expired: true });
    boundary.jobs(10, [
      { id: 101, name: "Test", conclusion: "success" },
      { id: 102, name: "Upload", conclusion: "success" },
    ]);
    assertEquals(await planRelease(repository, event), {
      action: "retry",
      commit: sourceSha,
      requestId: 50,
      runId: 10,
      attempt: 1,
      jobId: 102,
    });
  },
);

scenario(
  "Late completion events cannot retry the same request twice",
  async (boundary) => {
    const failed = run(10, { conclusion: "failure", run_attempt: 4 });
    const event = releaseEvent(boundary, {
      completed: failed,
      priorJobs: [retryJob()],
    });
    boundary.runs([failed]);
    assertEquals(await planRelease(repository, event), { action: "none" });

    boundary.runs([run(10, { conclusion: "failure", run_attempt: 5 })]);
    await assertRejects(
      () => planRelease(repository, event),
      Error,
      "failed after retrying run 10",
    );
  },
);

scenario(
  "A successful retry resumes publication on its completion event",
  async (boundary) => {
    const succeeded = run(10, { run_attempt: 5 });
    const event = releaseEvent(boundary, {
      completed: succeeded,
      priorJobs: [retryJob()],
    });
    boundary.runs([succeeded]);
    boundary.artifacts(10);
    assertEquals(await planRelease(repository, event), {
      action: "resume",
      commit: sourceSha,
      requestId: 50,
    });
    assertEquals(
      await planRelease(
        repository,
        releaseEvent(boundary, {
          priorJobs: [retryJob()],
        }),
      ),
      publication(),
    );
  },
);

scenario(
  "An explicit new request receives its own retry budget",
  async (boundary) => {
    const event = releaseEvent(boundary, {
      requestId: 70,
      currentId: 70,
      priorJobs: [retryJob()],
    });
    boundary.runs([run(10, { conclusion: "failure", run_attempt: 5 })]);
    boundary.jobs(10, [
      { id: 101, name: "gdUnit4", conclusion: "success" },
      { id: 102, name: "Export Game", conclusion: "success" },
      { id: 103, name: "Export Game", conclusion: "failure" },
    ]);
    assertEquals(await planRelease(repository, event), {
      action: "retry",
      commit: sourceSha,
      requestId: 70,
      runId: 10,
      attempt: 5,
      jobId: 103,
    });
  },
);

scenario(
  "A handler created before a request retains its retry journal",
  async (boundary) => {
    const request = run(70, {
      head_branch: "release",
      path: ".github/workflows/release.yml",
      display_title: "feat: improve playback",
    });
    const oldHandler = {
      ...request,
      id: 60,
      event: "workflow_run",
      head_branch: "main",
      display_title: `Release Test ${sourceSha}`,
    };
    const current = { ...oldHandler, id: 80, display_title: "Release" };
    boundary.api("actions/runs/80", current);
    boundary.api(
      "actions/runs/10",
      run(10, { run_attempt: 5, conclusion: "failure" }),
    );
    boundary.api("actions/workflows/release.yml/runs?per_page=100", [{
      workflow_runs: [current, request, oldHandler],
    }]);
    boundary.api("releases?per_page=100", [[]]);
    boundary.jobs(80, []);
    boundary.jobs(70, []);
    boundary.jobs(60, [{
      id: 301,
      name: "Prepare release 70: retry 10/4",
      conclusion: "success",
    }]);
    boundary.runs([run(10, { run_attempt: 5, conclusion: "failure" })]);
    await assertRejects(
      () =>
        planRelease(repository, {
          eventName: "workflow_run",
          runId: 80,
          completedRunId: 10,
        }),
      Error,
      "failed after retrying run 10",
    );
  },
);

scenario(
  "Failed tests stop publication without creating a test run",
  async (boundary) => {
    const event = releaseEvent(boundary);
    boundary.runs([run(10, { conclusion: "failure" })]);
    boundary.jobs(10, [{ id: 101, name: "Test", conclusion: "failure" }]);
    await assertRejects(
      () => planRelease(repository, event),
      Error,
      "No successful test result",
    );
  },
);

scenario(
  "A failed preparation request is reported instead of repeated",
  async (boundary) => {
    const event = releaseEvent(boundary, {
      priorJobs: [{ ...retryJob(), conclusion: "failure" }],
    });
    await assertRejects(
      () => planRelease(repository, event),
      Error,
      "Preparation request failed",
    );
  },
);

scenario(
  "A completion beyond the request deadline cannot publish",
  async (boundary) => {
    const event = releaseEvent(boundary, {
      completed: run(10),
      createdAt: "2020-01-01T00:00:00Z",
    });
    await assertRejects(
      () => planRelease(repository, event),
      Error,
      "Timed out",
    );
  },
);

scenario(
  "Repeated completion events skip an already published source",
  async (boundary) => {
    const event = releaseEvent(boundary, {
      completed: run(10),
      published: true,
    });
    assertEquals(await planRelease(repository, event), { action: "none" });
  },
);

scenario(
  "A tag pointing elsewhere does not count as a completed release",
  async (boundary) => {
    const event = releaseEvent(boundary, {
      published: true,
      tagCommit: otherSha,
    });
    boundary.runs([run(10)]);
    boundary.artifacts(10);
    assertEquals(await planRelease(repository, event), publication());
  },
);

scenario(
  "Untrusted completion events cannot request or publish artifacts",
  async (boundary) => {
    for (
      const completed of [
        run(10, { event: "pull_request" }),
        run(10, { head_repository: { full_name: "untrusted/Tempora" } }),
        run(10, { path: ".github/workflows/other.yml" }),
        run(10, { head_branch: "feature" }),
        run(10, { event: "workflow_dispatch" }),
      ]
    ) {
      const event = releaseEvent(boundary, { completed });
      if (completed.event === "workflow_dispatch") boundary.jobs(10, []);
      assertEquals(await planRelease(repository, event), { action: "none" });
    }
  },
);

scenario(
  "API failures stop event processing without starting preparation",
  async (boundary) => {
    const event = releaseEvent(boundary);
    boundary.ghExitCode = 1;
    await assertRejects(
      () => planRelease(repository, event),
      Error,
      "exit code 1",
    );
  },
);

function publication(options: { runId?: number } = {}) {
  return {
    action: "publish" as const,
    commit: sourceSha,
    requestId: 50,
    runId: options.runId ?? 10,
    artifactId: 201,
  };
}

function retryJob() {
  return {
    id: 301,
    name: "Prepare release 50: retry 10/4",
    conclusion: "success",
  };
}

function releaseEvent(boundary: Boundary, options: {
  currentId?: number;
  requestId?: number;
  requestStatus?: string;
  completed?: ArtifactRun;
  otherRequests?: ArtifactRun[];
  requested?: boolean;
  priorJobs?: { id: number; name: string; conclusion: string }[];
  createdAt?: string;
  published?: boolean;
  tagCommit?: string;
} = {}): ReleaseEvent {
  const requestId = options.requestId ?? 50;
  const currentId = options.currentId ?? (options.completed ? 60 : requestId);
  const request = run(requestId, {
    head_branch: "release",
    path: ".github/workflows/release.yml",
    display_title: "feat: improve playback",
    created_at: options.createdAt ?? new Date().toISOString(),
    status: options.requestStatus ?? "completed",
  });
  const current = options.completed
    ? {
      ...request,
      id: currentId,
      event: "workflow_run",
      head_branch: "main",
      head_sha: otherSha,
      display_title: "Release",
    }
    : { ...request, id: currentId };
  boundary.api(`actions/runs/${currentId}`, current);
  if (options.completed) {
    boundary.api(`actions/runs/${options.completed.id}`, options.completed);
  }
  boundary.api("actions/workflows/release.yml/runs?per_page=100", [{
    workflow_runs: options.requested === false ? [current] : [
      request,
      ...(currentId === requestId ? [] : [current]),
      ...(options.otherRequests ?? []),
    ],
  }]);
  boundary.jobs(currentId, options.priorJobs ?? []);
  if (currentId !== requestId) boundary.jobs(requestId, []);
  boundary.api("releases?per_page=100", [
    options.published
      ? [{ tag_name: "v1.5.4", target_commitish: sourceSha, draft: false }]
      : [],
  ]);
  boundary.publishedTagCommit = options.tagCommit ?? sourceSha;
  return {
    eventName: current.event,
    runId: currentId,
    completedRunId: options.completed?.id,
  };
}
