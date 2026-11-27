"use client";

import { Plus } from "lucide-react";
import Link from "next/link";

import { useEvaluations } from "@/components/evaluations/use-evaluations";
import { RunStatusBadge } from "@/components/runs/run-status-badge";
import { buttonVariants } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { headlineTestMetric } from "@/lib/evaluations";
import { experimentPath } from "@/lib/experiments";
import { formatDuration, formatMetric } from "@/lib/runs";

const createdFormat = new Intl.DateTimeFormat(undefined, {
  dateStyle: "medium",
  timeStyle: "short",
});

/**
 * The Evaluations tab: the experiment's evaluations, newest first, with the run's pipeline, status, the headline
 * test metric (accuracy or R²), when they were created and how long they took. Re-read every 3 seconds while any is
 * queued or running.
 * @param experimentId - the experiment
 * @returns the table with a "New evaluation" link, or an empty state
 */
export function EvaluationsTable({ experimentId }: { experimentId: string }) {
  const evaluations = useEvaluations(experimentId);
  const base = experimentPath(experimentId, "evaluations");

  const newLink = (
    <Link
      href={`${base}/new`}
      className={buttonVariants({ variant: "outline" })}
    >
      <Plus />
      New evaluation
    </Link>
  );

  if (evaluations.isPending) {
    return (
      <Skeleton className="h-32 w-full" aria-label="Loading evaluations" />
    );
  }

  if (evaluations.isError) {
    return (
      <p role="alert" className="text-sm text-destructive">
        Couldn&apos;t load the evaluations: {evaluations.error.message}
      </p>
    );
  }

  if (evaluations.data.length === 0) {
    return (
      <div className="flex flex-col items-start gap-3">
        <p className="text-muted-foreground">
          No evaluations yet. Evaluate a trained model to see its metrics, plots
          and feature importance.
        </p>
        {newLink}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-4">
      <div>{newLink}</div>
      <div className="overflow-x-auto rounded-lg border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Run</TableHead>
              <TableHead>Status</TableHead>
              <TableHead className="text-right">Test score</TableHead>
              <TableHead>Created</TableHead>
              <TableHead>Duration</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {evaluations.data.map((evaluation) => {
              const metric = headlineTestMetric(evaluation);
              return (
                <TableRow key={evaluation.id}>
                  <TableCell className="font-medium">
                    <Link
                      href={`${base}/${evaluation.id}`}
                      className="hover:underline"
                    >
                      {evaluation.pipelineName}
                    </Link>
                  </TableCell>
                  <TableCell>
                    <RunStatusBadge
                      status={evaluation.status}
                      progress={evaluation.progress}
                    />
                  </TableCell>
                  <TableCell className="text-right tabular-nums">
                    {metric ? (
                      <span>
                        {formatMetric(metric.value)}{" "}
                        <span className="text-xs text-muted-foreground">
                          {metric.label}
                        </span>
                      </span>
                    ) : (
                      "—"
                    )}
                  </TableCell>
                  <TableCell>
                    {createdFormat.format(new Date(evaluation.createdAt))}
                  </TableCell>
                  <TableCell className="tabular-nums">
                    {formatDuration(evaluation)}
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </div>
    </div>
  );
}
