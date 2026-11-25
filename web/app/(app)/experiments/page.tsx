"use client";

import { ExperimentsTable } from "@/components/experiments/experiments-table";
import { NewExperimentDialog } from "@/components/experiments/new-experiment-dialog";
import { useExperiments } from "@/components/experiments/use-experiments";
import { Skeleton } from "@/components/ui/skeleton";

/**
 * The experiments list: a "New experiment" button and a table of the caller's experiments.
 * @returns the page content
 */
export default function ExperimentsPage() {
  const experiments = useExperiments();

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold tracking-tight">Experiments</h1>
        <NewExperimentDialog />
      </div>
      {experiments.isPending ? (
        <ExperimentsSkeleton />
      ) : experiments.isError ? (
        <p role="alert" className="text-sm text-destructive">
          Couldn&apos;t load experiments: {experiments.error.message}
        </p>
      ) : (
        <ExperimentsTable experiments={experiments.data} />
      )}
    </div>
  );
}

/**
 * Placeholder rows while the experiments load.
 * @returns the skeleton
 */
function ExperimentsSkeleton() {
  return (
    <div className="flex flex-col gap-2" aria-label="Loading experiments">
      <Skeleton className="h-10 w-full" />
      <Skeleton className="h-10 w-full" />
      <Skeleton className="h-10 w-full" />
    </div>
  );
}
