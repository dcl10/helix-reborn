import type { Artifact } from "@/lib/eda";
import type { Evaluation } from "@/lib/evaluations";

/**
 * An evaluation for tests, queued with the default options unless overridden.
 * @param overrides - fields to change
 * @returns the evaluation
 */
export function makeEvaluation(
  overrides: Partial<Evaluation> = {},
): Evaluation {
  return {
    id: "ev-1",
    runId: "run-1",
    pipelineName: "RF baseline",
    status: "queued",
    progress: null,
    message: null,
    options: {
      plots: true,
      global_methods: ["permutation", "shap"],
      permutation_repeats: 10,
      local_methods: [],
      local_max_samples: 50,
      ensemble_methods: [],
      num_features_to_plot: 10,
      fuzzy: null,
      plot_options: null,
    },
    metrics: null,
    error: null,
    createdAt: "2026-11-27T10:00:00Z",
    startedAt: null,
    finishedAt: null,
    ...overrides,
  };
}

/**
 * An evaluation artifact for tests.
 * @param fileName - its file name, also its ID and title
 * @param kind - its kind
 * @returns the artifact
 */
export function makeArtifact(
  fileName: string,
  kind: Artifact["kind"] = "evaluationPlot",
): Artifact {
  return {
    id: fileName,
    kind,
    title: fileName,
    contentType: "application/octet-stream",
    sizeBytes: 1,
    sourceType: "evaluation",
    sourceId: "ev-1",
    createdAt: "2026-11-27T10:05:00Z",
    fileName,
  };
}

/**
 * A JSON fetch Response.
 * @param body - the body, serialized as JSON
 * @param status - the status code
 * @returns the response
 */
export function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}
