import { Suspense } from "react";

import { RunDetail } from "@/components/runs/run-detail";
import { Skeleton } from "@/components/ui/skeleton";

/**
 * One training run, followed live while it trains.
 * @param params - the route params: the experiment and run IDs
 * @returns the run's page
 */
export default function RunPage({
  params,
}: PageProps<"/experiments/[id]/runs/[runId]">) {
  return (
    <Suspense fallback={<Skeleton className="h-64 w-full" />}>
      <RunDetailForParams params={params} />
    </Suspense>
  );
}

async function RunDetailForParams({
  params,
}: Pick<PageProps<"/experiments/[id]/runs/[runId]">, "params">) {
  const { id, runId } = await params;
  return <RunDetail experimentId={id} runId={runId} />;
}
