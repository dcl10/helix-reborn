import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { PipelineEditor } from "@/components/pipelines/pipeline-editor";
import { catalog, columns } from "@/components/pipelines/test-fixtures";
import { Providers } from "@/components/providers";

const { push } = vi.hoisted(() => ({ push: vi.fn() }));
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

const dataset = {
  id: "ds-1",
  fileName: "data.csv",
  sizeBytes: 10,
  rowCount: 100,
  createdAt: "2026-11-01T10:00:00Z",
  columns,
};

let posted: unknown[] = [];

function stubApi(saveResponse?: () => Response) {
  posted = [];
  vi.stubGlobal(
    "fetch",
    vi.fn().mockImplementation((url: string, init?: RequestInit) => {
      if (url === "/api/proxy/pipeline-catalog")
        return Promise.resolve(json(catalog));
      if (url === "/api/proxy/experiments/exp-1/dataset")
        return Promise.resolve(json(dataset));
      if (
        url === "/api/proxy/experiments/exp-1/pipelines" &&
        init?.method === "POST"
      ) {
        posted.push(JSON.parse(init.body as string));
        return Promise.resolve(
          saveResponse?.() ??
            json(
              { id: "p-1", name: "RF", spec: {}, createdAt: "", updatedAt: "" },
              201,
            ),
        );
      }
      return Promise.resolve(json({}, 404));
    }),
  );
}

async function renderEditor() {
  render(
    <Providers>
      <PipelineEditor experimentId="exp-1" />
    </Providers>,
  );
  await screen.findByRole("form", { name: "New pipeline" });
}

async function choose(
  user: ReturnType<typeof userEvent.setup>,
  combobox: string,
  option: string,
) {
  await user.click(screen.getByRole("combobox", { name: combobox }));
  await user.click(await screen.findByRole("option", { name: option }));
}

async function modelOptions(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole("combobox", { name: "Model" }));
  const options = (await screen.findAllByRole("option")).map(
    (o) => o.textContent,
  );
  await user.keyboard("{Escape}");
  return options;
}

