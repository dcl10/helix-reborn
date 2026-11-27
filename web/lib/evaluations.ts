import { z } from "zod";

import { API_BASE, apiFetch } from "@/lib/api";
import { type Artifact, type PlotOptions, plotOptionsSchema } from "@/lib/eda";
import type { RunStatus } from "@/lib/runs";

/** Where an evaluation is: the same states as a run. */
export type EvaluationStatus = RunStatus;

/** Fuzzy interpretation options, snake_case as the API and worker take them. */
export interface FuzzyOptions {
  number_fuzzy_features: number;
  granular_features: boolean;
  number_clusters: number;
  cluster_names: string[];
  number_rules: number;
}

/** What the evaluation worker computes and plots, snake_case as the API and worker take them. */
export interface EvaluationOptions {
  plots: boolean;
  global_methods: ("permutation" | "shap")[];
  permutation_repeats: number;
  local_methods: ("lime" | "shap")[];
  local_max_samples: number;
  ensemble_methods: ("mean" | "majority_vote")[];
  num_features_to_plot: number;
  fuzzy: FuzzyOptions | null;
  plot_options: PlotOptions | null;
}

/** An evaluation (`EvaluationResponse`). `options` is snake_case; dates are ISO 8601 strings. */
export interface Evaluation {
  id: string;
  runId: string;
  pipelineName: string;
  status: EvaluationStatus;
  progress: number | null;
  message: string | null;
  options: EvaluationOptions;
  /** The test-set metrics, once it has succeeded. */
  metrics: Record<string, number | null> | null;
  error: { type: string; message: string } | null;
  createdAt: string;
  startedAt: string | null;
  finishedAt: string | null;
}

/** The evaluation's `metrics.json`: every metric for the training and test sets. */
export interface MetricsFile {
  train: Record<string, number | null>;
  test: Record<string, number | null>;
}

function evaluationsPath(experimentId: string, evaluationId?: string): string {
  const base = `/experiments/${encodeURIComponent(experimentId)}/evaluations`;
  return evaluationId ? `${base}/${encodeURIComponent(evaluationId)}` : base;
}

/**
 * Starts an evaluation of a succeeded run's model. The API answers 400 for invalid options, 409 when the run hasn't
 * succeeded, and 503 when the evaluation queue is unavailable.
 * @param experimentId - the experiment ID
 * @param runId - the run whose model to evaluate
 * @param options - the options, snake_case
 * @returns the queued evaluation
 */
export function startEvaluation(
  experimentId: string,
  runId: string,
  options: EvaluationOptions,
): Promise<Evaluation> {
  return apiFetch<Evaluation>(
    `/experiments/${encodeURIComponent(experimentId)}/runs/${encodeURIComponent(runId)}/evaluations`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ options }),
    },
  );
}

/**
 * Lists an experiment's evaluations, newest first.
 * @param experimentId - the experiment ID
 * @param runId - when given, only this run's evaluations
 * @returns the evaluations
 */
export function listEvaluations(
  experimentId: string,
  runId?: string,
): Promise<Evaluation[]> {
  const query = runId ? `?${new URLSearchParams({ runId })}` : "";
  return apiFetch<Evaluation[]>(`${evaluationsPath(experimentId)}${query}`);
}

/**
 * Gets one evaluation.
 * @param experimentId - the experiment ID
 * @param evaluationId - the evaluation ID
 * @returns the evaluation
 */
export function getEvaluation(
  experimentId: string,
  evaluationId: string,
): Promise<Evaluation> {
  return apiFetch<Evaluation>(evaluationsPath(experimentId, evaluationId));
}

/**
 * Lists an evaluation's files: its metrics, plots and tables, once it has succeeded.
 * @param experimentId - the experiment ID
 * @param evaluationId - the evaluation ID
 * @returns the artifacts
 */
export function listEvaluationArtifacts(
  experimentId: string,
  evaluationId: string,
): Promise<Artifact[]> {
  const query = new URLSearchParams({
    sourceType: "evaluation",
    sourceId: evaluationId,
  });
  return apiFetch<Artifact[]>(
    `/experiments/${encodeURIComponent(experimentId)}/artifacts?${query}`,
  );
}

/**
 * The URL of an evaluation's server-sent events stream, through the authenticated proxy.
 * @param experimentId - the experiment ID
 * @param evaluationId - the evaluation ID
 * @returns the URL
 */
export function evaluationEventsUrl(
  experimentId: string,
  evaluationId: string,
): string {
  return `${API_BASE}${evaluationsPath(experimentId, evaluationId)}/events`;
}

/**
 * The evaluation's headline test metric: accuracy for classification, R² for regression.
 * @param evaluation - the evaluation
 * @returns the metric's label and value, or null before it has metrics
 */
