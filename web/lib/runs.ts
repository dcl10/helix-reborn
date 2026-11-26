import { API_BASE, apiFetch } from "@/lib/api";
import type { Artifact } from "@/lib/eda";
import type { PipelineSpec } from "@/lib/pipelines";

/** Where a run is, as the API serializes it (camelCase). */
export type RunStatus = "queued" | "running" | "succeeded" | "failed";

/** The training worker's result, passed through by the API in snake_case. */
export interface TrainingResult {
  model_key: string;
  metadata_key: string;
  split_key: string;
  cv_metrics_key: string | null;
  cv_metrics: Record<string, { mean: number; std: number }> | null;
  best_params: Record<string, unknown> | null;
  train_rows: number;
  test_rows: number;
}

/** A training run (`RunResponse`). `spec` and `result` are snake_case; dates are ISO 8601 strings. */
export interface Run {
  id: string;
  pipelineId: string | null;
  pipelineName: string;
  status: RunStatus;
  progress: number | null;
  message: string | null;
  result: TrainingResult | null;
  error: { type: string; message: string } | null;
  spec: PipelineSpec;
  createdAt: string;
  startedAt: string | null;
  finishedAt: string | null;
}

/**
 * Whether a run may still change: queued or running.
 * @param status - the run's status
 * @returns whether it's in progress
 */
export function isActive(status: RunStatus): boolean {
  return status === "queued" || status === "running";
}

function runsPath(experimentId: string, runId?: string): string {
  const base = `/experiments/${encodeURIComponent(experimentId)}/runs`;
  return runId ? `${base}/${encodeURIComponent(runId)}` : base;
}

/**
 * Starts a run of a saved pipeline. The API answers 400 when the spec no longer fits the dataset, and 503 when the
 * training queue is unavailable.
 * @param experimentId - the experiment ID
 * @param pipelineId - the pipeline to run
 * @returns the queued run
 */
export function startRun(
  experimentId: string,
  pipelineId: string,
): Promise<Run> {
  return apiFetch<Run>(
    `/experiments/${encodeURIComponent(experimentId)}/pipelines/${encodeURIComponent(pipelineId)}/runs`,
    { method: "POST" },
  );
}

/**
 * Lists an experiment's runs, newest first.
 * @param experimentId - the experiment ID
 * @returns the runs
 */
export function listRuns(experimentId: string): Promise<Run[]> {
  return apiFetch<Run[]>(runsPath(experimentId));
}

/**
 * Gets one run.
 * @param experimentId - the experiment ID
 * @param runId - the run ID
 * @returns the run
 */
export function getRun(experimentId: string, runId: string): Promise<Run> {
  return apiFetch<Run>(runsPath(experimentId, runId));
}

/**
 * Lists a run's files: its model, metadata, split and CV metrics, once it has succeeded.
 * @param experimentId - the experiment ID
 * @param runId - the run ID
 * @returns the artifacts
 */
export function listRunArtifacts(
  experimentId: string,
  runId: string,
): Promise<Artifact[]> {
  const query = new URLSearchParams({ sourceType: "run", sourceId: runId });
  return apiFetch<Artifact[]>(
    `/experiments/${encodeURIComponent(experimentId)}/artifacts?${query}`,
  );
}

/**
 * The URL of a run's server-sent events stream, through the authenticated proxy.
 * @param experimentId - the experiment ID
 * @param runId - the run ID
 * @returns the URL
 */
export function runEventsUrl(experimentId: string, runId: string): string {
  return `${API_BASE}${runsPath(experimentId, runId)}/events`;
}

/**
 * The run's headline metric: mean cross-validated accuracy for classification, R² for regression.
 * @param run - the run
 * @returns the metric's label and value, or null when the run has no cross-validation metrics
 */
export function headlineMetric(
  run: Run,
): { label: string; value: number } | null {
  const [key, label] =
    run.spec.problem_type === "classification"
      ? ["accuracy", "Accuracy"]
      : ["r2", "R²"];
  const metric = run.result?.cv_metrics?.[key];
  return metric ? { label, value: metric.mean } : null;
}

/**
 * Formats a metric to 3 decimal places, or `—` when there isn't one.
 * @param value - the metric
 * @returns the text
 */
export function formatMetric(value: number | null | undefined): string {
  return value === null || value === undefined ? "—" : value.toFixed(3);
}

/**
 * How long a run has been training: from starting to finishing, or to `now` while it runs.
 * @param run - the run
 * @param now - the current time
 * @returns e.g. `45 s`, `3 min 12 s`, `1 h 5 min`; `—` before it has started
 */
export function formatDuration(
  run: Pick<Run, "startedAt" | "finishedAt">,
  now: Date = new Date(),
): string {
  if (!run.startedAt) return "—";
  const end = run.finishedAt ? new Date(run.finishedAt) : now;
  const seconds = Math.max(
    0,
    Math.round((end.getTime() - new Date(run.startedAt).getTime()) / 1000),
  );
  if (seconds < 60) return `${seconds} s`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes} min ${seconds % 60} s`;
  return `${Math.floor(minutes / 60)} h ${minutes % 60} min`;
}
