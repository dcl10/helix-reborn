"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { experimentQueryKey } from "@/components/experiments/use-experiments";
import { isNotFound, retryUnlessClientError } from "@/lib/api";
import {
  createPipeline,
  deletePipeline,
  getPipeline,
  getPipelineCatalog,
  listPipelines,
  type SavePipelineInput,
  updatePipeline,
} from "@/lib/pipelines";

/** TanStack Query key for the pipeline catalog. */
export const pipelineCatalogQueryKey = ["pipeline-catalog"] as const;

/**
 * Builds the query key for an experiment's pipelines.
 * @param experimentId - the experiment ID
 * @returns the query key
 */
export function pipelinesQueryKey(experimentId: string) {
  return [...experimentQueryKey(experimentId), "pipelines"] as const;
}

/**
 * Builds the query key for one pipeline.
 * @param experimentId - the experiment ID
 * @param pipelineId - the pipeline ID
 * @returns the query key
 */
export function pipelineQueryKey(experimentId: string, pipelineId: string) {
  return [...pipelinesQueryKey(experimentId), pipelineId] as const;
}

/**
 * The pipeline catalog, fetched once per session: it only changes on deploy.
 * @returns the query
 */
export function usePipelineCatalog() {
  return useQuery({
    queryKey: pipelineCatalogQueryKey,
    queryFn: getPipelineCatalog,
    staleTime: Infinity,
    retry: retryUnlessClientError,
  });
}

/**
 * An experiment's pipelines, by name.
 * @param experimentId - the experiment ID
 * @returns the query
 */
export function usePipelines(experimentId: string) {
  return useQuery({
    queryKey: pipelinesQueryKey(experimentId),
    queryFn: () => listPipelines(experimentId),
    retry: retryUnlessClientError,
  });
}

/**
 * One pipeline.
 * @param experimentId - the experiment ID
 * @param pipelineId - the pipeline ID
 * @returns the query
 */
export function usePipeline(experimentId: string, pipelineId: string) {
  return useQuery({
    queryKey: pipelineQueryKey(experimentId, pipelineId),
    queryFn: () => getPipeline(experimentId, pipelineId),
    retry: (failureCount, error) =>
      !isNotFound(error) && retryUnlessClientError(failureCount, error),
  });
}

/**
 * Saves a pipeline: creates it, or replaces the one with `pipelineId`. Refreshes the list on success.
 * @param experimentId - the experiment ID
 * @param pipelineId - the pipeline to replace, or undefined to create one
 * @returns the mutation
 */
export function useSavePipeline(experimentId: string, pipelineId?: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (input: SavePipelineInput) =>
      pipelineId
        ? updatePipeline(experimentId, pipelineId, input)
        : createPipeline(experimentId, input),
    onSuccess: (pipeline) => {
      queryClient.setQueryData(
        pipelineQueryKey(experimentId, pipeline.id),
        pipeline,
      );
      return queryClient.invalidateQueries({
        queryKey: pipelinesQueryKey(experimentId),
      });
    },
  });
}

/**
 * Deletes a pipeline, refreshing the list on success.
 * @param experimentId - the experiment ID
 * @returns the mutation
 */
export function useDeletePipeline(experimentId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (pipelineId: string) =>
      deletePipeline(experimentId, pipelineId),
    onSuccess: () =>
      queryClient.invalidateQueries({
        queryKey: pipelinesQueryKey(experimentId),
      }),
  });
}
