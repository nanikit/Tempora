import { readCommand } from "../../scripts/process.ts";

export interface ArtifactRun {
  id: number;
  head_sha: string;
  head_branch: string;
  event: string;
  display_title: string;
  status: string;
  conclusion: string | null;
  run_attempt: number;
  path: string;
  created_at: string;
  head_repository: { full_name: string };
}

export interface ArtifactJob {
  id: number;
  name: string;
  conclusion: string | null;
  steps?: { name: string }[];
}

interface ReleaseArtifact {
  id: number;
  name: string;
  expired: boolean;
}

export function artifactApi<T>(
  path: string,
  options?: { paged?: boolean },
): Promise<T>;
export function artifactApi(
  path: string,
  options: { method: "POST"; fields?: string[] },
): Promise<void>;
export async function artifactApi(
  path: string,
  options: { method?: string; paged?: boolean; fields?: string[] } = {},
): Promise<unknown> {
  const args = [
    "api",
    path,
    "--method",
    options.method ?? "GET",
    "-H",
    "X-GitHub-Api-Version: 2026-03-10",
  ];
  if (options.paged) args.push("--paginate", "--slurp");
  for (const field of options.fields ?? []) args.push("-f", field);
  const response = await readCommand("gh", args);
  return response ? JSON.parse(response) : undefined;
}

export async function getArtifactRuns(
  repository: string,
  commit: string,
): Promise<ArtifactRun[]> {
  const path = `repos/${repository}/actions/workflows/test.yml/runs`;
  const push = await artifactApi<{ workflow_runs: ArtifactRun[] }[]>(
    `${path}?event=push&head_sha=${commit}&per_page=100`,
    { paged: true },
  );
  const manual = await artifactApi<{ workflow_runs: ArtifactRun[] }[]>(
    `${path}?event=workflow_dispatch&per_page=100`,
    { paged: true },
  );
  const runs = [...push, ...manual].flatMap((page) => page.workflow_runs);
  const commits = await Promise.all(
    runs.map((run) => artifactCommit(repository, run)),
  );
  return runs.filter((_, index) => commits[index] === commit)
    .sort((a, b) => b.id - a.id);
}

export async function artifactCommit(
  repository: string,
  run: ArtifactRun,
): Promise<string | undefined> {
  if (
    run.head_repository?.full_name !== repository ||
    run.path.split("@")[0] !== ".github/workflows/test.yml"
  ) return;
  if (run.event === "push" && ["main", "master"].includes(run.head_branch)) {
    return run.head_sha;
  }
  if (run.event === "workflow_dispatch" && run.head_branch === "main") {
    return dispatchedCommit(
      run,
      await getArtifactJobs(repository, run.id, { allAttempts: true }),
    );
  }
}

export function isSuccessfulTest(job: ArtifactJob): boolean {
  return ["Test", "gdUnit4"].includes(job.name) && job.conclusion === "success";
}

export async function getArtifactJobs(
  repository: string,
  runId: number,
  options: { allAttempts?: boolean } = {},
): Promise<ArtifactJob[]> {
  const pages = await artifactApi<{ jobs: ArtifactJob[] }[]>(
    `repos/${repository}/actions/runs/${runId}/jobs?filter=all&per_page=100`,
    { paged: true },
  );
  const jobs = pages.flatMap((page) => page.jobs);
  if (options.allAttempts) return jobs;
  return [...Map.groupBy(jobs, (job) => job.name).values()]
    .map((attempts) => attempts.sort((a, b) => b.id - a.id)[0]);
}

export async function getReleaseArtifact(
  repository: string,
  runId: number,
): Promise<ReleaseArtifact | undefined> {
  const pages = await artifactApi<{ artifacts: ReleaseArtifact[] }[]>(
    `repos/${repository}/actions/runs/${runId}/artifacts?per_page=100`,
    { paged: true },
  );
  return pages.flatMap((page) => page.artifacts)
    .find((artifact) =>
      artifact.name === "release-artifacts" && !artifact.expired
    );
}

function dispatchedCommit(
  run: ArtifactRun,
  jobs: ArtifactJob[],
): string | undefined {
  const source = jobs.flatMap((job) => job.steps ?? [])
    .map((step) => step.name.match(/^Checkout source ([0-9a-f]{40})$/)?.[1])
    .find((commit) => commit !== undefined);
  if (source) return source;
  // Runs created before source steps were named stored the input in their title.
  if (jobs.some((job) => job.name === "gdUnit4")) {
    return run.display_title.match(/^Test ([0-9a-f]{40})$/)?.[1];
  }
}
