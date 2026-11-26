import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { RunPipelineButton } from "@/components/pipelines/run-pipeline-button";
import { Providers } from "@/components/providers";
import { makeRun } from "@/components/runs/test-runs";

const { push } = vi.hoisted(() => ({ push: vi.fn() }));
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));

function respond(body: unknown, status: number) {
  const fetchMock = vi.fn().mockResolvedValue(
    new Response(JSON.stringify(body), {
      status,
      headers: { "Content-Type": "application/json" },
    }),
  );
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

function renderButton() {
  return render(
    <Providers>
      <RunPipelineButton
        experimentId="exp-1"
        pipelineId="p-1"
        pipelineName="RF baseline"
      />
    </Providers>,
  );
}

describe("RunPipelineButton", () => {
  beforeEach(() => push.mockClear());
  afterEach(() => vi.unstubAllGlobals());

  it("starts the run and opens it on 202", async () => {
    const fetchMock = respond(makeRun({ id: "run-9" }), 202);
    const user = userEvent.setup();
    renderButton();

    await user.click(screen.getByRole("button", { name: "Run" }));

    await vi.waitFor(() =>
      expect(push).toHaveBeenCalledWith("/experiments/exp-1/runs/run-9"),
    );
    expect(fetchMock.mock.calls[0][0]).toBe(
      "/api/proxy/experiments/exp-1/pipelines/p-1/runs",
    );
    expect(fetchMock.mock.calls[0][1].method).toBe("POST");
  });

  it("shows the API's detail when the queue is unavailable", async () => {
    respond(
      { detail: "The training queue is unavailable; try again shortly." },
      503,
    );
    const user = userEvent.setup();
    renderButton();

    await user.click(screen.getByRole("button", { name: "Run" }));

    expect(
      await screen.findByText(
        /Couldn't start “RF baseline”: The training queue is unavailable/,
      ),
    ).toBeInTheDocument();
    expect(push).not.toHaveBeenCalled();
  });

  it("shows the first spec error on 400", async () => {
    respond(
      {
        errors: {
          spec: ["feature_columns: 'a' isn't a column in the dataset"],
        },
      },
      400,
    );
    const user = userEvent.setup();
    renderButton();

    await user.click(screen.getByRole("button", { name: "Run" }));

    expect(
      await screen.findByText(/'a' isn't a column in the dataset/),
    ).toBeInTheDocument();
  });
});