describe("PipelineEditor", () => {
  beforeEach(() => {
    push.mockClear();
    stubApi();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("offers only the chosen problem type's models", async () => {
    const user = userEvent.setup();
    await renderEditor();

    const regression = await modelOptions(user);
    expect(regression).toContain("Ridge regression");
    expect(regression).not.toContain("Logistic regression");

    await user.click(screen.getByRole("radio", { name: "Classification" }));

    const classification = await modelOptions(user);
    expect(classification).toContain("Logistic regression");
    expect(classification).not.toContain("Ridge regression");
  });

  it("clears a model that doesn't support the new problem type", async () => {
    const user = userEvent.setup();
    await renderEditor();
    await choose(user, "Model", "Ridge regression");

    await user.click(screen.getByRole("radio", { name: "Classification" }));

    expect(screen.getByRole("combobox", { name: "Model" })).toHaveTextContent(
      "Choose a model",
    );
    expect(
      await screen.findByText(
        /Removed what doesn't support classification: Ridge regression/,
      ),
    ).toBeInTheDocument();
  });

  it("doesn't let text columns be features", async () => {
    const user = userEvent.setup();
    await renderEditor();

    expect(screen.getAllByText("(not numeric)", { exact: false })).toHaveLength(
      2,
    );
    // Base UI marks a disabled checkbox with aria-disabled, and ignores clicks.
    const site = screen.getByRole("checkbox", { name: /^site/ });
    expect(site).toHaveAttribute("aria-disabled", "true");
    await user.click(site);
    expect(site).not.toBeChecked();
    expect(screen.getByRole("checkbox", { name: "a" })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: "a" })).not.toHaveAttribute(
      "aria-disabled",
    );
  });

  it("drops the target from the features when it's chosen", async () => {
    const user = userEvent.setup();
    await renderEditor();
    expect(screen.getByRole("checkbox", { name: "y" })).toBeChecked();

    await choose(user, "Target column", "y");

    expect(
      screen.queryByRole("checkbox", { name: "y" }),
    ).not.toBeInTheDocument();
  });

  it("requires n_iter for a random search", async () => {
    const user = userEvent.setup();
    await renderEditor();
    await user.type(screen.getByLabelText("Pipeline name"), "Search");
    await choose(user, "Target column", "y");
    await choose(user, "Model", "Ridge regression");
    await user.click(
      screen.getByRole("switch", {
        name: /Search for the best model settings/,
      }),
    );
    await user.click(screen.getByRole("radio", { name: /Random/ }));

    await user.click(screen.getByRole("button", { name: "Save pipeline" }));

    expect(
      await screen.findByText(
        "A random search needs 1 to 500 candidates to try.",
      ),
    ).toBeInTheDocument();
    expect(posted).toEqual([]);
  });

  it("warns about a scaler before a tree model", async () => {
    const user = userEvent.setup();
    await renderEditor();
    await choose(user, "Model", "Random forest");
    expect(
      screen.queryByText("Scaling has no effect on this model"),
    ).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Add step" }));
    await user.click(
      await screen.findByRole("menuitem", { name: "Min-max scaling" }),
    );

    const warning = await screen.findByText(
      "Scaling has no effect on this model",
    );
    expect(
      warning.closest("[role=alert]") ?? warning.parentElement,
    ).toHaveTextContent(/Min-max scaling has no effect/);
  });

  it("saves the exact snake_case spec", async () => {
    const user = userEvent.setup();
    await renderEditor();
    await user.type(screen.getByLabelText("Pipeline name"), "RF baseline");
    await choose(user, "Target column", "y");
    await user.click(screen.getByRole("button", { name: "Add step" }));
    await user.click(
      await screen.findByRole("menuitem", { name: "Fill missing values" }),
    );
    await choose(user, "Model", "Random forest");
    const trees = screen.getByLabelText("N Estimators");
    await user.clear(trees);
    await user.type(trees, "200");
    await choose(user, "Cross-validation", "5 folds");
    await user.click(
      screen.getByRole("switch", {
        name: /Search for the best model settings/,
      }),
    );
    await user.click(screen.getByRole("button", { name: "Customise" }));
    const grid = screen.getByLabelText("Max Depth", {
      selector: "#grid-max_depth",
    });
    await user.clear(grid);
    await user.type(grid, "None, 10");

    await user.click(screen.getByRole("button", { name: "Save pipeline" }));

    await vi.waitFor(() => expect(posted).toHaveLength(1));
    expect(posted[0]).toEqual({
      name: "RF baseline",
      spec: {
        problem_type: "regression",
        target_column: "y",
        feature_columns: ["a", "b"],
        id_column: null,
        split: { test_size: 0.2, random_seed: 42, cv_folds: 5 },
        steps: [{ step: "simple_imputer", params: { strategy: "median" } }],
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
          method: "grid",
          cv_folds: 5,
          n_iter: null,
          param_grid: {
            n_estimators: [10, 50, 100, 200],
            max_depth: [null, 10],
            min_samples_split: [2, 5, 10],
          },
        },
      },
    });
    await vi.waitFor(() =>
      expect(push).toHaveBeenCalledWith("/experiments/exp-1/pipelines"),
    );
  });

  it("shows the API's spec errors at the top", async () => {
    stubApi(() =>
      json(
        {
          errors: {
            spec: [
              "model.step: unknown step 'ridge'",
              "split.test_size: too big",
            ],
          },
        },
        400,
      ),
    );
    const user = userEvent.setup();
    await renderEditor();
    await user.type(screen.getByLabelText("Pipeline name"), "Ridge");
    await choose(user, "Target column", "y");
    await choose(user, "Model", "Ridge regression");

    await user.click(screen.getByRole("button", { name: "Save pipeline" }));

    const alert = await screen.findByText("The pipeline couldn't be saved");
    const list = within(alert.parentElement!);
    expect(
      list.getByText("model.step: unknown step 'ridge'"),
    ).toBeInTheDocument();
    expect(list.getByText("split.test_size: too big")).toBeInTheDocument();
    expect(push).not.toHaveBeenCalled();
  });
});
