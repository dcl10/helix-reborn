import { Suspense } from "react";

import { PipelineEditor } from "@/components/pipelines/pipeline-editor";
import { Skeleton } from "@/components/ui/skeleton";

/**
 * A new pipeline, in the pipeline editor.
 * @param params - the route params, including the experiment ID
 * @returns the editor
 */
export default function NewPipelinePage({
  params,
}: PageProps<"/experiments/[id]/pipelines/new">) {
  return (
    <Suspense fallback={<Skeleton className="h-96 w-full" />}>
      <NewPipelineForParams params={params} />
    </Suspense>
  );
}

async function NewPipelineForParams({
  params,
}: Pick<PageProps<"/experiments/[id]/pipelines/new">, "params">) {
  const { id } = await params;
  return <PipelineEditor experimentId={id} />;
}
