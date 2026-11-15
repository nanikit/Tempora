import { assertEquals } from "@std/assert";
import { selectReleaseArtifacts } from "../.github/scripts/artifact-runs.ts";
import { releaseArtifacts } from "./release-boundary.ts";

Deno.test("Incomplete or expired downloads are not published", () => {
  const artifacts = releaseArtifacts();
  for (let index = 0; index < artifacts.length; index++) {
    assertEquals(
      selectReleaseArtifacts(artifacts.filter((_, i) => i !== index)),
      undefined,
      `Missing ${artifacts[index].name}`,
    );
    assertEquals(
      selectReleaseArtifacts(artifacts.map((artifact, i) => ({
        ...artifact,
        expired: i === index,
      }))),
      undefined,
      `Expired ${artifacts[index].name}`,
    );
  }
  assertEquals(
    selectReleaseArtifacts([{
      id: 201,
      name: "release-artifacts",
      expired: true,
    }]),
    undefined,
  );
});

Deno.test("All OS downloads must have the same version", () => {
  const artifacts = releaseArtifacts();
  artifacts[1].name = "Tempora-v1.5.5-Linux.zip";
  assertEquals(selectReleaseArtifacts(artifacts), undefined);
});

Deno.test("Rebuilt downloads use the latest prepared version", () => {
  const latest = releaseArtifacts({
    version: "1.6.0-rc.2+build.7",
    firstId: 301,
  });
  assertEquals(
    selectReleaseArtifacts([
      ...releaseArtifacts().slice(0, 3),
      ...latest,
      { id: 305, name: "logs", expired: false },
      { id: 200, name: "release-artifacts", expired: false },
    ]),
    { artifactIds: [301, 302, 303, 304], skipDecompress: true },
  );
});
