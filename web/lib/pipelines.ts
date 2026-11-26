import { z } from "zod";

import { apiFetch } from "@/lib/api";
import type { DatasetColumn } from "@/lib/experiments";
import {
  describeFields,
  type FieldDescriptor,
  fieldZod,
  type JsonSchema,
  paramsToForm,
  paramsZod,
} from "@/lib/json-schema-form";

/** What a catalog step does. */
export type StepKind = "preprocessor" | "feature_selector" | "model";

/** Classification or regression. */
export type ProblemType = "classification" | "regression";

/** One step in the pipeline catalog (`GET /api/pipeline-catalog`, generated from helix-core). */
export interface CatalogStep {
  id: string;
  kind: StepKind;
  label: string;
  description: string;
  problem_types: ProblemType[];
  params_schema: JsonSchema;
  /** A model's default search grid (unprefixed param → candidates); null for other steps. */
  search_grid: Record<string, unknown[]> | null;
}

/** The pipeline catalog. */
export interface PipelineCatalog {
  schema_version: number;
  steps: CatalogStep[];
}

/** One step of a spec: a catalog id and its params, snake_case. */
export interface StepSpec {
  step: string;
  params: Record<string, unknown>;
}

/** A pipeline spec, exactly as the API stores it and the training worker reads it: snake_case. */
export interface PipelineSpec {
  problem_type: ProblemType;
  target_column: string;
  feature_columns: string[];
  id_column: string | null;
  split: { test_size: number; random_seed: number; cv_folds: number | null };
  steps: StepSpec[];
  model: StepSpec;
  search: {
    method: "grid" | "random";
    cv_folds: number;
    n_iter: number | null;
    param_grid: Record<string, unknown[]> | null;
  } | null;
}

/** A saved pipeline. Dates are ISO 8601 strings. */
export interface Pipeline {
  id: string;
  name: string;
  spec: PipelineSpec;
  createdAt: string;
  updatedAt: string;
}

/** What `POST`/`PUT …/pipelines` take. */
export interface SavePipelineInput {
  name: string;
  spec: PipelineSpec;
}

/**
 * Gets the pipeline catalog. It only changes on deploy, so callers cache it for the session.
 * @returns the catalog
 */
export function getPipelineCatalog(): Promise<PipelineCatalog> {
  return apiFetch<PipelineCatalog>("/pipeline-catalog");
}

function pipelinesPath(experimentId: string, pipelineId?: string): string {
  const base = `/experiments/${encodeURIComponent(experimentId)}/pipelines`;
  return pipelineId ? `${base}/${encodeURIComponent(pipelineId)}` : base;
}

/**
 * Lists an experiment's pipelines, by name.
 * @param experimentId - the experiment ID
 * @returns the pipelines
 */
export function listPipelines(experimentId: string): Promise<Pipeline[]> {
  return apiFetch<Pipeline[]>(pipelinesPath(experimentId));
}

/**
 * Gets one pipeline.
 * @param experimentId - the experiment ID
 * @param pipelineId - the pipeline ID
 * @returns the pipeline
 */
export function getPipeline(
  experimentId: string,
  pipelineId: string,
): Promise<Pipeline> {
  return apiFetch<Pipeline>(pipelinesPath(experimentId, pipelineId));
}

/**
 * Saves a new pipeline. A 400's `errors.spec` lists every problem with the spec.
 * @param experimentId - the experiment ID
 * @param input - the name and spec
 * @returns the saved pipeline
 */
export function createPipeline(
  experimentId: string,
  input: SavePipelineInput,
): Promise<Pipeline> {
  return apiFetch<Pipeline>(pipelinesPath(experimentId), {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(input),
  });
}

/**
 * Replaces a pipeline's name and spec.
 * @param experimentId - the experiment ID
 * @param pipelineId - the pipeline ID
 * @param input - the new name and spec
 * @returns the updated pipeline
 */
