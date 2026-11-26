import { afterEach, describe, expect, it, vi } from "vitest";

import { makeRun, resultFor } from "@/components/runs/test-runs";
import {
  formatDuration,
  formatMetric,
  headlineMetric,
  isActive,
  listRunArtifacts,
  listRuns,
  runEventsUrl,
  startRun,
} from "@/lib/runs";

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("fetchers", () => {
  it("call the run routes", async () => {
    const fetchMock = vi.fn().mockImplementation(() =>
      Promise.resolve(
        new Response("[]", {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }),
      ),
    );
    vi.stubGlobal("fetch", fetchMock);

    await startRun("exp-1", "p-1");
    await listRuns("exp-1");
    await listRunArtifacts("exp-1", "run-1");

    expect(
      fetchMock.mock.calls.map(([url, init]) => [url, init?.method ?? "GET"]),
    ).toEqual([
      ["/api/proxy/experiments/exp-1/pipelines/p-1/runs", "POST"],
      ["/api/proxy/experiments/exp-1/runs", "GET"],
      [
        "/api/proxy/experiments/exp-1/artifacts?sourceType=run&sourceId=run-1",
        "GET",
      ],
    ]);
    expect(runEventsUrl("exp-1", "run-1")).toBe(
      "/api/proxy/experiments/exp-1/runs/run-1/events",
    );
  });
});

describe("headlineMetric", () => {
  it("is CV accuracy for classification", () => {
    const run = makeRun({
      status: "succeeded",
      result: resultFor("classification"),
      spec: { ...makeRun().spec, problem_type: "classification" },
    });

    expect(headlineMetric(run)).toEqual({ label: "Accuracy", value: 0.91234 });
  });

  it("is CV R² for regression", () => {
    expect(
      headlineMetric(
        makeRun({ status: "succeeded", result: resultFor("regression") }),
      ),
    ).toEqual({
      label: "R²",
      value: 0.87654,
    });
  });

  it("is null without cross-validation", () => {
    const run = makeRun({
      status: "succeeded",
      result: { ...resultFor("regression"), cv_metrics: null },
    });

    expect(headlineMetric(run)).toBeNull();
    expect(headlineMetric(makeRun())).toBeNull();
  });
});

describe("formatting", () => {
  it("formats metrics to 3 decimal places", () => {
    expect(formatMetric(0.87654)).toBe("0.877");
    expect(formatMetric(null)).toBe("—");
  });

  it("formats durations", () => {
    const start = "2026-11-26T10:00:00Z";
    expect(formatDuration({ startedAt: null, finishedAt: null })).toBe("—");
    expect(
      formatDuration({ startedAt: start, finishedAt: "2026-11-26T10:00:45Z" }),
    ).toBe("45 s");
    expect(
      formatDuration({ startedAt: start, finishedAt: "2026-11-26T10:03:12Z" }),
    ).toBe("3 min 12 s");
    expect(
      formatDuration({ startedAt: start, finishedAt: "2026-11-26T11:05:00Z" }),
    ).toBe("1 h 5 min");
    expect(
      formatDuration(
        { startedAt: start, finishedAt: null },
        new Date("2026-11-26T10:00:10Z"),
      ),
    ).toBe("10 s");
  });

  it("knows which statuses are in progress", () => {
    expect(
      ["queued", "running", "succeeded", "failed"].map((s) =>
        isActive(s as never),
      ),
    ).toEqual([true, true, false, false]);
  });
});
