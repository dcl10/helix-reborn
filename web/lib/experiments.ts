import { z } from "zod";

import { apiFetch } from "@/lib/api";

export { ApiError, isNotFound, retryUnlessClientError } from "@/lib/api";

/** A dataset column's inferred type, from the API's CSV inspection. */
export type DatasetColumnType = "numeric" | "text" | "empty";

/** One column of an uploaded dataset. */
export interface DatasetColumn {
  name: string;
  inferredType: DatasetColumnType;
  missingCount: number;
}

/** An experiment's one uploaded dataset, as the API returns it. */
export interface Dataset {
  id: string;
  fileName: string;
  sizeBytes: number;
  rowCount: number;
  createdAt: string;
  columns: DatasetColumn[];
}

/** An experiment as `GET /api/experiments` and friends return it. Dates are ISO 8601 strings. */
export interface Experiment {
  id: string;
  name: string;
  description: string | null;
  ownerSub: string;
  createdAt: string;
  updatedAt: string;
  dataset: Dataset | null;
}

/** A tab in the experiment layout's navigation. */
export type ExperimentTab =
  "data" | "eda" | "pipelines" | "runs" | "evaluations" | "predict" | "files";

/**
 * Builds the path to an experiment, or one of its tabs.
 * @param id - the experiment ID
 * @param tab - the tab to link to; omitted for the experiment's own path
 * @returns the path
 */
export function experimentPath(id: string, tab?: ExperimentTab): string {
  return tab ? `/experiments/${id}/${tab}` : `/experiments/${id}`;
}

/** The new/edit experiment form's fields and their validation. The API checks the same rules again. */
export const experimentSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, "Enter a name.")
    .max(100, "That name is too long."),
  description: z
    .string()
    .trim()
    .max(1000, "That description is too long.")
    .optional()
    .or(z.literal("")),
});

/** Values the new/edit experiment form submits. */
export type ExperimentValues = z.infer<typeof experimentSchema>;

/** Largest CSV upload accepted, checked client-side before the API's own 50 MB limit. */
export const MAX_DATASET_BYTES = 50 * 1024 * 1024;

/**
 * Lists the caller's experiments, newest first.
 * @returns the experiments
 */
export function listExperiments(): Promise<Experiment[]> {
  return apiFetch<Experiment[]>("/experiments");
}

/**
 * Gets one experiment.
 * @param id - the experiment ID
 * @returns the experiment
 */
export function getExperiment(id: string): Promise<Experiment> {
  return apiFetch<Experiment>(`/experiments/${encodeURIComponent(id)}`);
}

/**
 * Creates an experiment owned by the caller.
 * @param values - the form's values
 * @returns the new experiment
 */
export function createExperiment(
  values: ExperimentValues,
): Promise<Experiment> {
  return apiFetch<Experiment>("/experiments", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      name: values.name,
      description: values.description || null,
    }),
  });
}

/** Fields to update an experiment; an id to identify which. */
export interface UpdateExperimentInput extends ExperimentValues {
  id: string;
}

/**
 * Updates an experiment's name and description.
 * @param input - the experiment ID and its new values
 * @returns the updated experiment
 */
export function updateExperiment({
  id,
  name,
  description,
}: UpdateExperimentInput): Promise<Experiment> {
  return apiFetch<Experiment>(`/experiments/${encodeURIComponent(id)}`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ name, description: description || null }),
  });
}

/**
 * Deletes an experiment and everything stored under it.
 * @param id - the experiment ID
 * @returns nothing
 */
export function deleteExperiment(id: string): Promise<void> {
  return apiFetch<void>(`/experiments/${encodeURIComponent(id)}`, {
    method: "DELETE",
  });
}

/**
 * Gets an experiment's dataset.
 * @param experimentId - the experiment ID
 * @returns the dataset
 */
export function getDataset(experimentId: string): Promise<Dataset> {
  return apiFetch<Dataset>(
    `/experiments/${encodeURIComponent(experimentId)}/dataset`,
  );
}

/**
 * Uploads an experiment's dataset.
 * @param experimentId - the experiment ID
 * @param file - the CSV file
 * @returns the new dataset
 */
export function uploadDataset(
  experimentId: string,
  file: File,
): Promise<Dataset> {
  const body = new FormData();
  body.append("file", file);

  return apiFetch<Dataset>(
    `/experiments/${encodeURIComponent(experimentId)}/dataset`,
    { method: "POST", body },
  );
}
