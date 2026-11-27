import { render, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { EvaluationDetail } from "@/components/evaluations/evaluation-detail";
import {
  json,
  makeArtifact,
  makeEvaluation,
} from "@/components/evaluations/test-evaluations";
import { Providers } from "@/components/providers";
import { sseEvent, sseResponse } from "@/components/runs/test-runs";
import type { Evaluation } from "@/lib/evaluations";

const ARTIFACTS = [
  makeArtifact("metrics.json", "metrics"),
  makeArtifact("confusion_matrix.png"),
  makeArtifact("roc_curve.png"),
  makeArtifact("global_shap_bar.png"),
  makeArtifact("local_lime_violin.png"),
  makeArtifact("ensemble_mean.png"),
  makeArtifact("fuzzy_sets_x0.png"),
  makeArtifact("global_shap.csv", "evaluationTable"),
];

function stubApi(evaluation: Evaluation) {
  vi.stubGlobal(
    "fetch",
    vi.fn().mockImplementation((url: string) => {
      if (url.endsWith("/events")) {
        return Promise.resolve(
          sseResponse([
            sseEvent("status", evaluation),
            sseEvent("end", { status: evaluation.status }),
          ]),
        );
      }
      if (url.includes("/artifacts?")) return Promise.resolve(json(ARTIFACTS));
      if (url.includes("/artifacts/metrics.json/content")) {
        return Promise.resolve(
          json({
            train: { accuracy: 0.95, f1_weighted: 0.94 },
            test: { accuracy: 0.9, f1_weighted: 0.88 },
          }),
        );
      }
      return Promise.resolve(json(evaluation));
    }),
  );
}

function renderDetail() {
  return render(
    <Providers>
      <EvaluationDetail experimentId="exp-1" evaluationId="ev-1" />
    </Providers>,
  );
}

afterEach(() => vi.unstubAllGlobals());

describe("EvaluationDetail", () => {
  it("groups the plots into sections and shows train vs test metrics", async () => {
    stubApi(
      makeEvaluation({ status: "succeeded", metrics: { accuracy: 0.9 } }),
    );

    renderDetail();

    const plots = (await screen.findByText("Plots")).closest(
      "[data-slot=card]",
    ) as HTMLElement;
    const sections = within(plots)
      .getAllByRole("heading", { level: 3 })
      .map((h) => h.textContent);
    expect(sections).toEqual([
      "Performance",
      "Global importance",
      "Local importance",
      "Ensemble",
      "Fuzzy",
    ]);
    const performance = screen.getByRole("heading", {
      name: "Performance",
    }).parentElement!;
    expect(
      within(performance)
        .getAllByRole("img")
        .map((img) => img.getAttribute("alt")),
    ).toEqual(["confusion_matrix.png", "roc_curve.png"]);

    const accuracy = (
      await screen.findByRole("cell", { name: "accuracy" })
    ).closest("tr")!;
    expect(
      within(accuracy)
        .getAllByRole("cell")
        .map((c) => c.textContent),
    ).toEqual(["accuracy", "0.950", "0.900"]);

    expect(
      screen.getByRole("button", { name: /global_shap\.csv/ }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("link", { name: "Download global_shap.csv" }),
    ).toHaveAttribute(
      "href",
      "/api/proxy/experiments/exp-1/artifacts/global_shap.csv/content?download=true",
    );
  });

  it("shows the error when it failed", async () => {
    stubApi(
      makeEvaluation({
        status: "failed",
        error: {
          type: "InvalidSpecError",
          message: "Ensemble importance needs at least one global method",
        },
      }),
    );

    renderDetail();

    expect(await screen.findByText("InvalidSpecError")).toBeInTheDocument();
    expect(
      screen.getByText("Ensemble importance needs at least one global method"),
    ).toBeInTheDocument();
    expect(screen.queryByText("Plots")).not.toBeInTheDocument();
  });

  it("links the run it evaluated", async () => {
    stubApi(
      makeEvaluation({
        status: "running",
        progress: 0.4,
        message: "global_importance",
      }),
    );

    renderDetail();

    expect(
      await screen.findByRole("link", { name: "RF baseline" }),
    ).toHaveAttribute("href", "/experiments/exp-1/runs/run-1");
  });
});
