import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";

import { StepParamsForm } from "@/components/pipelines/step-params-form";
import {
  catalog,
  FormHarness,
  valuesOf,
} from "@/components/pipelines/test-fixtures";
import { paramsToForm } from "@/lib/json-schema-form";
import { findStep } from "@/lib/pipelines";

function renderModel(stepId: string) {
  const schema = findStep(catalog, stepId)!.params_schema;
  return render(
    <FormHarness
      defaults={{ model: { step: stepId, params: paramsToForm(schema) } }}
    >
      {(form) => (
        <StepParamsForm
          control={form.control}
          name="model.params"
          schema={schema}
        />
      )}
    </FormHarness>,
  );
}

describe("StepParamsForm", () => {
  it("renders number fields with defaults and bounds, and a blank nullable as None", () => {
    renderModel("random_forest_regressor");

    expect(screen.getByLabelText("N Estimators")).toHaveValue("100");
    expect(
      screen.getByText(/Number of trees\. · 10 to 1000/),
    ).toBeInTheDocument();
    expect(screen.getByLabelText("Max Depth")).toHaveValue("");
    expect(screen.getByLabelText("Max Depth")).toHaveAttribute(
      "placeholder",
      "None",
    );
  });

  it("renders an int list as comma-separated text and an enum as a select", async () => {
    const user = userEvent.setup();
    renderModel("mlp_regressor");

    const sizes = screen.getByLabelText("Hidden Layer Sizes");
    expect(sizes).toHaveValue("100");
    await user.clear(sizes);
    await user.type(sizes, "64, 32");

    await user.click(screen.getByRole("combobox", { name: "Activation" }));
    await user.click(await screen.findByRole("option", { name: "tanh" }));

    const params = valuesOf(screen.getByTestId("values")).model.params;
    expect(params.hidden_layer_sizes).toBe("64, 32");
    expect(params.activation).toBe("tanh");
  });

  it("renders booleans as checkboxes", async () => {
    const user = userEvent.setup();
    const schema = findStep(catalog, "standard_scaler")!.params_schema;
    render(
      <FormHarness
        defaults={{
          steps: [{ step: "standard_scaler", params: paramsToForm(schema) }],
        }}
      >
        {(form) => (
          <StepParamsForm
            control={form.control}
            name="steps.0.params"
            schema={schema}
          />
        )}
      </FormHarness>,
    );

    const withMean = screen.getByRole("checkbox", { name: "With Mean" });
    expect(withMean).toBeChecked();
    await user.click(withMean);

    expect(
      valuesOf(screen.getByTestId("values")).steps[0].params.with_mean,
    ).toBe(false);
  });

  it("says when a step has no settings", () => {
    renderModel("min_max_scaler");

    expect(screen.getByText("No settings.")).toBeInTheDocument();
  });
});
