"use client";

import { Suspense } from "react";

import { DeleteExperimentButton } from "@/components/experiments/delete-experiment-button";
import { EditExperimentDialog } from "@/components/experiments/edit-experiment-dialog";
import {
  ExperimentTabLinks,
  ExperimentTabs,
} from "@/components/experiments/experiment-tabs";
import { useExperiment } from "@/components/experiments/use-experiments";
import { Skeleton } from "@/components/ui/skeleton";

/**
 * The experiment layout's header: its name, description, an edit dialog, a delete button, and the tab
 * navigation below them.
 * @param experimentId - the experiment ID, from the route
 * @returns the header and tab navigation
 */
export function ExperimentHeader({ experimentId }: { experimentId: string }) {
  const experiment = useExperiment(experimentId);

  return (
    <div className="flex flex-col gap-4">
      {experiment.isPending ? (
        <Skeleton className="h-9 w-64" />
      ) : experiment.isError ? (
        <p role="alert" className="text-sm text-destructive">
          Couldn&apos;t load this experiment: {experiment.error.message}
        </p>
      ) : (
        <div className="flex items-start justify-between gap-4">
          <div>
            <h1 className="text-2xl font-semibold tracking-tight">
              {experiment.data.name}
            </h1>
            {experiment.data.description && (
              <p className="text-muted-foreground">
                {experiment.data.description}
              </p>
            )}
          </div>
          <div className="flex shrink-0 gap-2">
            <EditExperimentDialog experiment={experiment.data} />
            <DeleteExperimentButton experiment={experiment.data} />
          </div>
        </div>
      )}
      <Suspense fallback={<ExperimentTabLinks experimentId={experimentId} />}>
        <ExperimentTabs experimentId={experimentId} />
      </Suspense>
    </div>
  );
}
