import { Suspense } from "react";

import { EvaluationsTable } from "@/components/evaluations/evaluations-table";
import { Skeleton } from "@/components/ui/skeleton";

/**
 * The Evaluations tab: the experiment's evaluations of its trained models.
 * @param params - the route params, including the experiment ID
 * @returns the tab content
 */
export default function EvaluationsPage({
  params,
}: PageProps<"/experiments/[id]/evaluations">) {
  return (
    <Suspense fallback={<Skeleton className="h-32 w-full" />}>
      <EvaluationsTableForParams params={params} />
    </Suspense>
  );
}

async function EvaluationsTableForParams({
  params,
}: Pick<PageProps<"/experiments/[id]/evaluations">, "params">) {
  const { id } = await params;
  return <EvaluationsTable experimentId={id} />;
}
