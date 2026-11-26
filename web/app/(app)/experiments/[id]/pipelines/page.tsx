import { Suspense } from "react";

import { PipelinesList } from "@/components/pipelines/pipelines-list";
import { Skeleton } from "@/components/ui/skeleton";

/**
 * The Pipelines tab: the experiment's saved pipelines.
 * @param params - the route params, including the experiment ID
 * @returns the tab content
 */
export default function PipelinesPage({
  params,
}: PageProps<"/experiments/[id]/pipelines">) {
  return (
    <Suspense fallback={<Skeleton className="h-32 w-full" />}>
      <PipelinesListForParams params={params} />
    </Suspense>
  );
}

async function PipelinesListForParams({
  params,
}: Pick<PageProps<"/experiments/[id]/pipelines">, "params">) {
  const { id } = await params;
  return <PipelinesList experimentId={id} />;
}
