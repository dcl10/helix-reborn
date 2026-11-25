import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { DeleteExperimentButton } from "@/components/experiments/delete-experiment-button";
import { Providers } from "@/components/providers";
import type { Experiment } from "@/lib/experiments";

const { push } = vi.hoisted(() => ({ push: vi.fn() }));
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));

const experiment: Experiment = {
  id: "exp-1",
  name: "My experiment",
  description: null,
  ownerSub: "user-1",
  createdAt: "2026-11-01T10:00:00Z",
  updatedAt: "2026-11-01T10:00:00Z",
  dataset: null,
};

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function renderButton() {
  return render(
    <Providers>
      <DeleteExperimentButton experiment={experiment} />
    </Providers>,
  );
}

describe("DeleteExperimentButton", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    push.mockClear();
  });

  it("asks for confirmation before deleting", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();
    renderButton();

    await user.click(screen.getByRole("button", { name: "Delete" }));

    expect(await screen.findByRole("alertdialog")).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("deletes and navigates to the experiments list after confirming", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();
    renderButton();

    await user.click(screen.getByRole("button", { name: "Delete" }));
    const dialog = await screen.findByRole("alertdialog");
    await user.click(
      screen
        .getAllByRole("button", { name: "Delete" })
        .find((button) => dialog.contains(button))!,
    );

    await vi.waitFor(() => expect(push).toHaveBeenCalledWith("/experiments"));
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/proxy/experiments/exp-1",
      expect.objectContaining({ method: "DELETE" }),
    );
  });

  it("shows the API's error message when deleting fails", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(json({ detail: "Can't delete that." }, 400));
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();
    renderButton();

    await user.click(screen.getByRole("button", { name: "Delete" }));
    const dialog = await screen.findByRole("alertdialog");
    await user.click(
      screen
        .getAllByRole("button", { name: "Delete" })
        .find((button) => dialog.contains(button))!,
    );

    expect(await screen.findByText("Can't delete that.")).toBeInTheDocument();
    expect(push).not.toHaveBeenCalled();
  });
});
