import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { MainNavLinks } from "@/components/main-nav";

describe("MainNavLinks", () => {
  it("hides the Users link for a non-admin", () => {
    render(<MainNavLinks isAdmin={false} pathname="/experiments" />);

    expect(
      screen.getByRole("link", { name: "Experiments" }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("link", { name: "Users" }),
    ).not.toBeInTheDocument();
  });

  it("shows the Users link for an admin", () => {
    render(<MainNavLinks isAdmin pathname="/experiments" />);

    expect(screen.getByRole("link", { name: "Users" })).toBeInTheDocument();
  });
});
