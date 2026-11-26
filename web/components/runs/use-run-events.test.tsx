import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderHook, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { makeRun, sseEvent, sseResponse } from "@/components/runs/test-runs";
import { useRunEvents } from "@/components/runs/use-run-events";
import { runQueryKey, runsQueryKey } from "@/components/runs/use-runs";
import type { Run } from "@/lib/runs";

function setup() {
  const queryClient = new QueryClient();
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
  return { queryClient, wrapper };
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("useRunEvents", () => {
  it("applies status events to the cache and stops at end", async () => {
    const running = makeRun({
      status: "running",
      progress: 0.5,
      message: "Split data",
    });
    const succeeded = makeRun({ status: "succeeded", progress: 1 });
    const fetchMock = vi.fn().mockResolvedValue(
      sseResponse([
        sseEvent("status", makeRun()),
        // An event split across chunks still parses.
        sseEvent("status", running).slice(0, 20),
        sseEvent("status", running).slice(20),
        sseEvent("status", succeeded) +
          sseEvent("end", { status: "succeeded" }),
      ]),
    );
    vi.stubGlobal("fetch", fetchMock);
    const { queryClient, wrapper } = setup();
    queryClient.setQueryData(runsQueryKey("exp-1"), [
      makeRun(),
      makeRun({ id: "run-2" }),
    ]);

    const { result } = renderHook(() => useRunEvents("exp-1", "run-1"), {
      wrapper,
    });

    await waitFor(() => expect(result.current).toBe("ended"));
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0][0]).toBe(
      "/api/proxy/experiments/exp-1/runs/run-1/events",
    );
    expect(
      queryClient.getQueryData<Run>(runQueryKey("exp-1", "run-1"))?.status,
    ).toBe("succeeded");
    const list = queryClient.getQueryData<Run[]>(runsQueryKey("exp-1"))!;
    expect(list.map((r) => r.status)).toEqual(["succeeded", "queued"]);
  });

  it("reconnects once when the stream drops before the end", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(
        sseResponse([sseEvent("status", makeRun({ status: "running" }))]),
      )
      .mockResolvedValueOnce(
        sseResponse([
          sseEvent("status", makeRun({ status: "failed" })),
          sseEvent("end", { status: "failed" }),
        ]),
      );
    vi.stubGlobal("fetch", fetchMock);
    const { queryClient, wrapper } = setup();

    const { result } = renderHook(() => useRunEvents("exp-1", "run-1"), {
      wrapper,
    });

    await waitFor(() => expect(result.current).toBe("ended"));
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(
      queryClient.getQueryData<Run>(runQueryKey("exp-1", "run-1"))?.status,
    ).toBe("failed");
  });

  it("falls back to polling after the reconnect fails too", async () => {
    const fetchMock = vi.fn().mockRejectedValue(new TypeError("network error"));
    vi.stubGlobal("fetch", fetchMock);
    const { wrapper } = setup();

    const { result } = renderHook(() => useRunEvents("exp-1", "run-1"), {
      wrapper,
    });

    await waitFor(() => expect(result.current).toBe("polling"));
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });
});
