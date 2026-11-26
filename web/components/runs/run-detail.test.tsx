import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { Providers } from "@/components/providers";
import { RunDetail } from "@/components/runs/run-detail";
import {
  makeRun,
  resultFor,
  sseEvent,
  sseResponse,
} from "@/components/runs/test-runs";
import type { Run } from "@/lib/runs";

function json(body: unknown) {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
}

function stubApi(run: Run, streamed: Run[] = [run]) {
  vi.stubGlobal(
    "fetch",
    vi.fn().mockImplementation((url: string) => {
      if (url.endsWith("/events")) {
        return Promise.resolve(
          sseResponse([
            ...streamed.map((r) => sseEvent("status", r)),
            sseEvent("end", { status: streamed.at(-1)!.status }),
          ]),
        );
      }
      if (url.includes("/artifacts?")) {
        return Promise.resolve(
          json([
            {
              id: "a-1",
              kind: "model",
              title: "Model (ONNX)",
              contentType: "application/octet-stream",
              sizeBytes: 10,
              sourceType: "run",
              sourceId: run.id,
              createdAt: "",
              fileName: "model.onnx",
            },
          ]),
        );
      }
      return Promise.resolve(json(run));
    }),
  );
}

function renderDetail() {
  return render(
    <Providers>
      <RunDetail experimentId="exp-1" runId="run-1" />
    </Providers>,
  );
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("RunDetail", () => {
  it("follows the run to success and shows its results and files", async () => {
    const queued = makeRun();
    const succeeded = makeRun({
      status: "succeeded",
      progress: 1,
      message: "Uploaded",
      startedAt: "2026-11-26T10:00:00Z",
      finishedAt: "2026-11-26T10:00:30Z",
      result: resultFor("regression"),
    });
    stubApi(queued, [makeRun({ status: "running", progress: 0.5 }), succeeded]);

    renderDetail();

    expect(await screen.findByText("Succeeded")).toBeInTheDocument();
    expect(
      screen.getByText("Trained on 80 rows; 20 held out for testing."),
    ).toBeInTheDocument();
    expect(screen.getByText("0.877 ± 0.010")).toBeInTheDocument();
    expect(screen.getByText("max_depth: None")).toBeInTheDocument();
    expect(
      await screen.findByRole("link", { name: "Download Model (ONNX)" }),
    ).toHaveAttribute(
      "href",
      "/api/proxy/experiments/exp-1/artifacts/a-1/content?download=true",
    );
    expect(
      screen.getByRole("button", { name: "Evaluate this model" }),
    ).toBeDisabled();
  });

  it("shows a running run's progress and message", async () => {
    const running = makeRun({
      status: "running",
      progress: 0.5,
      message: "Hyperparameter search done",
    });
    // The stream reports the run still running and then drops; the page keeps the last state.
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockImplementation((url: string) =>
          Promise.resolve(
            url.endsWith("/events")
              ? sseResponse([sseEvent("status", running)])
              : json(running),
          ),
        ),
    );

    renderDetail();

    expect(await screen.findByText("Running 50%")).toBeInTheDocument();
    expect(screen.getByText("Hyperparameter search done")).toBeInTheDocument();
    expect(
      screen.getByRole("progressbar", { name: "Training progress" }),
    ).toBeInTheDocument();
  });

  it("shows a failed run's error", async () => {
    const failed = makeRun({
      status: "failed",
      error: { type: "OnnxParityError", message: "ONNX probabilities differ" },
    });
    stubApi(failed);

    renderDetail();

    expect(await screen.findByText("OnnxParityError")).toBeInTheDocument();
    expect(screen.getByText("ONNX probabilities differ")).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Evaluate this model" }),
    ).not.toBeInTheDocument();
  });
});
