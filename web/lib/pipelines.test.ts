import { afterEach, describe, expect, it, vi } from "vitest";

import type { DatasetColumn } from "@/lib/experiments";
import { describeFields } from "@/lib/json-schema-form";
import {
  createPipeline,
  deletePipeline,
  emptyForm,
  findStep,
  formFromSpec,
  getPipelineCatalog,
  gridToText,
  listPipelines,
  parseGridText,
  type PipelineCatalog,
  pipelineFormSchema,
  type PipelineFormValues,
  type PipelineSpec,
  redundantScalerSteps,
  specFromForm,
  stepsFor,
  updatePipeline,
} from "@/lib/pipelines";

import catalogJson from "../../contracts/pipeline-catalog.json";

const catalog = catalogJson as unknown as PipelineCatalog;

const columns: DatasetColumn[] = [
  { name: "a", inferredType: "numeric", missingCount: 0 },
  { name: "site", inferredType: "text", missingCount: 0 },
  { name: "b", inferredType: "numeric", missingCount: 1 },
  { name: "y", inferredType: "numeric", missingCount: 0 },
  { name: "label", inferredType: "text", missingCount: 0 },
];

const spec: PipelineSpec = {
  problem_type: "regression",
  target_column: "y",
  feature_columns: ["a", "b"],
  id_column: null,
  split: { test_size: 0.25, random_seed: 7, cv_folds: 5 },
  steps: [
    { step: "simple_imputer", params: { strategy: "mean" } },
    { step: "standard_scaler", params: { with_mean: true, with_std: false } },
  ],
  model: {
    step: "random_forest_regressor",
    params: {
      n_estimators: 200,
      max_depth: null,
      min_samples_split: 2,
      min_samples_leaf: 1,
    },
  },
  search: {
    method: "random",
    cv_folds: 3,
    n_iter: 10,
    param_grid: { n_estimators: [50, 100], max_depth: [null, 10] },
  },
};

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("fetchers", () => {
  it("call the catalog and pipeline routes", async () => {
    const fetchMock = vi
      .fn()
      .mockImplementation(() => Promise.resolve(json({})));
    vi.stubGlobal("fetch", fetchMock);

    await getPipelineCatalog();
    await listPipelines("exp-1");
    await createPipeline("exp-1", { name: "RF", spec });
    await updatePipeline("exp-1", "p-1", { name: "RF", spec });
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }));
    await deletePipeline("exp-1", "p-1");

    const calls = fetchMock.mock.calls.map(([url, init]) => [
      url,
      init?.method ?? "GET",
    ]);
    expect(calls).toEqual([
      ["/api/proxy/pipeline-catalog", "GET"],
      ["/api/proxy/experiments/exp-1/pipelines", "GET"],
      ["/api/proxy/experiments/exp-1/pipelines", "POST"],
      ["/api/proxy/experiments/exp-1/pipelines/p-1", "PUT"],
      ["/api/proxy/experiments/exp-1/pipelines/p-1", "DELETE"],
    ]);
    expect(JSON.parse(fetchMock.mock.calls[2][1].body)).toEqual({
      name: "RF",
      spec,
    });
  });

  it("keep every validation message on the error", async () => {
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValue(json({ errors: { spec: ["one", "two"] } }, 400)),
    );

    await expect(
      createPipeline("exp-1", { name: "RF", spec }),
    ).rejects.toMatchObject({
      status: 400,
      errors: { spec: ["one", "two"] },
    });
  });
});

describe("stepsFor", () => {
  it("filters by kind and problem type", () => {
    const models = stepsFor(catalog, ["model"], "classification").map(
      (s) => s.id,
    );

    expect(models).toContain("logistic_regression");
    expect(models).not.toContain("ridge");
    expect(
      stepsFor(catalog, ["preprocessor"]).every(
        (s) => s.kind === "preprocessor",
      ),
    ).toBe(true);
  });
});

