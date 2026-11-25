import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { ExperimentTabLinks } from "@/components/experiments/experiment-tabs";

describe("ExperimentTabLinks", () => {
  it("highlights the tab matching the pathname", () => {
    render(
      <ExperimentTabLinks
        experimentId="exp-1"
        pathname="/experiments/exp-1/eda"
      />,
    );

    expect(screen.getByRole("link", { name: "EDA" })).toHaveAttribute(
      "aria-current",
      "page",
    );
    expect(screen.getByRole("link", { name: "Data" })).not.toHaveAttribute(
      "aria-current",
    );
  });

  it("links each tab to the experiment's path", () => {
    render(<ExperimentTabLinks experimentId="exp-1" pathname="" />);

    expect(screen.getByRole("link", { name: "Data" })).toHaveAttribute(
      "href",
      "/experiments/exp-1/data",
    );
    expect(screen.getByRole("link", { name: "Files" })).toHaveAttribute(
      "href",
      "/experiments/exp-1/files",
    );
  });

  it("highlights no tab when the pathname is unknown", () => {
    render(<ExperimentTabLinks experimentId="exp-1" />);

    for (const link of screen.getAllByRole("link")) {
      expect(link).not.toHaveAttribute("aria-current");
    }
  });
});
