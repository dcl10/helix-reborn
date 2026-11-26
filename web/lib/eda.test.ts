import { afterEach, describe, expect, it, vi } from "vitest";

import {
  artifactContentUrl,
  createPlot,
  defaultPlotValues,
  getEdaSummary,
  listEdaArtifacts,
  plotSchema,
  type PlotValues,
  runNormalityTests,
  toPlotRequest,
} from "@/lib/eda";

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function stubFetch(body: unknown, status = 200) {
  const fetchMock = vi.fn().mockResolvedValue(jsonResponse(body, status));
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

function values(overrides: Partial<PlotValues>): PlotValues {
  return { ...defaultPlotValues, ...overrides };
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("getEdaSummary", () => {
  it("gets the summary through the proxy", async () => {
    const fetchMock = stubFetch({ row_count: 1, column_count: 0, columns: [] });

    await getEdaSummary("exp 1");

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("/api/proxy/experiments/exp%201/eda/summary");
    expect(init.method).toBeUndefined();
  });

  it("throws the API's error for a 409", async () => {
    stubFetch({ detail: "Experiment has no dataset." }, 409);

    await expect(getEdaSummary("exp-1")).rejects.toMatchObject({
      status: 409,
      message: "Experiment has no dataset.",
    });
  });
});

describe("createPlot", () => {
  it("posts the type, options and plot options as JSON", async () => {
    const fetchMock = stubFetch({ id: "a-1" }, 201);
    const request = {
      type: "pca" as const,
      options: { columns: null, colour_column: "target", scaling: "standard" },
      plotOptions: { dpi: 100 },
    };

    await createPlot("exp-1", request);

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("/api/proxy/experiments/exp-1/eda/plots");
    expect(init.method).toBe("POST");
    expect(init.headers["Content-Type"]).toBe("application/json");
    expect(JSON.parse(init.body)).toEqual(request);
  });
});

describe("runNormalityTests", () => {
  it("posts the columns, or null for all of them", async () => {
    const fetchMock = stubFetch({ artifact: {}, rows: [] }, 201);

    await runNormalityTests("exp-1", null);

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("/api/proxy/experiments/exp-1/eda/tables/normality");
    expect(init.method).toBe("POST");
    expect(JSON.parse(init.body)).toEqual({ columns: null });
  });
});

describe("listEdaArtifacts", () => {
  it("gets the EDA artifacts through the proxy", async () => {
    const fetchMock = stubFetch([]);

    await listEdaArtifacts("exp-1");

    expect(fetchMock.mock.calls[0][0]).toBe(
      "/api/proxy/experiments/exp-1/eda/artifacts",
    );
  });
});

describe("artifactContentUrl", () => {
  it("builds the inline and download URLs", () => {
    expect(artifactContentUrl("exp-1", "a-1")).toBe(
      "/api/proxy/experiments/exp-1/artifacts/a-1/content",
    );
    expect(artifactContentUrl("exp-1", "a-1", true)).toBe(
      "/api/proxy/experiments/exp-1/artifacts/a-1/content?download=true",
    );
  });
});

describe("toPlotRequest", () => {
  it("sends only the chosen type's fields, snake_case", () => {
    expect(
      toPlotRequest(
        values({ type: "pairplot", columns: ["a", "b"], hueColumn: "label" }),
      ),
    ).toEqual({
      type: "pairplot",
      options: {
        columns: ["a", "b"],
        hue_column: "label",
        corner: true,
        diag_kind: "hist",
      },
      plotOptions: null,
    });
  });

  it("sends null for no columns and no colour column", () => {
    expect(toPlotRequest(values({ type: "tsne" })).options).toEqual({
      columns: null,
      colour_column: null,
      scaling: "standard",
      perplexity: 30,
    });
  });

  it("keeps only the plot options that were filled in", () => {
    const request = toPlotRequest(
      values({
        plotOptions: { dpi: 100, title: "", style: "ticks", width: undefined },
      }),
    );

    expect(request.plotOptions).toEqual({ dpi: 100, style: "ticks" });
  });
});

describe("plotSchema", () => {
  it("requires 2 to 10 pairplot columns", () => {
    const tooFew = plotSchema.safeParse(
      values({ type: "pairplot", columns: ["a"] }),
    );
    const tooMany = plotSchema.safeParse(
      values({
        type: "pairplot",
        columns: Array.from({ length: 11 }, (_, i) => `c${i}`),
      }),
    );
    const justRight = plotSchema.safeParse(
      values({ type: "pairplot", columns: ["a", "b"] }),
    );

    expect(tooFew.success).toBe(false);
    expect(tooMany.success).toBe(false);
    expect(justRight.success).toBe(true);
  });

  it("requires a target column for a target distribution", () => {
    const result = plotSchema.safeParse(values({ type: "targetDistribution" }));

    expect(result.success).toBe(false);
    expect(result.error?.issues[0].path).toEqual(["targetColumn"]);
  });

  it("checks t-SNE perplexity only for t-SNE", () => {
    expect(
      plotSchema.safeParse(values({ type: "tsne", perplexity: 2 })).success,
    ).toBe(false);
    expect(
      plotSchema.safeParse(values({ type: "pca", perplexity: 2 })).success,
    ).toBe(true);
  });
});
