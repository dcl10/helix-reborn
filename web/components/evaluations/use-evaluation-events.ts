"use client";

import { useQueryClient } from "@tanstack/react-query";
import { useCallback } from "react";

import {
  evaluationQueryKey,
  evaluationsQueryKey,
} from "@/components/evaluations/use-evaluations";
import {
  type JobEventsState,
  useJobEvents,
} from "@/components/jobs/use-job-events";
import { type Evaluation, evaluationEventsUrl } from "@/lib/evaluations";

/**
 * Follows an evaluation live (see {@link useJobEvents}): puts each `status` event's evaluation into the TanStack cache
 * (the evaluation and its row in the list). Returns `polling` once the stream has dropped twice, for the caller to
 * poll instead ({@link useEvaluation}'s `poll`).
 * @param experimentId - the experiment ID
 * @param evaluationId - the evaluation ID
 * @returns the connection's state
 */
export function useEvaluationEvents(
  experimentId: string,
  evaluationId: string,
): JobEventsState {
  const queryClient = useQueryClient();

  const apply = useCallback(
    async (evaluation: Evaluation) => {
      await queryClient.cancelQueries({
        queryKey: evaluationQueryKey(experimentId, evaluationId),
        exact: true,
      });
      queryClient.setQueryData(
        evaluationQueryKey(experimentId, evaluationId),
        evaluation,
      );
      queryClient.setQueryData<Evaluation[]>(
        evaluationsQueryKey(experimentId),
        (evaluations) =>
          evaluations?.map((existing) =>
            existing.id === evaluation.id ? evaluation : existing,
          ),
      );
    },
    [queryClient, experimentId, evaluationId],
  );

  return useJobEvents(evaluationEventsUrl(experimentId, evaluationId), apply);
}
