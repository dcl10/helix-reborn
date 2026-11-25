import { Suspense } from "react";

import { DatasetOverview } from "@/components/experiments/dataset-overview";
import { Skeleton } from "@/components/ui/skeleton";

/**
 * The Data tab: dataset upload when the experiment has none yet, or a summary and columns table.
 * @param params - the route params, including the experiment ID
 * @returns the tab content
 */
export default function DataPage({
  params,
}: PageProps<"/experiments/[id]/data">) {
  return (
    <Suspense fallback={<Skeleton className="h-32 w-full" />}>
      <DatasetOverviewForParams params={params} />
    </Suspense>
  );
}

async function DatasetOverviewForParams({
  params,
}: Pick<PageProps<"/experiments/[id]/data">, "params">) {
  const { id } = await params;
  return <DatasetOverview experimentId={id} />;
}
