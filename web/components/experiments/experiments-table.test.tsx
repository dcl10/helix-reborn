import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { ExperimentsTable } from "@/components/experiments/experiments-table";
import type { Experiment } from "@/lib/experiments";

const withDataset: Experiment = {
  id: "exp-1",
  name: "With dataset",
  description: null,
  ownerSub: "user-1",
  createdAt: "2026-11-01T10:00:00Z",
  updatedAt: "2026-11-01T10:00:00Z",
  dataset: {
    id: "ds-1",
    fileName: "data.csv",
    sizeBytes: 1024,
    rowCount: 42,
    createdAt: "2026-11-01T10:00:00Z",
    columns: [],
  },
};

const withoutDataset: Experiment = {
  ...withDataset,
  id: "exp-2",
  name: "Without dataset",
  dataset: null,
};

describe("ExperimentsTable", () => {
  it("renders each experiment as a row, linking the name to its page", () => {
    render(<ExperimentsTable experiments={[withDataset, withoutDataset]} />);

    expect(screen.getByRole("link", { name: "With dataset" })).toHaveAttribute(
      "href",
      "/experiments/exp-1",
    );
    expect(screen.getByText("data.csv")).toBeInTheDocument();
    expect(screen.getByText("42")).toBeInTheDocument();
  });

  it("shows 'No dataset' for an experiment without one", () => {
    render(<ExperimentsTable experiments={[withoutDataset]} />);

    expect(screen.getByText("No dataset")).toBeInTheDocument();
  });

  it("shows an empty state with no experiments", () => {
    render(<ExperimentsTable experiments={[]} />);

    expect(screen.getByText(/No experiments yet/)).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });
});
