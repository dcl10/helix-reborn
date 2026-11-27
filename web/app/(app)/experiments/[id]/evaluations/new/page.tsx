import { Suspense } from "react";

import { EvaluationForm } from "@/components/evaluations/evaluation-form";
import { Skeleton } from "@/components/ui/skeleton";

/**
 * A new evaluation of a trained model. `?runId=` preselects the run (the run page's "Evaluate this model" link).
 * @param params - the route params, including the experiment ID
 * @param searchParams - the query, with an optional `runId`
 * @returns the form
 */
export default function NewEvaluationPage({
  params,
  searchParams,
}: PageProps<"/experiments/[id]/evaluations/new">) {
  return (
    <Suspense fallback={<Skeleton className="h-96 w-full" />}>
      <EvaluationFormForParams params={params} searchParams={searchParams} />
    </Suspense>
  );
}

async function EvaluationFormForParams({
  params,
  searchParams,
}: Pick<
  PageProps<"/experiments/[id]/evaluations/new">,
  "params" | "searchParams"
>) {
  const { id } = await params;
  const { runId } = await searchParams;
  return (
    <EvaluationForm
      experimentId={id}
      initialRunId={typeof runId === "string" ? runId : undefined}
    />
  );
}