export function headlineTestMetric(
  evaluation: Pick<Evaluation, "metrics">,
): { label: string; value: number } | null {
  const metrics = evaluation.metrics;
  if (!metrics) return null;
  if (typeof metrics.accuracy === "number")
    return { label: "Accuracy", value: metrics.accuracy };
  if (typeof metrics.r2 === "number") return { label: "R²", value: metrics.r2 };
  return null;
}

/** The default fuzzy cluster names, lowest first. */
export const DEFAULT_CLUSTER_NAMES = [
  "very low",
  "low",
  "medium",
  "high",
  "very high",
];

/**
 * Splits the cluster names input: comma-separated, trimmed, empty entries dropped.
 * @param text - the input
 * @returns the names
 */
export function parseClusterNames(text: string): string[] {
  return text
    .split(",")
    .map((name) => name.trim())
    .filter((name) => name.length > 0);
}

/** An integer field with limits, with messages for the form. */
function intIn(min: number, max?: number) {
  const base = z
    .number({ error: "Enter a number." })
    .int("Enter a whole number.")
    .min(min, `Use at least ${min}.`);
  return max === undefined ? base : base.max(max, `Use at most ${max}.`);
}

/**
 * The new evaluation form's fields and validation: the run, each method as a checkbox, their settings, fuzzy (only
 * checked when enabled), and plot options. The dependency rules (fuzzy needs a local and an ensemble method;
 * ensembles need a global method; one cluster name per cluster) are the API's own.
 */
export const evaluationFormSchema = z
  .object({
    runId: z.string().min(1, "Choose a run."),
    plots: z.boolean(),
    globalPermutation: z.boolean(),
    globalShap: z.boolean(),
    permutationRepeats: intIn(1, 100),
    localLime: z.boolean(),
    localShap: z.boolean(),
    localMaxSamples: intIn(1, 1000),
    ensembleMean: z.boolean(),
    ensembleMajorityVote: z.boolean(),
    numFeaturesToPlot: intIn(1, 50),
    fuzzyEnabled: z.boolean(),
    fuzzy: z.object({
      numberFuzzyFeatures: z.number(),
      granularFeatures: z.boolean(),
      numberClusters: z.number(),
      clusterNames: z.string(),
      numberRules: z.number(),
    }),
    plotOptions: plotOptionsSchema,
  })
  .superRefine((values, ctx) => {
    const issue = (path: (string | number)[], message: string) =>
      ctx.addIssue({ code: "custom", path, message });

    const hasGlobal = values.globalPermutation || values.globalShap;
    const hasLocal = values.localLime || values.localShap;
    const hasEnsemble = values.ensembleMean || values.ensembleMajorityVote;

    if (hasEnsemble && !hasGlobal) {
      issue(
        ["ensembleMean"],
        "Ensemble importance needs at least one global method.",
      );
    }

    if (!values.fuzzyEnabled) return;

    if (!hasLocal || !hasEnsemble) {
      issue(
        ["fuzzyEnabled"],
        "Fuzzy interpretation needs at least one local method and at least one ensemble method.",
      );
    }

    const { fuzzy } = values;
    const checkInt = (
      key: keyof typeof fuzzy,
      value: number,
      min: number,
      max?: number,
    ) => {
      if (
        !Number.isInteger(value) ||
        value < min ||
        (max !== undefined && value > max)
      ) {
        issue(
          ["fuzzy", key],
          max === undefined
            ? `Use a whole number of at least ${min}.`
            : `Use a whole number from ${min} to ${max}.`,
        );
      }
    };
    checkInt("numberFuzzyFeatures", fuzzy.numberFuzzyFeatures, 1);
    checkInt("numberClusters", fuzzy.numberClusters, 2, 10);
    checkInt("numberRules", fuzzy.numberRules, 1);

    const names = parseClusterNames(fuzzy.clusterNames);
    if (
      Number.isInteger(fuzzy.numberClusters) &&
      names.length !== fuzzy.numberClusters
    ) {
      issue(
        ["fuzzy", "clusterNames"],
        `Enter ${fuzzy.numberClusters} names, one per cluster (you have ${names.length}).`,
      );
    }
  });

/** Values of the new evaluation form. */
export type EvaluationFormValues = z.infer<typeof evaluationFormSchema>;

/**
 * The form's starting values: the API's defaults (plots and both global methods on), for the given run.
 * @param runId - the run to preselect, or `""`
 * @returns the values
 */
export function defaultEvaluationValues(runId = ""): EvaluationFormValues {
  return {
    runId,
    plots: true,
    globalPermutation: true,
    globalShap: true,
    permutationRepeats: 10,
    localLime: false,
    localShap: false,
    localMaxSamples: 50,
    ensembleMean: false,
    ensembleMajorityVote: false,
    numFeaturesToPlot: 10,
    fuzzyEnabled: false,
    fuzzy: {
      numberFuzzyFeatures: 10,
      granularFeatures: true,
      numberClusters: 5,
      clusterNames: DEFAULT_CLUSTER_NAMES.join(", "),
      numberRules: 5,
    },
    plotOptions: {},
  };
}

