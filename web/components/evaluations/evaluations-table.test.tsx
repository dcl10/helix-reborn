import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { EvaluationsTable } from "@/components/evaluations/evaluations-table";
import {
  json,
  makeEvaluation,
} from "@/components/evaluations/test-evaluations";
import { Providers } from "@/components/providers";

function renderTable() {
  return render(
    <Providers>
      <EvaluationsTable experimentId="exp-1" />
    </Providers>,
  );
}

afterEach(() => vi.unstubAllGlobals());

describe("EvaluationsTable", () => {
  it("lists evaluations with status and the headline test metric", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        json([
          makeEvaluation({
            id: "ev-2",
            status: "succeeded",
            metrics: { r2: 0.87654 },
          }),
          makeEvaluation({ id: "ev-1", status: "running", progress: 0.25 }),
        ]),
      ),
    );

    renderTable();

    const links = await screen.findAllByRole("link", { name: "RF baseline" });
    expect(links.map((l) => l.getAttribute("href"))).toEqual([
      "/experiments/exp-1/evaluations/ev-2",
      "/experiments/exp-1/evaluations/ev-1",
    ]);
    expect(screen.getByText("0.877")).toBeInTheDocument();
    expect(screen.getByText("Succeeded")).toBeInTheDocument();
    expect(screen.getByText("Running 25%")).toBeInTheDocument();
  });

  it("points to a new evaluation when there are none", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json([])));

    renderTable();

    expect(await screen.findByText(/No evaluations yet/)).toBeInTheDocument();
    expect(
      screen.getByRole("link", { name: "New evaluation" }),
    ).toHaveAttribute("href", "/experiments/exp-1/evaluations/new");
  });
});
