"use client";

import { useQueryClient } from "@tanstack/react-query";
import { useCallback } from "react";

import {
  type JobEventsState,
  useJobEvents,
} from "@/components/jobs/use-job-events";
import { runQueryKey, runsQueryKey } from "@/components/runs/use-runs";
import { type Run, runEventsUrl } from "@/lib/runs";

/** Where the live connection is: streaming, finished, or given up in favour of polling. */
export type RunEventsState = JobEventsState;

/**
 * Follows a run live (see {@link useJobEvents}): puts each `status` event's run into the TanStack cache (the run and
 * its row in the runs list). Returns `polling` once the stream has dropped twice, for the caller to poll instead
 * ({@link useRun}'s `poll`).
 * @param experimentId - the experiment ID
 * @param runId - the run ID
 * @returns the connection's state
 */
export function useRunEvents(
  experimentId: string,
  runId: string,
): RunEventsState {
  const queryClient = useQueryClient();

  const apply = useCallback(
    async (run: Run) => {
      // A fetch of the run still in flight would land after this and put an older state back: cancel it first.
      await queryClient.cancelQueries({
        queryKey: runQueryKey(experimentId, runId),
        exact: true,
      });
      queryClient.setQueryData(runQueryKey(experimentId, runId), run);
      queryClient.setQueryData<Run[]>(runsQueryKey(experimentId), (runs) =>
        runs?.map((existing) => (existing.id === run.id ? run : existing)),
      );
    },
    [queryClient, experimentId, runId],
  );

  return useJobEvents(runEventsUrl(experimentId, runId), apply);
}
