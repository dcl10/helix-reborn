import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { SummaryTable } from "@/components/eda/summary-table";
import { Providers } from "@/components/providers";
import type { DatasetSummary } from "@/lib/eda";

const summary: DatasetSummary = {
  row_count: 4,
  column_count: 2,
  columns: [
    {
      name: "age",
      dtype: "float64",
      is_numeric: true,
      non_null: 3,
      missing: 1,
      missing_pct: 25,
      unique: 3,
      mean: 30.123456,
      std: 5,
      min: 20,
      q25: 25,
      median: 30,
      q75: 35,
      max: 40,
      top_values: null,
    },
    {
      name: "label",
      dtype: "object",
      is_numeric: false,
      non_null: 4,
      missing: 0,
      missing_pct: 0,
      unique: 2,
      mean: null,
      std: null,
      min: null,
      q25: null,
      median: null,
      q75: null,
      max: null,
      top_values: [
        { value: "a", count: 3 },
        { value: "b", count: 1 },
      ],
    },
  ],
};

function rowOf(name: string) {
  return screen.getByRole("row", { name: new RegExp(`^${name}`) });
}

describe("SummaryTable", () => {
  it("renders null statistics as an em dash", () => {
    render(
      <Providers>
        <SummaryTable summary={summary} />
      </Providers>,
    );

    const cells = within(rowOf("label")).getAllByRole("cell");
    expect(cells.slice(4).map((cell) => cell.textContent)).toEqual([
      "—",
      "—",
      "—",
      "—",
      "—",
    ]);
  });

  it("shows numeric statistics, missing counts and types", () => {
    render(
      <Providers>
        <SummaryTable summary={summary} />
      </Providers>,
    );

    const row = within(rowOf("age"));
    expect(row.getByText("numeric")).toBeInTheDocument();
    expect(row.getByText("1 (25%)")).toBeInTheDocument();
    expect(row.getByText("30.12")).toBeInTheDocument();
    expect(within(rowOf("label")).getByText("text")).toBeInTheDocument();
  });
});
