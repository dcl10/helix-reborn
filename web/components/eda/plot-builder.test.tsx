import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { PlotBuilder } from "@/components/eda/plot-builder";
import { Providers } from "@/components/providers";
import type { ColumnSummary } from "@/lib/eda";

function column(name: string, isNumeric: boolean): ColumnSummary {
  return {
    name,
    dtype: isNumeric ? "float64" : "object",
    is_numeric: isNumeric,
    non_null: 10,
    missing: 0,
    missing_pct: 0,
    unique: 10,
    mean: isNumeric ? 1 : null,
    std: isNumeric ? 1 : null,
    min: isNumeric ? 0 : null,
    q25: isNumeric ? 0 : null,
    median: isNumeric ? 1 : null,
    q75: isNumeric ? 2 : null,
    max: isNumeric ? 2 : null,
    top_values: isNumeric ? null : [{ value: "x", count: 10 }],
  };
}

const columns = [
  column("age", true),
  column("height", true),
  column("weight", true),
  column("label", false),
];

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function renderBuilder() {
  return render(
    <Providers>
      <PlotBuilder experimentId="exp-1" columns={columns} />
    </Providers>,
  );
}

async function chooseType(
  user: ReturnType<typeof userEvent.setup>,
  label: string,
) {
  await user.click(screen.getByRole("combobox", { name: "Plot type" }));
  await user.click(await screen.findByRole("option", { name: label }));
}

describe("PlotBuilder", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("shows the chosen plot type's fields", async () => {
    const user = userEvent.setup();
    renderBuilder();

    expect(
      screen.getByRole("combobox", { name: "Method" }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("checkbox", { name: "Lower triangle only" }),
    ).not.toBeInTheDocument();

    await chooseType(user, "Pairplot");

    expect(
      screen.queryByRole("combobox", { name: "Method" }),
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole("checkbox", { name: "Lower triangle only" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("combobox", { name: "Diagonal" }),
    ).toBeInTheDocument();

    await chooseType(user, "t-SNE");

    expect(screen.getByLabelText("Perplexity")).toBeInTheDocument();
    expect(
      screen.getByRole("combobox", { name: "Scaling" }),
    ).toBeInTheDocument();
  });

  it("only offers numeric columns in the column list", async () => {
    renderBuilder();

    expect(screen.getByRole("checkbox", { name: "age" })).toBeInTheDocument();
    expect(
      screen.queryByRole("checkbox", { name: "label" }),
    ).not.toBeInTheDocument();
  });

  it("requires 2 to 10 columns for a pairplot", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();
    renderBuilder();

    await chooseType(user, "Pairplot");
    await user.click(screen.getByRole("checkbox", { name: "age" }));
    await user.click(screen.getByRole("button", { name: "Generate" }));

    expect(
      await screen.findByText("Choose between 2 and 10 columns."),
    ).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("sends the chosen type and its options", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      json(
        {
          id: "a-1",
          kind: "edaPlot",
          title: "Pairplot",
          contentType: "image/png",
          sizeBytes: 1,
          sourceType: "eda",
          sourceId: null,
          createdAt: "2026-11-26T10:00:00Z",
          fileName: "a-1.png",
        },
        201,
      ),
    );
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();
    renderBuilder();

    await chooseType(user, "Pairplot");
    await user.click(screen.getByRole("checkbox", { name: "age" }));
    await user.click(screen.getByRole("checkbox", { name: "weight" }));
    await user.click(screen.getByRole("combobox", { name: "Colour by" }));
    await user.click(await screen.findByRole("option", { name: "label" }));
    await user.click(screen.getByRole("button", { name: "Generate" }));

    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalled());
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("/api/proxy/experiments/exp-1/eda/plots");
    expect(init.method).toBe("POST");
    expect(JSON.parse(init.body)).toEqual({
      type: "pairplot",
      options: {
        columns: ["age", "weight"],
        hue_column: "label",
        corner: true,
        diag_kind: "hist",
      },
      plotOptions: null,
    });
  });

  it("sends filled-in plot options in snake_case", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(json({ id: "a-1", title: "x" }, 201));
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();
    renderBuilder();

    await user.click(screen.getByRole("button", { name: "Plot options" }));
    await user.type(await screen.findByLabelText("DPI"), "100");
    await user.type(screen.getByLabelText("Title font size"), "18");
    await user.click(screen.getByRole("button", { name: "Generate" }));

    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalled());
    expect(JSON.parse(fetchMock.mock.calls[0][1].body).plotOptions).toEqual({
      dpi: 100,
      title_font_size: 18,
    });
  });

  it("shows the API's error inline", async () => {
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValue(
          json({ detail: "Non-numeric column(s): label" }, 400),
        ),
    );
    const user = userEvent.setup();
    renderBuilder();

    await user.click(screen.getByRole("button", { name: "Generate" }));

    expect(
      await screen.findByText("Non-numeric column(s): label"),
    ).toBeInTheDocument();
  });
});
