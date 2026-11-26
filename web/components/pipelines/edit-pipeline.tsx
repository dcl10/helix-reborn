"use client";

import { PipelineEditor } from "@/components/pipelines/pipeline-editor";
import { usePipeline } from "@/components/pipelines/use-pipelines";
import { Skeleton } from "@/components/ui/skeleton";

/**
 * Loads a saved pipeline into the {@link PipelineEditor}.
 * @param experimentId - the experiment
 * @param pipelineId - the pipeline
 * @returns the editor, or a loading state or error
 */
export function EditPipeline({
  experimentId,
  pipelineId,
}: {
  experimentId: string;
  pipelineId: string;
}) {
  const pipeline = usePipeline(experimentId, pipelineId);

  if (pipeline.isPending) {
    return (
      <Skeleton className="h-96 w-full" aria-label="Loading the pipeline" />
    );
  }

  if (pipeline.isError) {
    return (
      <p role="alert" className="text-sm text-destructive">
        Couldn&apos;t load the pipeline: {pipeline.error.message}
      </p>
    );
  }

  return (
    <PipelineEditor experimentId={experimentId} pipeline={pipeline.data} />
  );
}
