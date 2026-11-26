import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";

import { StepList } from "@/components/pipelines/step-list";
import {
  catalog,
  FormHarness,
  valuesOf,
} from "@/components/pipelines/test-fixtures";

function renderList() {
  return render(
    <FormHarness>
      {(form) => (
        <StepList
          control={form.control}
          catalog={catalog}
          problemType="regression"
        />
      )}
    </FormHarness>,
  );
}

function stepIds() {
  return valuesOf(screen.getByTestId("values")).steps.map((s) => s.step);
}

async function addStep(
  user: ReturnType<typeof userEvent.setup>,
  label: string,
) {
  await user.click(screen.getByRole("button", { name: "Add step" }));
  await user.click(await screen.findByRole("menuitem", { name: label }));
}

describe("StepList", () => {
  it("adds steps with their default params, grouped by kind in the menu", async () => {
    const user = userEvent.setup();
    renderList();

    await user.click(screen.getByRole("button", { name: "Add step" }));
    const menu = await screen.findByRole("menu");
    expect(within(menu).getByText("Preprocessing")).toBeInTheDocument();
    expect(within(menu).getByText("Feature selection")).toBeInTheDocument();
    expect(
      within(menu).queryByRole("menuitem", { name: "Ridge regression" }),
    ).not.toBeInTheDocument();
    await user.click(
      within(menu).getByRole("menuitem", { name: "Fill missing values" }),
    );

    const values = valuesOf(screen.getByTestId("values"));
    expect(values.steps).toEqual([
      { step: "simple_imputer", params: { strategy: "median" } },
    ]);
    expect(
      screen.getByRole("listitem", { name: "Step 1: Fill missing values" }),
    ).toBeInTheDocument();
  });

  it("reorders steps", async () => {
    const user = userEvent.setup();
    renderList();
    await addStep(user, "Fill missing values");
    await addStep(user, "Standard scaling");

    await user.click(
      screen.getByRole("button", { name: "Move Standard scaling up" }),
    );

    expect(stepIds()).toEqual(["standard_scaler", "simple_imputer"]);
    expect(
      screen.getByRole("button", { name: "Move Standard scaling up" }),
    ).toBeDisabled();

    await user.click(
      screen.getByRole("button", { name: "Move Standard scaling down" }),
    );

    expect(stepIds()).toEqual(["simple_imputer", "standard_scaler"]);
  });

  it("removes a step", async () => {
    const user = userEvent.setup();
    renderList();
    await addStep(user, "Fill missing values");
    await addStep(user, "Min-max scaling");

    await user.click(
      screen.getByRole("button", { name: "Remove Fill missing values" }),
    );

    expect(stepIds()).toEqual(["min_max_scaler"]);
  });
});