export function updatePipeline(
  experimentId: string,
  pipelineId: string,
  input: SavePipelineInput,
): Promise<Pipeline> {
  return apiFetch<Pipeline>(pipelinesPath(experimentId, pipelineId), {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(input),
  });
}

/**
 * Deletes a pipeline. Its runs keep their own copy of the spec.
 * @param experimentId - the experiment ID
 * @param pipelineId - the pipeline ID
 * @returns nothing
 */
export function deletePipeline(
  experimentId: string,
  pipelineId: string,
): Promise<void> {
  return apiFetch<void>(pipelinesPath(experimentId, pipelineId), {
    method: "DELETE",
  });
}

/**
 * The catalog's steps of the given kinds that support a problem type, in catalog order.
 * @param catalog - the catalog
 * @param kinds - the kinds wanted
 * @param problemType - the problem type, or undefined for any
 * @returns the steps
 */
export function stepsFor(
  catalog: PipelineCatalog,
  kinds: StepKind[],
  problemType?: ProblemType,
): CatalogStep[] {
  return catalog.steps.filter(
    (step) =>
      kinds.includes(step.kind) &&
      (problemType === undefined || step.problem_types.includes(problemType)),
  );
}

/**
 * Finds a catalog step by id.
 * @param catalog - the catalog
 * @param id - the step id
 * @returns the step, or undefined
 */
export function findStep(
  catalog: PipelineCatalog,
  id: string,
): CatalogStep | undefined {
  return catalog.steps.find((step) => step.id === id);
}

// ── Redundant scalers ──────────────────────────────────────────────────────────────────────────────────────────────

/**
 * Tree models: they split on one column's value order at a time, so a per-column scaler has no effect on them.
 * Mirrors `helix_core.pipelines.advice.TREE_MODELS`; keep the two in step.
 */
export const TREE_MODELS: ReadonlySet<string> = new Set([
  "random_forest_classifier",
  "random_forest_regressor",
  "xgboost_classifier",
  "xgboost_regressor",
]);

/** Steps that rescale each column affinely. Mirrors `helix_core.pipelines.advice.SCALERS`. */
export const SCALERS: ReadonlySet<string> = new Set([
  "standard_scaler",
  "mean_centering",
  "min_max_scaler",
  "robust_scaler",
]);

/**
 * Whether a later step gives a different result when `scaler` runs first: `lasso_selector`'s penalty depends on
 * scale, and `variance_threshold`'s absolute cutoff does unless the scaler only centres.
 */
function scaleSensitiveAfter(scaler: string, laterStep: string): boolean {
  if (laterStep === "lasso_selector") return true;
  return laterStep === "variance_threshold" && scaler !== "mean_centering";
}

/**
 * The scaler steps that have no effect because a tree model follows them with nothing scale-sensitive in between.
 * The same rule as `helix_core.pipelines.redundant_scaler_steps`, which the training worker uses to explain a failed
 * ONNX parity check.
 * @param stepIds - the pipeline's step ids, in order
 * @param modelId - the model's step id
 * @returns the redundant scaler ids, in order, without duplicates
 */
export function redundantScalerSteps(
  stepIds: string[],
  modelId: string,
): string[] {
  if (!TREE_MODELS.has(modelId)) return [];
  const redundant: string[] = [];
  stepIds.forEach((stepId, i) => {
    if (!SCALERS.has(stepId) || redundant.includes(stepId)) return;
    const later = stepIds.slice(i + 1);
    if (!later.some((laterStep) => scaleSensitiveAfter(stepId, laterStep))) {
      redundant.push(stepId);
    }
  });
  return redundant;
}

// ── The editor form ────────────────────────────────────────────────────────────────────────────────────────────────

/** A step as the editor holds it: params as form values (numbers as text, int arrays as `"100, 50"`). */
export interface StepFormValues {
  step: string;
  params: Record<string, unknown>;
}

