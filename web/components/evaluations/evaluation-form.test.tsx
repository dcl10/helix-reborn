import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { EvaluationForm } from "@/components/evaluations/evaluation-form";
import {
  json,
  makeEvaluation,
} from "@/components/evaluations/test-evaluations";
import { Providers } from "@/components/providers";
import { makeRun } from "@/components/runs/test-runs";

const { push } = vi.hoisted(() => ({ push: vi.fn() }));
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));

function stubApi() {
  const fetchMock = vi
    .fn()
    .mockImplementation((url: string, init?: RequestInit) => {
      if (init?.method === "POST")
        return Promise.resolve(json(makeEvaluation({ id: "ev-9" }), 202));
      return Promise.resolve(
        json([
          makeRun({ status: "succeeded" }),
          makeRun({ id: "run-2", status: "failed" }),
        ]),
      );
    });
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

async function renderForm(initialRunId = "run-1") {
  render(
    <Providers>
      <EvaluationForm experimentId="exp-1" initialRunId={initialRunId} />
    </Providers>,
  );
  await screen.findByRole("button", { name: "Evaluate" });
}

const submit = () => screen.getByRole("button", { name: "Evaluate" });

/** Sets a number input; user-event can't clear a controlled number input in jsdom. */
const setNumber = (label: string, value: string) =>
  fireEvent.change(screen.getByLabelText(label), { target: { value } });

afterEach(() => {
  vi.unstubAllGlobals();
  push.mockReset();
});

describe("EvaluationForm", () => {
  it("shows an error and disables submit when fuzzy is on without a local method", async () => {
    stubApi();
    const user = userEvent.setup();
    await renderForm();
    expect(submit()).toBeEnabled();

    await user.click(screen.getByRole("checkbox", { name: "Mean" }));
    await user.click(
      screen.getByRole("switch", { name: "Run fuzzy interpretation" }),
    );

    expect(
      await screen.findByText(
        "Fuzzy interpretation needs at least one local method and at least one ensemble method.",
      ),
    ).toBeInTheDocument();
    expect(submit()).toBeDisabled();

    await user.click(screen.getByRole("checkbox", { name: "LIME" }));

    await waitFor(() => expect(submit()).toBeEnabled());
  });

  it("shows an error when an ensemble method has no global method", async () => {
    stubApi();
    const user = userEvent.setup();
    await renderForm();

    await user.click(screen.getByRole("checkbox", { name: "Permutation" }));
    // The first SHAP checkbox is the global one.
    await user.click(screen.getAllByRole("checkbox", { name: "SHAP" })[0]);
    await user.click(screen.getByRole("checkbox", { name: "Majority vote" }));

    expect(
      await screen.findByText(
        "Ensemble importance needs at least one global method.",
      ),
    ).toBeInTheDocument();
    expect(submit()).toBeDisabled();
  });

  it("shows an error when the cluster names don't match the number of clusters", async () => {
    stubApi();
    const user = userEvent.setup();
    await renderForm();
    await user.click(screen.getByRole("checkbox", { name: "LIME" }));
    await user.click(screen.getByRole("checkbox", { name: "Mean" }));
    await user.click(
      screen.getByRole("switch", { name: "Run fuzzy interpretation" }),
    );

    setNumber("Target clusters", "3");

    expect(
      await screen.findByText("Enter 3 names, one per cluster (you have 5)."),
    ).toBeInTheDocument();
    expect(submit()).toBeDisabled();

    const names = screen.getByLabelText("Cluster names");
    await user.clear(names);
    await user.type(names, "low, medium, high");

    await waitFor(() => expect(submit()).toBeEnabled());
  });

  it("disables submit until a run is chosen", async () => {
    stubApi();
    await renderForm("");

    expect(submit()).toBeDisabled();
  });

  it("posts the exact snake_case body and opens the evaluation", async () => {
    const fetchMock = stubApi();
    const user = userEvent.setup();
    await renderForm();
    await user.click(screen.getByRole("checkbox", { name: "LIME" }));
    await user.click(screen.getByRole("checkbox", { name: "Mean" }));
    await user.click(
      screen.getByRole("switch", { name: "Run fuzzy interpretation" }),
    );
    setNumber("Permutation repeats", "5");

    await user.click(submit());

    await waitFor(() =>
      expect(push).toHaveBeenCalledWith("/experiments/exp-1/evaluations/ev-9"),
    );
    const [url, init] = fetchMock.mock.calls.find(
      ([, i]) => i?.method === "POST",
    )!;
    expect(url).toBe("/api/proxy/experiments/exp-1/runs/run-1/evaluations");
    expect(JSON.parse(init.body as string)).toEqual({
      options: {
        plots: true,
        global_methods: ["permutation", "shap"],
        permutation_repeats: 5,
        local_methods: ["lime"],
        local_max_samples: 50,
        ensemble_methods: ["mean"],
        num_features_to_plot: 10,
        fuzzy: {
          number_fuzzy_features: 10,
          granular_features: true,
          number_clusters: 5,
          cluster_names: ["very low", "low", "medium", "high", "very high"],
          number_rules: 5,
        },
        plot_options: null,
      },
    });
  });

  it("says so when there are no succeeded runs", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(json([makeRun({ status: "running" })])),
    );
    render(
      <Providers>
        <EvaluationForm experimentId="exp-1" />
      </Providers>,
    );

    expect(
      await screen.findByText(/No trained models yet/),
    ).toBeInTheDocument();
  });
});
