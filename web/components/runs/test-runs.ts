import type { Run, TrainingResult } from "@/lib/runs";

/**
 * A run for tests, regression unless overridden.
 * @param overrides - fields to change
 * @returns the run
 */
export function makeRun(overrides: Partial<Run> = {}): Run {
  return {
    id: "run-1",
    pipelineId: "p-1",
    pipelineName: "RF baseline",
    status: "queued",
    progress: null,
    message: null,
    result: null,
    error: null,
    spec: {
      problem_type: "regression",
      target_column: "y",
      feature_columns: ["a", "b"],
      id_column: null,
      split: { test_size: 0.2, random_seed: 42, cv_folds: 5 },
      steps: [{ step: "simple_imputer", params: { strategy: "median" } }],
      model: {
        step: "random_forest_regressor",
        params: { n_estimators: 200, max_depth: null },
      },
      search: null,
    },
    createdAt: "2026-11-26T10:00:00Z",
    startedAt: null,
    finishedAt: null,
    ...overrides,
  };
}

/** A succeeded run's result with CV metrics for the given problem type. */
export function resultFor(
  problemType: "classification" | "regression",
): TrainingResult {
  return {
    model_key: "experiments/e/runs/run-1/model.onnx",
    metadata_key: "experiments/e/runs/run-1/metadata.json",
    split_key: "experiments/e/runs/run-1/split.json",
    cv_metrics_key: "experiments/e/runs/run-1/cv_metrics.json",
    cv_metrics:
      problemType === "classification"
        ? {
            accuracy: { mean: 0.91234, std: 0.02 },
            f1_weighted: { mean: 0.9, std: 0.03 },
          }
        : { r2: { mean: 0.87654, std: 0.01 }, mae: { mean: 1.5, std: 0.2 } },
    best_params: { n_estimators: 100, max_depth: null },
    train_rows: 80,
    test_rows: 20,
  };
}

/**
 * A fetch Response whose body streams the given chunks of SSE text.
 * @param chunks - the text, in pieces
 * @returns the response
 */
export function sseResponse(chunks: string[]): Response {
  const encoder = new TextEncoder();
  const body = new ReadableStream<Uint8Array>({
    start(controller) {
      for (const chunk of chunks) controller.enqueue(encoder.encode(chunk));
      controller.close();
    },
  });
  return new Response(body, {
    status: 200,
    headers: { "Content-Type": "text/event-stream" },
  });
}

/**
 * One SSE event's text.
 * @param event - the event type
 * @param data - its data, serialized as JSON
 * @returns the text
 */
export function sseEvent(event: string, data: unknown): string {
  return `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`;
}