/** The pipeline editor's values. Number inputs hold text; {@link specFromForm} turns it into a spec. */
export interface PipelineFormValues {
  name: string;
  problemType: ProblemType;
  targetColumn: string;
  /** `""` for none. */
  idColumn: string;
  featureColumns: string[];
  testSize: string;
  randomSeed: string;
  /** `""` for no cross-validation. */
  cvFolds: string;
  steps: StepFormValues[];
  model: StepFormValues;
  searchEnabled: boolean;
  searchMethod: "grid" | "random";
  searchCvFolds: string;
  nIter: string;
  /** Whether the grid is the user's own rather than the model's default. */
  customGrid: boolean;
  /** Model param → candidates as text: comma-separated, or `;`-separated lists for an int array param. */
  paramGrid: Record<string, string>;
}

/** The separator between candidates in a grid field: `;` for int array params, whose values contain commas. */
export function gridSeparator(field: FieldDescriptor): string {
  return field.kind === "intArray" ? ";" : ",";
}

/**
 * Shows one grid candidate as text: `None` for null, `100, 50` for an int array.
 * @param value - the candidate
 * @returns its text
 */
export function formatGridValue(value: unknown): string {
  if (value === null) return "None";
  if (Array.isArray(value)) return value.join(", ");
  return String(value);
}

/**
 * Shows a model's grid for a param as editable text.
 * @param field - the param
 * @param values - its candidates
 * @returns the text, e.g. `"10, 50, 100"` or `"50; 100; 100, 50"`
 */
export function gridToText(field: FieldDescriptor, values: unknown[]): string {
  return values.map(formatGridValue).join(`${gridSeparator(field)} `);
}

/**
 * Parses a grid field's text into candidates, each checked against the param's schema.
 * @param field - the param
 * @param text - the text
 * @returns the candidates, or an error message
 */
export function parseGridText(
  field: FieldDescriptor,
  text: string,
): { values: unknown[] } | { error: string } {
  const parts = text
    .split(gridSeparator(field))
    .map((part) => part.trim())
    .filter((part) => part !== "");
  const values: unknown[] = [];
  for (const part of parts) {
    let candidate: unknown = part;
    if (/^(none|null)$/i.test(part)) candidate = null;
    else if (field.kind === "boolean" && /^(true|false)$/i.test(part))
      candidate = part.toLowerCase() === "true";
    const parsed = fieldZod(field).safeParse(candidate);
    if (!parsed.success || (parsed.data === null && !field.nullable)) {
      return { error: `'${part}' isn't a valid ${field.label}.` };
    }
    values.push(parsed.data);
  }
  return { values };
}

function stepFormFromSpec(
  catalog: PipelineCatalog,
  step: StepSpec,
): StepFormValues {
  const definition = findStep(catalog, step.step);
  return {
    step: step.step,
    params: definition
      ? paramsToForm(definition.params_schema, step.params)
      : { ...step.params },
  };
}

/**
 * A new pipeline's starting values: every numeric column as a feature, the API's split defaults, no steps or model.
 * @param columns - the dataset's columns
 * @returns the form values
 */
export function emptyForm(columns: DatasetColumn[]): PipelineFormValues {
  return {
    name: "",
    problemType: "regression",
    targetColumn: "",
    idColumn: "",
    featureColumns: columns
      .filter((c) => c.inferredType === "numeric")
      .map((c) => c.name),
    testSize: "0.2",
    randomSeed: "42",
    cvFolds: "",
    steps: [],
    model: { step: "", params: {} },
    searchEnabled: false,
    searchMethod: "grid",
    searchCvFolds: "5",
    nIter: "",
    customGrid: false,
    paramGrid: {},
  };
}

/**
 * The editor's values for a saved pipeline.
 * @param name - the pipeline's name
 * @param spec - its spec
 * @param catalog - the catalog, to turn params into form values
 * @returns the form values
 */
