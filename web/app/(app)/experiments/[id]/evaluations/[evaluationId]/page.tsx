import { Suspense } from "react";

import { EvaluationDetail } from "@/components/evaluations/evaluation-detail";
import { Skeleton } from "@/components/ui/skeleton";

/**
 * One evaluation, followed live while it runs.
 * @param params - the route params: the experiment and evaluation IDs
 * @returns the evaluation's page
 */
export default function EvaluationPage({
  params,
}: PageProps<"/experiments/[id]/evaluations/[evaluationId]">) {
  return (
    <Suspense fallback={<Skeleton className="h-64 w-full" />}>
      <EvaluationDetailForParams params={params} />
    </Suspense>
  );
}

async function EvaluationDetailForParams({
  params,
}: Pick<PageProps<"/experiments/[id]/evaluations/[evaluationId]">, "params">) {
  const { id, evaluationId } = await params;
  return <EvaluationDetail experimentId={id} evaluationId={evaluationId} />;
}
