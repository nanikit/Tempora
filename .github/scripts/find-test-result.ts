import { required } from "../../scripts/process.ts";
import { validateCommit, writeOutputs } from "./actions.ts";
import { getArtifactJobs, getArtifactRuns } from "./artifact-runs.ts";

if (import.meta.main) {
  await findTestResult(
    required(Deno.args[0], "repository"),
    required(Deno.args[1], "commit"),
    { currentRunId: Number(required(Deno.args[2], "current run ID")) },
  );
}

export async function findTestResult(
  repository: string,
  commit: string,
  options: { currentRunId: number },
): Promise<void> {
  validateCommit(commit);
  for (const run of await getArtifactRuns(repository, commit)) {
    if (run.id === options.currentRunId && run.run_attempt === 1) continue;
    const jobs = await getArtifactJobs(repository, run.id, {
      allAttempts: true,
    });
    if (
      jobs.some((job) => job.name === "gdUnit4" && job.conclusion === "success")
    ) {
      await writeOutputs({ "test-passed": "true", "run-id": run.id });
      console.log(`Reusing successful tests from run ${run.id} for ${commit}.`);
      return;
    }
  }
}
