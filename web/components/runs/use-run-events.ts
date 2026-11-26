"use client";

import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";

import { runQueryKey, runsQueryKey } from "@/components/runs/use-runs";
import { type Run, runEventsUrl } from "@/lib/runs";
import { SseParser } from "@/lib/sse";

/** Where the live connection is: streaming, finished, or given up in favour of polling. */
export type RunEventsState = "connecting" | "live" | "ended" | "polling";

/** How many times to reconnect after the stream drops before falling back to polling. */
const RECONNECTS = 1;

/**
 * Follows a run live: reads its server-sent events through the proxy with `fetch` and a stream reader, puts each
 * `status` event's run into the TanStack cache (the run and its row in the runs list), and stops at `end`. If the
 * stream drops before then it reconnects once; after that it gives up and returns `polling`, for the caller to poll
 * instead ({@link useRun}'s `poll`).
 * @param experimentId - the experiment ID
 * @param runId - the run ID
 * @returns the connection's state
 */
export function useRunEvents(
  experimentId: string,
  runId: string,
): RunEventsState {
  const queryClient = useQueryClient();
  const [state, setState] = useState<RunEventsState>("connecting");

  useEffect(() => {
    const controller = new AbortController();

    const apply = async (run: Run) => {
      // A fetch of the run still in flight would land after this and put an older state back: cancel it first.
      await queryClient.cancelQueries({
        queryKey: runQueryKey(experimentId, runId),
        exact: true,
      });
      queryClient.setQueryData(runQueryKey(experimentId, runId), run);
      queryClient.setQueryData<Run[]>(runsQueryKey(experimentId), (runs) =>
        runs?.map((existing) => (existing.id === run.id ? run : existing)),
      );
    };

    /** Reads one connection; true once the run has ended. */
    const follow = async (): Promise<boolean> => {
      const response = await fetch(runEventsUrl(experimentId, runId), {
        headers: { Accept: "text/event-stream" },
        signal: controller.signal,
      });
      if (!response.ok || !response.body)
        throw new Error(`The run's events returned ${response.status}.`);

      const reader = response.body.getReader();
      const decoder = new TextDecoder();
      const parser = new SseParser();
      setState("live");
      for (;;) {
        const { done, value } = await reader.read();
        if (done) return false;
        for (const message of parser.push(
          decoder.decode(value, { stream: true }),
        )) {
          if (message.event === "status") {
            await apply(JSON.parse(message.data) as Run);
          } else if (message.event === "end") {
            await reader.cancel();
            return true;
          }
        }
      }
    };

    void (async () => {
      for (let attempt = 0; attempt <= RECONNECTS; attempt++) {
        try {
          if (await follow()) {
            setState("ended");
            return;
          }
        } catch {
          if (controller.signal.aborted) return;
        }
        if (controller.signal.aborted) return;
      }
      setState("polling");
    })();

    return () => controller.abort();
  }, [experimentId, runId, queryClient]);

  return state;
}