/**
 * Builds the API's options from the form's values, in snake_case. Plot options left empty are left out, so the
 * worker's defaults apply.
 * @param values - the form's values
 * @returns the options
 */
export function toEvaluationOptions(
  values: EvaluationFormValues,
): EvaluationOptions {
  const plotOptions = Object.fromEntries(
    Object.entries(values.plotOptions).filter(
      ([, value]) => value !== undefined && value !== "",
    ),
  ) as PlotOptions;

  return {
    plots: values.plots,
    global_methods: [
      ...(values.globalPermutation ? (["permutation"] as const) : []),
      ...(values.globalShap ? (["shap"] as const) : []),
    ],
    permutation_repeats: values.permutationRepeats,
    local_methods: [
      ...(values.localLime ? (["lime"] as const) : []),
      ...(values.localShap ? (["shap"] as const) : []),
    ],
    local_max_samples: values.localMaxSamples,
    ensemble_methods: [
      ...(values.ensembleMean ? (["mean"] as const) : []),
      ...(values.ensembleMajorityVote ? (["majority_vote"] as const) : []),
    ],
    num_features_to_plot: values.numFeaturesToPlot,
    fuzzy: values.fuzzyEnabled
      ? {
          number_fuzzy_features: values.fuzzy.numberFuzzyFeatures,
          granular_features: values.fuzzy.granularFeatures,
          number_clusters: values.fuzzy.numberClusters,
          cluster_names: parseClusterNames(values.fuzzy.clusterNames),
          number_rules: values.fuzzy.numberRules,
        }
      : null,
    plot_options: Object.keys(plotOptions).length > 0 ? plotOptions : null,
  };
}

/** A section of the evaluation's plots. */
export interface PlotGroup {
  title: string;
  plots: Artifact[];
}

/** Plot sections, in display order, and the file-name prefixes that belong to each. */
const PLOT_GROUPS: { title: string; prefixes: string[] }[] = [
  {
    title: "Performance",
    prefixes: ["confusion_", "roc_", "precision_", "predicted_", "residual"],
  },
  { title: "Global importance", prefixes: ["global_"] },
  { title: "Local importance", prefixes: ["local_"] },
  { title: "Ensemble", prefixes: ["ensemble_"] },
  { title: "Fuzzy", prefixes: ["fuzzy_"] },
];

/**
 * Groups an evaluation's plots into sections by file name (Performance, Global importance, Local importance,
 * Ensemble, Fuzzy, then Other for anything unrecognised), leaving out empty sections and non-plot artifacts.
 * @param artifacts - the evaluation's artifacts, in the order to show them
 * @returns the non-empty groups, in display order
 */
export function groupEvaluationPlots(artifacts: Artifact[]): PlotGroup[] {
  const groups = PLOT_GROUPS.map(({ title }) => ({
    title,
    plots: [] as Artifact[],
  }));
  const other: Artifact[] = [];

  for (const artifact of artifacts) {
    if (artifact.kind !== "evaluationPlot") continue;
    const index = PLOT_GROUPS.findIndex(({ prefixes }) =>
      prefixes.some((prefix) => artifact.fileName.startsWith(prefix)),
    );
    if (index === -1) other.push(artifact);
    else groups[index].plots.push(artifact);
  }

  return [...groups, { title: "Other", plots: other }].filter(
    (group) => group.plots.length > 0,
  );
}

/** A parsed CSV: its header, up to the requested number of rows, and whether more were dropped. */
export interface CsvTable {
  header: string[];
  rows: string[][];
  truncated: boolean;
}

/**
 * Parses CSV text (RFC 4180: quoted fields may hold commas, newlines and `""` for a quote), keeping the header and
 * at most `maxRows` rows.
 * @param text - the CSV
 * @param maxRows - the most data rows to keep
 * @returns the table
 */
export function parseCsv(text: string, maxRows: number): CsvTable {
  const records: string[][] = [];
  let record: string[] = [];
  let field = "";
  let quoted = false;
  let i = 0;

  const endField = () => {
    record.push(field);
    field = "";
  };
  const endRecord = () => {
    endField();
    records.push(record);
    record = [];
  };

  while (i < text.length) {
    const char = text[i];
    if (quoted) {
      if (char === '"' && text[i + 1] === '"') {
        field += '"';
        i += 2;
        continue;
      }
      if (char === '"') quoted = false;
      else field += char;
      i++;
      continue;
    }

    if (char === '"') quoted = true;
    else if (char === ",") endField();
    else if (char === "\n" || char === "\r") {
      if (char === "\r" && text[i + 1] === "\n") i++;
      endRecord();
      // One more than needed tells us whether rows were dropped.
      if (records.length > maxRows + 1) break;
    } else field += char;
    i++;
  }
  if (field !== "" || record.length > 0) endRecord();

  const [header = [], ...rows] = records;
  return {
    header,
    rows: rows.slice(0, maxRows),
    truncated: rows.length > maxRows,
  };
}
