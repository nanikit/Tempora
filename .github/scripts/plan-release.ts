import { readCommand, required } from "../../scripts/process.ts";
import { validateCommit, writeOutputs } from "./actions.ts";
import {
  artifactApi,
  artifactCommit,
  type ArtifactJob,
  type ArtifactRun,
  getArtifactJobs,
  getArtifactRuns,
  getReleaseArtifacts,
  isSuccessfulTest,
  type ReleaseArtifacts,
} from "./artifact-runs.ts";

if (import.meta.main) {
  const repository = required(
    Deno.env.get("GITHUB_REPOSITORY"),
    "GITHUB_REPOSITORY",
  );
  if (Deno.args[0] === "prepare") {
    await prepareRelease(
      repository,
      JSON.parse(required(Deno.env.get("RELEASE_PLAN"), "RELEASE_PLAN")),
    );
  } else {
    const payload = JSON.parse(
      await Deno.readTextFile(
        required(Deno.env.get("GITHUB_EVENT_PATH"), "GITHUB_EVENT_PATH"),
      ),
    );
    const plan = await planRelease(repository, {
      eventName: required(
        Deno.env.get("GITHUB_EVENT_NAME"),
        "GITHUB_EVENT_NAME",
      ),
      runId: Number(required(Deno.env.get("GITHUB_RUN_ID"), "GITHUB_RUN_ID")),
      completedRunId: payload.workflow_run?.id,
    });
    await writeOutputs(planOutputs(plan));
  }
}

export interface ReleaseEvent {
  eventName: string;
  runId: number;
  completedRunId?: number;
}

export type ReleasePlan =
  | { action: "none" }
  | { action: "resume"; commit: string; requestId: number }
  | { action: "dispatch"; commit: string; requestId: number }
  | {
    action: "retry";
    commit: string;
    requestId: number;
    runId: number;
    attempt: number;
    jobId: number;
  }
  | ({
    action: "publish";
    commit: string;
    requestId: number;
    runId: number;
  } & ReleaseArtifacts);

export async function planRelease(
  repository: string,
  event: ReleaseEvent,
): Promise<ReleasePlan> {
  const current = await artifactApi<ArtifactRun>(
    `repos/${repository}/actions/runs/${event.runId}`,
  );
  if (current.event !== event.eventName || !isReleaseRun(repository, current)) {
    return { action: "none" };
  }
  const commit = await eventCommit(repository, { current, event });
  if (!commit) return { action: "none" };
  validateCommit(commit);

  const history = await releaseHistory(repository, { commit, current });
  const request = isReleaseRequest(current)
    ? current
    : history.find(isReleaseRequest);
  if (!request) return { action: "none" };
  if (current.event === "workflow_run" && request.status !== "completed") {
    return { action: "none" };
  }
  if (await alreadyPublished(repository, commit)) return { action: "none" };
  if (Date.now() - Date.parse(request.created_at) >= 2 * 60 * 60 * 1000) {
    throw new Error(`Timed out waiting for release artifacts for ${commit}.`);
  }

  const preparations = await preparationHistory(repository, {
    history,
    requestId: request.id,
  });
  const plan = await planArtifacts(repository, {
    commit,
    requestId: request.id,
    preparations,
  });
  // Re-running the original event preserves its OIDC source SHA for attestation.
  return current.event === "workflow_run" && plan.action !== "none"
    ? { action: "resume", commit, requestId: request.id }
    : plan;
}

export async function prepareRelease(
  repository: string,
  plan: ReleasePlan,
): Promise<void> {
  if (plan.action === "resume") {
    await artifactApi(
      `repos/${repository}/actions/runs/${plan.requestId}/rerun`,
      {
        method: "POST",
      },
    );
  } else if (plan.action === "dispatch") {
    validateCommit(plan.commit);
    const source = await artifactApi<{ commit: { message: string } }>(
      `repos/${repository}/commits/${plan.commit}`,
    );
    await artifactApi(
      `repos/${repository}/actions/workflows/test.yml/dispatches`,
      {
        method: "POST",
        fields: [
          "ref=main",
          `inputs[commit]=${plan.commit}`,
          `inputs[title]=${source.commit.message.split(/\r?\n/, 1)[0]}`,
        ],
      },
    );
  } else if (plan.action === "retry") {
    await artifactApi(`repos/${repository}/actions/jobs/${plan.jobId}/rerun`, {
      method: "POST",
    });
  } else {
    throw new Error(`Not a preparation action: ${plan.action}`);
  }
}

export function planOutputs(
  plan: ReleasePlan,
): Record<string, string | number> {
  const outputs: Record<string, string | number> = {
    action: plan.action,
    plan: JSON.stringify(plan),
  };
  if (plan.action === "none") return outputs;
  outputs.commit = plan.commit;
  if (plan.action === "publish") {
    outputs["run-id"] = plan.runId;
    outputs["artifact-ids"] = plan.artifactIds.join(",");
    outputs["skip-decompress"] = String(plan.skipDecompress);
  } else if (plan.action === "resume") {
    outputs["preparation-name"] = `Resume release ${plan.requestId}`;
  } else {
    const action = plan.action === "dispatch"
      ? "dispatch"
      : `retry ${plan.runId}/${plan.attempt}`;
    outputs["preparation-name"] =
      `Prepare release ${plan.requestId}: ${action}`;
  }
  return outputs;
}