export function formFromSpec(
  name: string,
  spec: PipelineSpec,
  catalog: PipelineCatalog,
): PipelineFormValues {
  const model = findStep(catalog, spec.model.step);
  const grid = spec.search?.param_grid ?? null;
  const fields = model ? describeFields(model.params_schema) : [];
  return {
    name,
    problemType: spec.problem_type,
    targetColumn: spec.target_column,
    idColumn: spec.id_column ?? "",
    featureColumns: [...spec.feature_columns],
    testSize: String(spec.split.test_size),
    randomSeed: String(spec.split.random_seed),
    cvFolds: spec.split.cv_folds === null ? "" : String(spec.split.cv_folds),
    steps: spec.steps.map((step) => stepFormFromSpec(catalog, step)),
    model: stepFormFromSpec(catalog, spec.model),
    searchEnabled: spec.search !== null,
    searchMethod: spec.search?.method ?? "grid",
    searchCvFolds: String(spec.search?.cv_folds ?? 5),
    nIter:
      spec.search?.n_iter === null || spec.search?.n_iter === undefined
        ? ""
        : String(spec.search.n_iter),
    customGrid: grid !== null,
    paramGrid: Object.fromEntries(
      Object.entries(grid ?? {}).map(([key, values]) => {
        const field = fields.find((f) => f.name === key);
        return [
          key,
          field
            ? gridToText(field, values)
            : values.map(formatGridValue).join(", "),
        ];
      }),
    ),
  };
}

function parseInt10(text: string): number {
  return Number.parseInt(text.trim(), 10);
}

/**
 * Builds the snake_case spec to save from the editor's values. Call it on values that passed
 * {@link pipelineFormSchema}.
 * @param values - the form values
 * @param catalog - the catalog, for each step's params schema
 * @param columns - the dataset's columns, to keep features in dataset order
 * @returns the spec
 */
export function specFromForm(
  values: PipelineFormValues,
  catalog: PipelineCatalog,
  columns: DatasetColumn[],
): PipelineSpec {
  const parseStep = (step: StepFormValues): StepSpec => {
    const definition = findStep(catalog, step.step);
    return {
      step: step.step,
      params: definition
        ? paramsZod(definition.params_schema).parse(step.params)
        : step.params,
    };
  };

  const model = findStep(catalog, values.model.step);
  let paramGrid: Record<string, unknown[]> | null = null;
  if (values.searchEnabled && values.customGrid && model) {
    paramGrid = {};
    for (const field of describeFields(model.params_schema)) {
      const text = values.paramGrid[field.name] ?? "";
      const parsed = parseGridText(field, text);
      if ("values" in parsed && parsed.values.length > 0)
        paramGrid[field.name] = parsed.values;
    }
  }

  const chosen = new Set(values.featureColumns);
  return {
    problem_type: values.problemType,
    target_column: values.targetColumn,
    feature_columns: columns
      .map((c) => c.name)
      .filter((name) => chosen.has(name)),
    id_column: values.idColumn || null,
    split: {
      test_size: Number(values.testSize),
      random_seed: parseInt10(values.randomSeed),
      cv_folds: values.cvFolds === "" ? null : parseInt10(values.cvFolds),
    },
    steps: values.steps.map(parseStep),
    model: parseStep(values.model),
    search: values.searchEnabled
      ? {
          method: values.searchMethod,
          cv_folds: parseInt10(values.searchCvFolds),
          n_iter:
            values.searchMethod === "random" ? parseInt10(values.nIter) : null,
          param_grid: paramGrid,
        }
      : null,
  };
}

const intText = (min: number, max: number, message: string) =>
  z
    .string()
    .trim()
    .regex(/^-?\d+$/, "Enter a whole number.")
    .refine((text) => {
      const n = Number(text);
      return n >= min && n <= max;
    }, message);

/**
 * The editor's validation: the API's own rules, so most problems show on the field before saving. Steps and params
 * are checked against the catalog; columns against the dataset.
 * @param catalog - the catalog
 * @param columns - the dataset's columns
 * @returns the zod schema for {@link PipelineFormValues}
 */
