import { Suspense } from "react";

import { EditPipeline } from "@/components/pipelines/edit-pipeline";
import { Skeleton } from "@/components/ui/skeleton";

/**
 * A saved pipeline, in the pipeline editor.
 * @param params - the route params: the experiment and pipeline IDs
 * @returns the editor
 */
export default function EditPipelinePage({
  params,
}: PageProps<"/experiments/[id]/pipelines/[pipelineId]">) {
  return (
    <Suspense fallback={<Skeleton className="h-96 w-full" />}>
      <EditPipelineForParams params={params} />
    </Suspense>
  );
}

async function EditPipelineForParams({
  params,
}: Pick<PageProps<"/experiments/[id]/pipelines/[pipelineId]">, "params">) {
  const { id, pipelineId } = await params;
  return <EditPipeline experimentId={id} pipelineId={pipelineId} />;
}
