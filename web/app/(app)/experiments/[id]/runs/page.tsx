import { Suspense } from "react";

import { RunsTable } from "@/components/runs/runs-table";
import { Skeleton } from "@/components/ui/skeleton";

/**
 * The Runs tab: the experiment's training runs.
 * @param params - the route params, including the experiment ID
 * @returns the tab content
 */
export default function RunsPage({
  params,
}: PageProps<"/experiments/[id]/runs">) {
  return (
    <Suspense fallback={<Skeleton className="h-32 w-full" />}>
      <RunsTableForParams params={params} />
    </Suspense>
  );
}

async function RunsTableForParams({
  params,
}: Pick<PageProps<"/experiments/[id]/runs">, "params">) {
  const { id } = await params;
  return <RunsTable experimentId={id} />;
}
