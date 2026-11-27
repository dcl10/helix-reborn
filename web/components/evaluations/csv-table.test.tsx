import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { CsvTable } from "@/components/evaluations/csv-table";
import { makeArtifact } from "@/components/evaluations/test-evaluations";
import { Providers } from "@/components/providers";

afterEach(() => vi.unstubAllGlobals());

describe("CsvTable", () => {
  it("renders a quoted CSV and caps it at 50 rows", async () => {
    const rows = Array.from({ length: 60 }, (_, i) => `x${i},"a, ""b""",${i}`);
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValue(
          new Response(["feature,note,value", ...rows].join("\n")),
        ),
    );

    render(
      <Providers>
        <CsvTable
          experimentId="exp-1"
          artifact={makeArtifact("t.csv", "evaluationTable")}
        />
      </Providers>,
    );

    expect(
      await screen.findByRole("columnheader", { name: "note" }),
    ).toBeInTheDocument();
    expect(screen.getAllByRole("row")).toHaveLength(51);
    expect(screen.getAllByText('a, "b"')).toHaveLength(50);
    expect(screen.getByText(/Showing the first 50 rows/)).toBeInTheDocument();
  });
});