export function pipelineFormSchema(
  catalog: PipelineCatalog,
  columns: DatasetColumn[],
) {
  const byName = new Map(columns.map((c) => [c.name, c]));
  const step = z.object({
    step: z.string(),
    params: z.record(z.string(), z.unknown()),
  });

  return z
    .object({
      name: z
        .string()
        .trim()
        .min(1, "Enter a name.")
        .max(100, "That name is too long."),
      problemType: z.enum(["classification", "regression"]),
      targetColumn: z.string().min(1, "Choose the column to predict."),
      idColumn: z.string(),
      featureColumns: z
        .array(z.string())
        .min(1, "Choose at least one feature column."),
      testSize: z
        .string()
        .trim()
        .refine((text) => {
          const n = Number(text);
          return text !== "" && n >= 0.05 && n <= 0.5;
        }, "Use a test size between 0.05 and 0.5."),
      randomSeed: z
        .string()
        .trim()
        .regex(/^-?\d+$/, "Enter a whole number."),
      cvFolds: z.union([z.literal(""), intText(2, 10, "Use 2 to 10 folds.")]),
      steps: z.array(step),
      model: step,
      searchEnabled: z.boolean(),
      searchMethod: z.enum(["grid", "random"]),
      searchCvFolds: z.string(),
      nIter: z.string(),
      customGrid: z.boolean(),
      paramGrid: z.record(z.string(), z.string()),
    })
    .superRefine((values, ctx) => {
      const issue = (path: (string | number)[], message: string) =>
        ctx.addIssue({ code: "custom", path, message });

      const target = byName.get(values.targetColumn);
      if (
        values.targetColumn &&
        values.problemType === "regression" &&
        target?.inferredType !== "numeric"
      ) {
        issue(
          ["targetColumn"],
          "A regression target must be numeric. Choose classification, or another column.",
        );
      }
      if (values.featureColumns.includes(values.targetColumn)) {
        issue(["featureColumns"], "The target can't also be a feature.");
      }
      if (values.idColumn && values.idColumn === values.targetColumn) {
        issue(["idColumn"], "The ID column can't be the target.");
      }
      for (const name of values.featureColumns) {
        if (byName.get(name)?.inferredType !== "numeric") {
          issue(["featureColumns"], `'${name}' is not numeric.`);
        }
      }

      const checkStep = (value: StepFormValues, path: (string | number)[]) => {
        const definition = findStep(catalog, value.step);
        if (!definition) {
          issue([...path, "step"], "Choose a step from the list.");
          return;
        }
        if (!definition.problem_types.includes(values.problemType)) {
          issue(
            [...path, "step"],
            `${definition.label} doesn't support ${values.problemType}.`,
          );
        }
        const parsed = paramsZod(definition.params_schema).safeParse(
          value.params,
        );
        if (!parsed.success) {
          for (const problem of parsed.error.issues) {
            issue(
              [...path, "params", ...problem.path.map(String)],
              problem.message,
            );
          }
        }
      };

      values.steps.forEach((value, i) => checkStep(value, ["steps", i]));
      if (!values.model.step) {
        issue(["model", "step"], "Choose a model.");
      } else {
        checkStep(values.model, ["model"]);
      }

      if (!values.searchEnabled) return;
      if (!intText(2, 10, "x").safeParse(values.searchCvFolds).success) {
        issue(["searchCvFolds"], "Use 2 to 10 folds.");
      }
      if (
        values.searchMethod === "random" &&
        !intText(1, 500, "x").safeParse(values.nIter).success
      ) {
        issue(["nIter"], "A random search needs 1 to 500 candidates to try.");
      }

      const model = findStep(catalog, values.model.step);
      if (!model) return;
      if (!values.customGrid) {
        if (!model.search_grid) {
          issue(
            ["customGrid"],
            `${model.label} has no default grid: customise it.`,
          );
        }
        return;
      }
      let any = false;
      for (const field of describeFields(model.params_schema)) {
        const parsed = parseGridText(field, values.paramGrid[field.name] ?? "");
        if ("error" in parsed) issue(["paramGrid", field.name], parsed.error);
        else if (parsed.values.length > 0) any = true;
      }
      if (!any)
        issue(["customGrid"], "Give candidates for at least one parameter.");
    });
}
