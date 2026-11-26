"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { experimentQueryKey } from "@/components/experiments/use-experiments";
import { ApiError, retryUnlessClientError } from "@/lib/api";
import {
  createPlot,
  getEdaSummary,
  listEdaArtifacts,
  type PlotRequest,
  runNormalityTests,
} from "@/lib/eda";

/**
 * Builds the query key for everything EDA under an experiment.
 * @param experimentId - the experiment ID
 * @returns the query key
 */
export function edaQueryKey(experimentId: string) {
  return [...experimentQueryKey(experimentId), "eda"] as const;
}

/**
 * Builds the query key for an experiment's EDA summary.
 * @param experimentId - the experiment ID
 * @returns the query key
 */
export function edaSummaryQueryKey(experimentId: string) {
  return [...edaQueryKey(experimentId), "summary"] as const;
}

/**
 * Builds the query key for an experiment's EDA artifacts.
 * @param experimentId - the experiment ID
 * @returns the query key
 */
export function edaArtifactsQueryKey(experimentId: string) {
  return [...edaQueryKey(experimentId), "artifacts"] as const;
}

/**
 * The experiment's dataset summary, or null once the API's 409 confirms there's no dataset yet.
 * @param experimentId - the experiment ID
 * @returns the query
 */
export function useEdaSummary(experimentId: string) {
  return useQuery({
    queryKey: edaSummaryQueryKey(experimentId),
    queryFn: async () => {
      try {
        return await getEdaSummary(experimentId);
      } catch (error) {
        if (error instanceof ApiError && error.status === 409) return null;
        throw error;
      }
    },
    retry: retryUnlessClientError,
  });
}

/**
 * The experiment's EDA plots and tables, oldest first.
 * @param experimentId - the experiment ID
 * @returns the query
 */
export function useEdaArtifacts(experimentId: string) {
  return useQuery({
    queryKey: edaArtifactsQueryKey(experimentId),
    queryFn: () => listEdaArtifacts(experimentId),
    retry: retryUnlessClientError,
  });
}

/**
 * Renders a plot, refreshing the EDA artifacts on success. Never retried: a plot can take a minute, and a failure
 * (a bad column) won't change on retry.
 * @param experimentId - the experiment ID
 * @returns the mutation
 */
export function useCreatePlot(experimentId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: PlotRequest) => createPlot(experimentId, request),
    retry: false,
    onSuccess: () =>
      queryClient.invalidateQueries({
        queryKey: edaArtifactsQueryKey(experimentId),
      }),
  });
}

/**
 * Runs normality tests on every numeric column, refreshing the EDA artifacts on success (the table is saved as a
 * CSV). Never retried.
 * @param experimentId - the experiment ID
 * @returns the mutation
 */
export function useRunNormalityTests(experimentId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => runNormalityTests(experimentId, null),
    retry: false,
    onSuccess: () =>
      queryClient.invalidateQueries({
        queryKey: edaArtifactsQueryKey(experimentId),
      }),
  });
}
