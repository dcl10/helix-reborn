"use client";

import { PipelineEditor } from "@/components/pipelines/pipeline-editor";
import { RunPipelineButton } from "@/components/pipelines/run-pipeline-button";
import { usePipeline } from "@/components/pipelines/use-pipelines";
import { Skeleton } from "@/components/ui/skeleton";

/**
 * Loads a saved pipeline into the {@link PipelineEditor}, with a Run button for the saved version above it.
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
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-muted-foreground">
          Run trains the saved version; save any changes first.
        </p>
        <RunPipelineButton
          experimentId={experimentId}
          pipelineId={pipeline.data.id}
          pipelineName={pipeline.data.name}
        />
      </div>
      <PipelineEditor experimentId={experimentId} pipeline={pipeline.data} />
    </div>
  );
}
