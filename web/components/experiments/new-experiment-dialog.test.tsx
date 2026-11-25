import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { NewExperimentDialog } from "@/components/experiments/new-experiment-dialog";
import { Providers } from "@/components/providers";

const { push } = vi.hoisted(() => ({ push: vi.fn() }));
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function renderDialog() {
  return render(
    <Providers>
      <NewExperimentDialog />
    </Providers>,
  );
}

describe("NewExperimentDialog", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    push.mockClear();
  });

  it("shows a validation message for a missing name and doesn't call the API", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();
    renderDialog();

    await user.click(screen.getByRole("button", { name: "New experiment" }));
    await user.click(screen.getByRole("button", { name: "Create" }));

    expect(await screen.findByText("Enter a name.")).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("creates the experiment and navigates to its Data tab", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      json(
        {
          id: "exp-1",
          name: "New one",
          description: null,
          ownerSub: "user-1",
          createdAt: "2026-11-01T10:00:00Z",
          updatedAt: "2026-11-01T10:00:00Z",
          dataset: null,
        },
        201,
      ),
    );
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();
    renderDialog();

    await user.click(screen.getByRole("button", { name: "New experiment" }));
    await user.type(screen.getByLabelText("Name"), "New one");
    await user.click(screen.getByRole("button", { name: "Create" }));

    await vi.waitFor(() =>
      expect(push).toHaveBeenCalledWith("/experiments/exp-1/data"),
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/proxy/experiments",
      expect.objectContaining({ method: "POST" }),
    );
  });
});
