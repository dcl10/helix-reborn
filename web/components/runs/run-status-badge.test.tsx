import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { RunStatusBadge } from "@/components/runs/run-status-badge";

describe("RunStatusBadge", () => {
  it.each([
    ["queued", "Queued"],
    ["succeeded", "Succeeded"],
    ["failed", "Failed"],
  ] as const)("renders %s", (status, label) => {
    render(<RunStatusBadge status={status} />);

    expect(screen.getByText(label)).toBeInTheDocument();
  });

  it("shows a running run's progress", () => {
    render(<RunStatusBadge status="running" progress={0.456} />);

    expect(screen.getByText("Running 46%")).toBeInTheDocument();
  });

  it("colours each status differently", () => {
    const { rerender } = render(<RunStatusBadge status="running" />);
    expect(screen.getByText("Running")).toHaveClass("bg-blue-600");

    rerender(<RunStatusBadge status="succeeded" />);
    expect(screen.getByText("Succeeded")).toHaveClass("bg-green-600");
  });
});
