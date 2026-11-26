import { render, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { Providers } from "@/components/providers";
import { RunsTable } from "@/components/runs/runs-table";
import { makeRun, resultFor } from "@/components/runs/test-runs";
import type { Run } from "@/lib/runs";

function stubRuns(runs: Run[]) {
  vi.stubGlobal(
    "fetch",
    vi.fn().mockResolvedValue(
      new Response(JSON.stringify(runs), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      }),
    ),
  );
}

function rowOf(name: string) {
  return within(screen.getByRole("link", { name }).closest("tr")!);
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("RunsTable", () => {
  it("shows the headline metric per problem type, or a dash", async () => {
    stubRuns([
      makeRun({
        id: "c",
        pipelineName: "Classifier",
        status: "succeeded",
        startedAt: "2026-11-26T10:00:00Z",
        finishedAt: "2026-11-26T10:01:05Z",
        result: resultFor("classification"),
        spec: { ...makeRun().spec, problem_type: "classification" },
      }),
      makeRun({
        id: "r",
        pipelineName: "Regressor",
        status: "succeeded",
        result: resultFor("regression"),
      }),
      makeRun({ id: "q", pipelineName: "Queued one" }),
    ]);

    render(
      <Providers>
        <RunsTable experimentId="exp-1" />
      </Providers>,
    );

    expect(
      await screen.findByRole("link", { name: "Classifier" }),
    ).toHaveAttribute("href", "/experiments/exp-1/runs/c");
    expect(rowOf("Classifier").getByText("0.912")).toBeInTheDocument();
    expect(rowOf("Classifier").getByText("Accuracy")).toBeInTheDocument();
    expect(rowOf("Classifier").getByText("1 min 5 s")).toBeInTheDocument();
    expect(rowOf("Regressor").getByText("0.877")).toBeInTheDocument();
    expect(rowOf("Regressor").getByText("R²")).toBeInTheDocument();
    expect(rowOf("Queued one").getAllByText("—")).toHaveLength(2);
    expect(rowOf("Queued one").getByText("Queued")).toBeInTheDocument();
  });

  it("points at the pipelines when there are no runs", async () => {
    stubRuns([]);

    render(
      <Providers>
        <RunsTable experimentId="exp-1" />
      </Providers>,
    );

    expect(
      await screen.findByRole("link", { name: "Go to pipelines" }),
    ).toHaveAttribute("href", "/experiments/exp-1/pipelines");
  });
});