describe("redundantScalerSteps", () => {
  it.each<[string[], string, string[]]>([
    [["min_max_scaler"], "xgboost_classifier", ["min_max_scaler"]],
    [["standard_scaler", "lasso_selector"], "random_forest_classifier", []],
    [
      ["mean_centering", "variance_threshold"],
      "xgboost_regressor",
      ["mean_centering"],
    ],
    [["standard_scaler"], "logistic_regression", []],
    [["standard_scaler", "variance_threshold"], "xgboost_regressor", []],
    [
      [
        "simple_imputer",
        "robust_scaler",
        "select_k_best",
        "correlation_threshold",
      ],
      "random_forest_regressor",
      ["robust_scaler"],
    ],
    [
      ["standard_scaler", "lasso_selector", "min_max_scaler"],
      "random_forest_classifier",
      ["min_max_scaler"],
    ],
    [[], "xgboost_classifier", []],
  ])("%j before %s → %j", (steps, model, expected) => {
    expect(redundantScalerSteps(steps, model)).toEqual(expected);
  });
});

describe("formFromSpec / specFromForm", () => {
  it("round-trip a spec", () => {
    const values = formFromSpec("RF", spec, catalog);

    expect(values.model.params.n_estimators).toBe("200");
    expect(values.model.params.max_depth).toBe("");
    expect(values.paramGrid.max_depth).toBe("None, 10");
    expect(specFromForm(values, catalog, columns)).toEqual(spec);
  });

  it("keep features in dataset order", () => {
    const values = {
      ...formFromSpec("RF", spec, catalog),
      featureColumns: ["b", "a"],
    };

    expect(specFromForm(values, catalog, columns).feature_columns).toEqual([
      "a",
      "b",
    ]);
  });

  it("start a new pipeline with every numeric column", () => {
    expect(emptyForm(columns).featureColumns).toEqual(["a", "b", "y"]);
  });
});

describe("grid text", () => {
  const mlp = describeFields(findStep(catalog, "mlp_regressor")!.params_schema);
  const sizes = mlp.find((f) => f.name === "hidden_layer_sizes")!;
  const alpha = mlp.find((f) => f.name === "alpha")!;

  it("separates int array candidates with semicolons", () => {
    expect(gridToText(sizes, [[50], [100, 50]])).toBe("50; 100, 50");
    expect(parseGridText(sizes, "50; 100, 50")).toEqual({
      values: [[50], [100, 50]],
    });
  });

  it("checks each candidate against the param's schema", () => {
    expect(parseGridText(alpha, "0.001, 0.01")).toEqual({
      values: [0.001, 0.01],
    });
    expect(parseGridText(alpha, "0.001, -1")).toEqual({
      error: "'-1' isn't a valid Alpha.",
    });
  });
});

describe("pipelineFormSchema", () => {
  const schema = pipelineFormSchema(catalog, columns);
  const valid = (): PipelineFormValues => formFromSpec("RF", spec, catalog);

  function errorPaths(values: PipelineFormValues): string[] {
    const result = schema.safeParse(values);
    return result.success
      ? []
      : result.error.issues.map((i) => i.path.join("."));
  }

  it("accepts a valid pipeline", () => {
    expect(errorPaths(valid())).toEqual([]);
  });

  it("requires n_iter for a random search", () => {
    expect(errorPaths({ ...valid(), nIter: "" })).toContain("nIter");
  });

  it("rejects a text feature", () => {
    expect(errorPaths({ ...valid(), featureColumns: ["a", "site"] })).toContain(
      "featureColumns",
    );
  });

  it("rejects a text regression target", () => {
    expect(errorPaths({ ...valid(), targetColumn: "label" })).toContain(
      "targetColumn",
    );
  });

  it("names the bad param of a step", () => {
    const values = valid();
    values.model.params = { ...values.model.params, n_estimators: "5" };

    expect(errorPaths(values)).toContain("model.params.n_estimators");
  });

  it("rejects a model for the other problem type", () => {
    const values = {
      ...valid(),
      problemType: "classification" as const,
      targetColumn: "label",
    };

    expect(errorPaths(values)).toContain("model.step");
  });

  it("rejects a bad custom grid value", () => {
    const values = { ...valid(), paramGrid: { n_estimators: "50, 5" } };

    expect(errorPaths(values)).toContain("paramGrid.n_estimators");
  });
});
