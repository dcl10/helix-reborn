import { describe, expect, it } from "vitest";

import type { Artifact } from "@/lib/eda";
import {
  defaultEvaluationValues,
  evaluationFormSchema,
  groupEvaluationPlots,
  headlineTestMetric,
  parseClusterNames,
  parseCsv,
  toEvaluationOptions,
} from "@/lib/evaluations";

function artifact(
  fileName: string,
  kind: Artifact["kind"] = "evaluationPlot",
): Artifact {
  return {
    id: fileName,
    kind,
    title: fileName,
    contentType: "image/png",
    sizeBytes: 1,
    sourceType: "evaluation",
    sourceId: "ev-1",
    createdAt: "2026-11-27T10:00:00Z",
    fileName,
  };
}

function issues(values: ReturnType<typeof defaultEvaluationValues>) {
  const result = evaluationFormSchema.safeParse(values);
  return result.success
    ? {}
    : Object.fromEntries(
        result.error.issues.map((issue) => [
          issue.path.join("."),
          issue.message,
        ]),
      );
}

describe("groupEvaluationPlots", () => {
  it("groups plots by file-name prefix in display order", () => {
    const groups = groupEvaluationPlots([
      artifact("fuzzy_sets_x0.png"),
      artifact("global_shap_bar.png"),
      artifact("confusion_matrix.png"),
      artifact("residuals_histogram.png"),
      artifact("local_lime_violin.png"),
      artifact("ensemble_mean.png"),
      artifact("roc_curve.png"),
      artifact("mystery.png"),
      artifact("global_shap.csv", "evaluationTable"),
      artifact("metrics.json", "metrics"),
    ]);

    expect(
      groups.map((g) => [g.title, g.plots.map((p) => p.fileName)]),
    ).toEqual([
      [
        "Performance",
        ["confusion_matrix.png", "residuals_histogram.png", "roc_curve.png"],
      ],
      ["Global importance", ["global_shap_bar.png"]],
      ["Local importance", ["local_lime_violin.png"]],
      ["Ensemble", ["ensemble_mean.png"]],
      ["Fuzzy", ["fuzzy_sets_x0.png"]],
      ["Other", ["mystery.png"]],
    ]);
  });

  it("leaves out empty groups", () => {
    expect(
      groupEvaluationPlots([artifact("predicted_vs_actual.png")]).map(
        (g) => g.title,
      ),
    ).toEqual(["Performance"]);
  });
});

describe("parseCsv", () => {
  it("parses quoted fields with commas, quotes and newlines", () => {
    const table = parseCsv(
      'feature,"note, with comma",value\r\nx0,"say ""hi""",1.5\nx1,"two\nlines",2\n',
      50,
    );

    expect(table.header).toEqual(["feature", "note, with comma", "value"]);
    expect(table.rows).toEqual([
      ["x0", 'say "hi"', "1.5"],
      ["x1", "two\nlines", "2"],
    ]);
    expect(table.truncated).toBe(false);
  });

  it("caps the rows and says so", () => {
    const csv = ["n", ...Array.from({ length: 120 }, (_, i) => String(i))].join(
      "\n",
    );

    const table = parseCsv(csv, 50);

    expect(table.rows).toHaveLength(50);
    expect(table.rows[49]).toEqual(["49"]);
    expect(table.truncated).toBe(true);
  });

  it("handles a file without a trailing newline or with exactly the cap", () => {
    const table = parseCsv("a,b\n1,2", 1);

    expect(table.rows).toEqual([["1", "2"]]);
    expect(table.truncated).toBe(false);
  });
});

describe("evaluationFormSchema", () => {
  it("accepts the defaults with a run", () => {
    expect(issues(defaultEvaluationValues("run-1"))).toEqual({});
  });

  it("requires a run", () => {
    expect(issues(defaultEvaluationValues())).toHaveProperty("runId");
  });

  it("requires a global method for ensembles", () => {
    const values = {
      ...defaultEvaluationValues("run-1"),
      globalPermutation: false,
      globalShap: false,
      ensembleMean: true,
    };

    expect(issues(values).ensembleMean).toMatch(/at least one global method/);
  });

  it("requires local and ensemble methods for fuzzy", () => {
    const values = { ...defaultEvaluationValues("run-1"), fuzzyEnabled: true };

    expect(issues(values).fuzzyEnabled).toMatch(
      /local method and at least one ensemble/,
    );
  });

  it("requires one cluster name per cluster, only when fuzzy is on", () => {
    const base = defaultEvaluationValues("run-1");
    const fuzzy = { ...base.fuzzy, numberClusters: 3 };

    expect(issues({ ...base, fuzzy })).toEqual({});
    expect(
      issues({
        ...base,
        fuzzy,
        fuzzyEnabled: true,
        localLime: true,
        ensembleMean: true,
      })["fuzzy.clusterNames"],
    ).toBe("Enter 3 names, one per cluster (you have 5).");
  });
});

describe("toEvaluationOptions", () => {
  it("builds snake_case options, dropping empty plot options", () => {
    const options = toEvaluationOptions({
      ...defaultEvaluationValues("run-1"),
      globalPermutation: false,
      localLime: true,
      localShap: true,
      ensembleMajorityVote: true,
      fuzzyEnabled: true,
      fuzzy: {
        numberFuzzyFeatures: 4,
        granularFeatures: false,
        numberClusters: 3,
        clusterNames: " low, mid ,high,",
        numberRules: 2,
      },
      plotOptions: { dpi: 200, title: "", colour_map: undefined },
    });

    expect(options).toEqual({
      plots: true,
      global_methods: ["shap"],
      permutation_repeats: 10,
      local_methods: ["lime", "shap"],
      local_max_samples: 50,
      ensemble_methods: ["majority_vote"],
      num_features_to_plot: 10,
      fuzzy: {
        number_fuzzy_features: 4,
        granular_features: false,
        number_clusters: 3,
        cluster_names: ["low", "mid", "high"],
        number_rules: 2,
      },
      plot_options: { dpi: 200 },
    });
  });

  it("sends null fuzzy and plot options when unset", () => {
    const options = toEvaluationOptions(defaultEvaluationValues("run-1"));

    expect(options.fuzzy).toBeNull();
    expect(options.plot_options).toBeNull();
  });
});

describe("parseClusterNames", () => {
  it("trims and drops empty names", () => {
    expect(parseClusterNames(" a , ,b,")).toEqual(["a", "b"]);
  });
});

describe("headlineTestMetric", () => {
  it("prefers accuracy, then R²", () => {
    expect(headlineTestMetric({ metrics: { accuracy: 0.9, f1: 0.8 } })).toEqual(
      {
        label: "Accuracy",
        value: 0.9,
      },
    );
    expect(headlineTestMetric({ metrics: { r2: 0.7 } })).toEqual({
      label: "R²",
      value: 0.7,
    });
    expect(headlineTestMetric({ metrics: null })).toBeNull();
  });
});
