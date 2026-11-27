"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { experimentQueryKey } from "@/components/experiments/use-experiments";
import { isNotFound, readApiError, retryUnlessClientError } from "@/lib/api";
import { type Artifact, artifactContentUrl } from "@/lib/eda";
import {
  type Evaluation,
  type EvaluationOptions,
  getEvaluation,
  listEvaluationArtifacts,
  listEvaluations,
  type MetricsFile,
  startEvaluation,
} from "@/lib/evaluations";
import { isActive } from "@/lib/runs";

/** How often evaluations are re-read while one is queued or running and the live stream isn't available. */
export const EVALUATIONS_POLL_MS = 3000;

/**
 * Builds the query key for an experiment's evaluations.
 * @param experimentId - the experiment ID
 * @returns the query key
 */
export function evaluationsQueryKey(experimentId: string) {
  return [...experimentQueryKey(experimentId), "evaluations"] as const;
}

/**
 * Builds the query key for one evaluation.
 * @param experimentId - the experiment ID
 * @param evaluationId - the evaluation ID
 * @returns the query key
 */
export function evaluationQueryKey(experimentId: string, evaluationId: string) {
  return [...evaluationsQueryKey(experimentId), evaluationId] as const;
}

/**
 * An experiment's evaluations, newest first, re-read every 3 seconds while any is queued or running.
 * @param experimentId - the experiment ID
 * @returns the query
 */
export function useEvaluations(experimentId: string) {
  return useQuery({
    queryKey: evaluationsQueryKey(experimentId),
    queryFn: () => listEvaluations(experimentId),
    retry: retryUnlessClientError,
    refetchInterval: (query) =>
      query.state.data?.some((evaluation: Evaluation) =>
        isActive(evaluation.status),
      )
        ? EVALUATIONS_POLL_MS
        : false,
  });
}

/**
 * One evaluation. Normally kept fresh by {@link useEvaluationEvents}; with `poll`, it's also re-read every 3 seconds
 * while in progress, the fallback when the event stream can't be kept open.
 * @param experimentId - the experiment ID
 * @param evaluationId - the evaluation ID
 * @param poll - whether to poll while it's in progress
 * @returns the query
 */
export function useEvaluation(
  experimentId: string,
  evaluationId: string,
  poll = false,
) {
  return useQuery({
    queryKey: evaluationQueryKey(experimentId, evaluationId),
    queryFn: () => getEvaluation(experimentId, evaluationId),
    retry: (failureCount, error) =>
      !isNotFound(error) && retryUnlessClientError(failureCount, error),
    refetchInterval: (query) =>
      poll && query.state.data && isActive(query.state.data.status)
        ? EVALUATIONS_POLL_MS
        : false,
  });
}

/**
 * An evaluation's files (metrics, plots, tables), once it has succeeded.
 * @param experimentId - the experiment ID
 * @param evaluationId - the evaluation ID
 * @param enabled - whether to fetch them yet
 * @returns the query
 */
export function useEvaluationArtifacts(
  experimentId: string,
  evaluationId: string,
  enabled: boolean,
) {
  return useQuery({
    queryKey: [...evaluationQueryKey(experimentId, evaluationId), "artifacts"],
    queryFn: () => listEvaluationArtifacts(experimentId, evaluationId),
    enabled,
    retry: retryUnlessClientError,
  });
}

/**
 * Reads a JSON or text artifact's content through the proxy.
 * @param experimentId - the experiment ID
 * @param artifact - the artifact, or undefined while it isn't known
 * @param as - whether to parse the content as JSON or return it as text
 * @returns the query
 */
export function useArtifactContent<T = string>(
  experimentId: string,
  artifact: Artifact | undefined,
  as: "json" | "text",
) {
  return useQuery({
    queryKey: [
      ...experimentQueryKey(experimentId),
      "artifacts",
      artifact?.id,
      "content",
      as,
    ],
    queryFn: async (): Promise<T> => {
      const response = await fetch(
        artifactContentUrl(experimentId, artifact!.id),
      );
      if (!response.ok) throw await readApiError(response);
      return (
        as === "json" ? await response.json() : await response.text()
      ) as T;
    },
    enabled: artifact !== undefined,
    retry: retryUnlessClientError,
    staleTime: Infinity,
  });
}

/**
 * The evaluation's `metrics.json`: train and test metrics.
 * @param experimentId - the experiment ID
 * @param artifacts - the evaluation's artifacts, once loaded
 * @returns the query
 */
export function useMetricsFile(
  experimentId: string,
  artifacts: Artifact[] | undefined,
) {
  return useArtifactContent<MetricsFile>(
    experimentId,
    artifacts?.find((artifact) => artifact.kind === "metrics"),
    "json",
  );
}

/**
 * Starts an evaluation, caching it and refreshing the evaluations list. Never retried: a 400, 409 or 503 won't change
 * on retry, and a retried success would start a second evaluation.
 * @param experimentId - the experiment ID
 * @returns the mutation
 */
export function useStartEvaluation(experimentId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      runId,
      options,
    }: {
      runId: string;
      options: EvaluationOptions;
    }) => startEvaluation(experimentId, runId, options),
    retry: false,
    onSuccess: (evaluation) => {
      queryClient.setQueryData(
        evaluationQueryKey(experimentId, evaluation.id),
        evaluation,
      );
      return queryClient.invalidateQueries({
        queryKey: evaluationsQueryKey(experimentId),
        exact: true,
      });
    },
  });
}
