"use client";

import Link from "next/link";

import { RunStatusBadge } from "@/components/runs/run-status-badge";
import { useRuns } from "@/components/runs/use-runs";
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
import { experimentPath } from "@/lib/experiments";
import { formatDuration, formatMetric, headlineMetric } from "@/lib/runs";

const createdFormat = new Intl.DateTimeFormat(undefined, {
  dateStyle: "medium",
  timeStyle: "short",
});

/**
 * The Runs tab: the experiment's runs, newest first, with status, when they were created, how long they took and
 * the headline metric (CV accuracy or R²). Re-read every 3 seconds while any is queued or running.
 * @param experimentId - the experiment
 * @returns the table, or an empty state pointing at the Pipelines tab
 */
export function RunsTable({ experimentId }: { experimentId: string }) {
  const runs = useRuns(experimentId);

  if (runs.isPending) {
    return <Skeleton className="h-32 w-full" aria-label="Loading runs" />;
  }

  if (runs.isError) {
    return (
      <p role="alert" className="text-sm text-destructive">
        Couldn&apos;t load the runs: {runs.error.message}
      </p>
    );
  }

  if (runs.data.length === 0) {
    return (
      <div className="flex flex-col items-start gap-3">
        <p className="text-muted-foreground">
          No runs yet. Run a pipeline to train a model.
        </p>
        <Link
          href={experimentPath(experimentId, "pipelines")}
          className={buttonVariants({ variant: "outline" })}
        >
          Go to pipelines
        </Link>
      </div>
    );
  }

  return (
    <div className="overflow-x-auto rounded-lg border">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Pipeline</TableHead>
            <TableHead>Status</TableHead>
            <TableHead>Created</TableHead>
            <TableHead>Duration</TableHead>
            <TableHead className="text-right">CV score</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {runs.data.map((run) => {
            const metric = headlineMetric(run);
            return (
              <TableRow key={run.id}>
                <TableCell className="font-medium">
                  <Link
                    href={`${experimentPath(experimentId, "runs")}/${run.id}`}
                    className="hover:underline"
                  >
                    {run.pipelineName}
                  </Link>
                </TableCell>
                <TableCell>
                  <RunStatusBadge status={run.status} progress={run.progress} />
                </TableCell>
                <TableCell>
                  {createdFormat.format(new Date(run.createdAt))}
                </TableCell>
                <TableCell className="tabular-nums">
                  {formatDuration(run)}
                </TableCell>
                <TableCell className="text-right tabular-nums">
                  {metric ? (
                    <span title={metric.label}>
                      {formatMetric(metric.value)}{" "}
                      <span className="text-xs text-muted-foreground">
                        {metric.label}
                      </span>
                    </span>
                  ) : (
                    "—"
                  )}
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </div>
  );
}
