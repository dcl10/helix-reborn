"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { experimentQueryKey } from "@/components/experiments/use-experiments";
import { isNotFound, retryUnlessClientError } from "@/lib/api";
import {
  getRun,
  isActive,
  listRunArtifacts,
  listRuns,
  type Run,
  startRun,
} from "@/lib/runs";

/** How often the runs list is re-read while a run is queued or running. */
export const RUNS_POLL_MS = 3000;

/**
 * Builds the query key for an experiment's runs.
 * @param experimentId - the experiment ID
 * @returns the query key
 */
export function runsQueryKey(experimentId: string) {
  return [...experimentQueryKey(experimentId), "runs"] as const;
}

/**
 * Builds the query key for one run.
 * @param experimentId - the experiment ID
 * @param runId - the run ID
 * @returns the query key
 */
export function runQueryKey(experimentId: string, runId: string) {
  return [...runsQueryKey(experimentId), runId] as const;
}

/**
 * An experiment's runs, newest first, re-read every 3 seconds while any is queued or running.
 * @param experimentId - the experiment ID
 * @returns the query
 */
export function useRuns(experimentId: string) {
  return useQuery({
    queryKey: runsQueryKey(experimentId),
    queryFn: () => listRuns(experimentId),
    retry: retryUnlessClientError,
    refetchInterval: (query) =>
      query.state.data?.some((run: Run) => isActive(run.status))
        ? RUNS_POLL_MS
        : false,
  });
}

/**
 * One run. Normally kept fresh by {@link useRunEvents}; with `poll`, it's also re-read every 3 seconds while in
 * progress, the fallback when the event stream can't be kept open.
 * @param experimentId - the experiment ID
 * @param runId - the run ID
 * @param poll - whether to poll while the run is in progress
 * @returns the query
 */
export function useRun(experimentId: string, runId: string, poll = false) {
  return useQuery({
    queryKey: runQueryKey(experimentId, runId),
    queryFn: () => getRun(experimentId, runId),
    retry: (failureCount, error) =>
      !isNotFound(error) && retryUnlessClientError(failureCount, error),
    refetchInterval: (query) =>
      poll && query.state.data && isActive(query.state.data.status)
        ? RUNS_POLL_MS
        : false,
  });
}

/**
 * A run's files, once it has succeeded.
 * @param experimentId - the experiment ID
 * @param runId - the run ID
 * @param enabled - whether to fetch them yet
 * @returns the query
 */
export function useRunArtifacts(
  experimentId: string,
  runId: string,
  enabled: boolean,
) {
  return useQuery({
    queryKey: [...runQueryKey(experimentId, runId), "artifacts"],
    queryFn: () => listRunArtifacts(experimentId, runId),
    enabled,
    retry: retryUnlessClientError,
  });
}

/**
 * Starts a run of a pipeline, caching it and refreshing the runs list. Never retried: a 400 or 503 won't change on
 * retry, and a retried success would start a second run.
 * @param experimentId - the experiment ID
 * @returns the mutation
 */
export function useStartRun(experimentId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (pipelineId: string) => startRun(experimentId, pipelineId),
    retry: false,
    onSuccess: (run) => {
      queryClient.setQueryData(runQueryKey(experimentId, run.id), run);
      return queryClient.invalidateQueries({
        queryKey: runsQueryKey(experimentId),
        exact: true,
      });
    },
  });
}
