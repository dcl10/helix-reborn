"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import {
  createExperiment,
  deleteExperiment,
  getDataset,
  getExperiment,
  isNotFound,
  listExperiments,
  retryUnlessClientError,
  updateExperiment,
  uploadDataset,
} from "@/lib/experiments";

/** TanStack Query key for the experiments list. */
export const experimentsQueryKey = ["experiments"] as const;

/**
 * Builds the query key for one experiment.
 * @param id - the experiment ID
 * @returns the query key
 */
export function experimentQueryKey(id: string) {
  return [...experimentsQueryKey, id] as const;
}

/**
 * Builds the query key for an experiment's dataset.
 * @param experimentId - the experiment ID
 * @returns the query key
 */
export function datasetQueryKey(experimentId: string) {
  return [...experimentQueryKey(experimentId), "dataset"] as const;
}

/**
 * The caller's experiments, newest first.
 * @returns the query
 */
export function useExperiments() {
  return useQuery({
    queryKey: experimentsQueryKey,
    queryFn: listExperiments,
    retry: retryUnlessClientError,
  });
}

/**
 * One experiment.
 * @param id - the experiment ID
 * @returns the query
 */
export function useExperiment(id: string) {
  return useQuery({
    queryKey: experimentQueryKey(id),
    queryFn: () => getExperiment(id),
    retry: (failureCount, error) =>
      !isNotFound(error) && retryUnlessClientError(failureCount, error),
  });
}

/**
 * An experiment's dataset, or null once the API's 404 confirms there isn't one yet.
 * @param experimentId - the experiment ID
 * @returns the query
 */
export function useDataset(experimentId: string) {
  return useQuery({
    queryKey: datasetQueryKey(experimentId),
    queryFn: async () => {
      try {
        return await getDataset(experimentId);
      } catch (error) {
        if (isNotFound(error)) return null;
        throw error;
      }
    },
    retry: (failureCount, error) =>
      !isNotFound(error) && retryUnlessClientError(failureCount, error),
  });
}

/**
 * Creates an experiment, refreshing the list on success.
 * @returns the mutation
 */
export function useCreateExperiment() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: createExperiment,
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: experimentsQueryKey }),
  });
}

/**
 * Updates an experiment, refreshing the list and the experiment on success.
 * @returns the mutation
 */
export function useUpdateExperiment() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: updateExperiment,
    onSuccess: (experiment) => {
      queryClient.invalidateQueries({ queryKey: experimentsQueryKey });
      queryClient.setQueryData(experimentQueryKey(experiment.id), experiment);
    },
  });
}

/**
 * Deletes an experiment, refreshing the list on success.
 * @returns the mutation
 */
export function useDeleteExperiment() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: deleteExperiment,
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: experimentsQueryKey }),
  });
}

/**
 * Uploads an experiment's dataset, refreshing its dataset and the experiment (which embeds it) on success.
 * @param experimentId - the experiment ID
 * @returns the mutation
 */
export function useUploadDataset(experimentId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (file: File) => uploadDataset(experimentId, file),
    onSuccess: (dataset) => {
      queryClient.setQueryData(datasetQueryKey(experimentId), dataset);
      queryClient.invalidateQueries({
        queryKey: experimentQueryKey(experimentId),
      });
    },
  });
}