async function planArtifacts(
  repository: string,
  { commit, requestId, preparations }: {
    commit: string;
    requestId: number;
    preparations: ArtifactJob[];
  },
): Promise<ReleasePlan> {
  if (preparations.some((job) => job.conclusion !== "success")) {
    throw new Error(
      `Preparation request failed for release request ${requestId}.`,
    );
  }
  const retried = retryRecord(preparations);
  const runs = await getArtifactRuns(repository, commit);
  let waitingForRetry = false;
  for (const run of runs) {
    if (
      retried && run.id === retried.runId &&
      run.run_attempt <= retried.attempt
    ) {
      waitingForRetry = true;
      continue;
    }
    if (run.status !== "completed" || run.conclusion !== "success") continue;
    const artifacts = await getReleaseArtifacts(repository, run.id);
    if (artifacts) {
      return {
        action: "publish",
        commit,
        requestId,
        runId: run.id,
        ...artifacts,
      };
    }
  }
  if (waitingForRetry || runs.some((run) => run.status !== "completed")) {
    return { action: "none" };
  }
  if (runs.length === 0) {
    return preparations.length
      ? { action: "none" }
      : { action: "dispatch", commit, requestId };
  }
  if (retried) {
    throw new Error(
      `Artifact preparation failed after retrying run ${retried.runId}.`,
    );
  }
  return await retryPlan(repository, { runs, commit, requestId });
}

async function eventCommit(
  repository: string,
  { current, event }: { current: ArtifactRun; event: ReleaseEvent },
): Promise<string | undefined> {
  if (isReleaseRequest(current)) return current.head_sha;
  if (event.eventName !== "workflow_run" || !event.completedRunId) return;
  const completed = await artifactApi<ArtifactRun>(
    `repos/${repository}/actions/runs/${event.completedRunId}`,
  );
  return artifactCommit(repository, completed);
}

async function releaseHistory(
  repository: string,
  { commit, current }: { commit: string; current: ArtifactRun },
): Promise<ArtifactRun[]> {
  const pages = await artifactApi<{ workflow_runs: ArtifactRun[] }[]>(
    `repos/${repository}/actions/workflows/release.yml/runs?per_page=100`,
    { paged: true },
  );
  return [
    current,
    ...pages.flatMap((page) => page.workflow_runs).filter((run) =>
      run.id !== current.id
    ),
  ]
    .filter((run) => isReleaseRun(repository, run))
    // New completion handlers only resume requests; legacy handlers may hold retry records.
    .filter((run) =>
      isReleaseRequest(run) ? run.head_sha === commit : run.id === current.id ||
        run.display_title === `Release Test ${commit}`
    )
    .sort((a, b) => b.id - a.id);
}

function isReleaseRun(repository: string, run: ArtifactRun): boolean {
  return run.head_repository?.full_name === repository &&
    run.path.split("@")[0] === ".github/workflows/release.yml";
}

function isReleaseRequest(run: ArtifactRun): boolean {
  return ["push", "workflow_dispatch"].includes(run.event) &&
    run.head_branch === "release";
}

async function alreadyPublished(
  repository: string,
  commit: string,
): Promise<boolean> {
  const pages = await artifactApi<
    { tag_name: string; target_commitish: string; draft: boolean }[][]
  >(
    `repos/${repository}/releases?per_page=100`,
    { paged: true },
  );
  for (
    const release of pages.flat().filter((release) =>
      !release.draft && release.target_commitish === commit
    )
  ) {
    if (await tagCommit(release.tag_name) === commit) return true;
  }
  return false;
}

async function preparationHistory(
  repository: string,
  { history, requestId }: { history: ArtifactRun[]; requestId: number },
): Promise<ArtifactJob[]> {
  const jobs: ArtifactJob[] = [];
  // Job names are the durable journal, created before dispatch/rerun is attempted.
  for (const run of history) {
    jobs.push(
      ...await getArtifactJobs(repository, run.id, { allAttempts: true }),
    );
  }
  return jobs.filter((job) =>
    job.name.startsWith(`Prepare release ${requestId}: `) &&
    job.conclusion !== "skipped"
  );
}

async function retryPlan(
  repository: string,
  { runs, commit, requestId }: {
    runs: ArtifactRun[];
    commit: string;
    requestId: number;
  },
): Promise<ReleasePlan> {
  for (const run of runs) {
    const jobs = await getArtifactJobs(repository, run.id);
    if (!jobs.some(isSuccessfulTest)) continue;
    const job = preparationJob(run, jobs);
    return {
      action: "retry",
      commit,
      requestId,
      runId: run.id,
      attempt: run.run_attempt,
      jobId: job.id,
    };
  }
  throw new Error(
    `No successful test result exists for ${commit}. Publication stopped.`,
  );
}

function preparationJob(run: ArtifactRun, jobs: ArtifactJob[]): ArtifactJob {
  const exported = jobs.find((job) =>
    ["Upload", "Export Game"].includes(job.name)
  );
  if (!exported) {
    throw new Error(`Run ${run.id} has no artifact preparation job.`);
  }
  const packaging = jobs.find((job) => job.name === "Package Artifacts");
  return exported.conclusion === "success" && packaging &&
      packaging.conclusion !== "success"
    ? packaging
    : exported;
}

function retryRecord(
  jobs: ArtifactJob[],
): { runId: number; attempt: number } | undefined {
  for (const job of jobs) {
    const match = job.name.match(/: retry (\d+)\/(\d+)$/);
    if (match) return { runId: Number(match[1]), attempt: Number(match[2]) };
  }
}

async function tagCommit(name: string): Promise<string | undefined> {
  const tag = `refs/tags/${name}`;
  const remote = await readCommand("git", [
    "ls-remote",
    "--tags",
    "origin",
    tag,
    `${tag}^{}`,
  ]);
  const refs = new Map(
    remote.split(/\r?\n/).map((line) => {
      const [sha, ref] = line.split(/\s+/);
      return [ref, sha];
    }),
  );
  return refs.get(`${tag}^{}`) ?? refs.get(tag);
}
