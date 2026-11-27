"use client";

import { Download, FlaskConical, TriangleAlert } from "lucide-react";
import Link from "next/link";

import { RunStatusBadge } from "@/components/runs/run-status-badge";
import { useRunEvents } from "@/components/runs/use-run-events";
import { useRun, useRunArtifacts } from "@/components/runs/use-runs";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { buttonVariants } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Progress } from "@/components/ui/progress";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { artifactContentUrl } from "@/lib/eda";
import { experimentPath } from "@/lib/experiments";
import { formatGridValue } from "@/lib/pipelines";
import { formatDuration, isActive, type Run } from "@/lib/runs";

const dateFormat = new Intl.DateTimeFormat(undefined, {
  dateStyle: "medium",
  timeStyle: "short",
});
const metricFormat = new Intl.NumberFormat(undefined, {
  maximumFractionDigits: 3,
  minimumFractionDigits: 3,
});

/** `{a: 1, b: null}` as `a: 1, b: None`. */
function describeParams(params: Record<string, unknown>): string {
  const entries = Object.entries(params);
  return entries.length === 0
    ? "defaults"
    : entries
        .map(([key, value]) => `${key}: ${formatGridValue(value)}`)
        .join(", ");
}

/** What the run trained: data, steps, model and search. */
function SpecPanel({ run }: { run: Run }) {
  const { spec } = run;
  return (
    <Card>
      <CardHeader>
        <CardTitle>Pipeline</CardTitle>
      </CardHeader>
      <CardContent>
        <dl className="grid gap-x-6 gap-y-3 text-sm sm:grid-cols-[max-content_1fr]">
          <dt className="text-muted-foreground">Problem</dt>
          <dd className="capitalize">{spec.problem_type}</dd>
          <dt className="text-muted-foreground">Target</dt>
          <dd>{spec.target_column}</dd>
          <dt className="text-muted-foreground">Features</dt>
          <dd>
            {spec.feature_columns.length} column
            {spec.feature_columns.length === 1 ? "" : "s"}
          </dd>
          <dt className="text-muted-foreground">Split</dt>
          <dd>
            {Math.round(spec.split.test_size * 100)}% test, seed{" "}
            {spec.split.random_seed}
            {spec.split.cv_folds
              ? `, ${spec.split.cv_folds}-fold cross-validation`
              : ""}
          </dd>
          <dt className="text-muted-foreground">Steps</dt>
          <dd>
            {spec.steps.length === 0 ? (
              "None"
            ) : (
              <ol className="list-decimal pl-4">
                {spec.steps.map((step, i) => (
                  <li key={`${step.step}-${i}`}>
                    <span className="font-medium">{step.step}</span>{" "}
                    <span className="text-muted-foreground">
                      ({describeParams(step.params)})
                    </span>
                  </li>
                ))}
              </ol>
            )}
          </dd>
          <dt className="text-muted-foreground">Model</dt>
          <dd>
            <span className="font-medium">{spec.model.step}</span>{" "}
            <span className="text-muted-foreground">
              ({describeParams(spec.model.params)})
            </span>
          </dd>
          <dt className="text-muted-foreground">Search</dt>
          <dd>
            {spec.search
              ? `${spec.search.method}, ${spec.search.cv_folds} folds${
                  spec.search.n_iter ? `, ${spec.search.n_iter} candidates` : ""
                }, ${spec.search.param_grid ? "custom grid" : "default grid"}`
              : "None"}
          </dd>
        </dl>
      </CardContent>
    </Card>
  );
}

/** Rows used, cross-validation metrics and the search's best params. */
function ResultsPanel({ run }: { run: Run }) {
  const result = run.result;
  if (!result) return null;
  return (
    <Card>
      <CardHeader>
        <CardTitle>Results</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-4 text-sm">
        <p>
          Trained on {result.train_rows} rows; {result.test_rows} held out for
          testing.
        </p>
        {result.cv_metrics ? (
          <div className="overflow-x-auto rounded-lg border">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Cross-validation metric</TableHead>
                  <TableHead className="text-right">Mean ± std</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {Object.entries(result.cv_metrics).map(
                  ([name, { mean, std }]) => (
                    <TableRow key={name}>
                      <TableCell>{name}</TableCell>
                      <TableCell className="text-right tabular-nums">
                        {metricFormat.format(mean)} ± {metricFormat.format(std)}
                      </TableCell>
                    </TableRow>
                  ),
                )}
              </TableBody>
            </Table>
          </div>
        ) : (
          <p className="text-muted-foreground">
            No cross-validation: the pipeline didn&apos;t ask for it.
          </p>
        )}
        {result.best_params && (
          <div>
            <p className="mb-1 font-medium">Best parameters from the search</p>
            <div className="flex flex-wrap gap-1.5">
              {Object.entries(result.best_params).map(([key, value]) => (
                <Badge key={key} variant="secondary">
                  {key}: {formatGridValue(value)}
                </Badge>
              ))}
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

/** The run's files: model, metadata, split and CV metrics, with downloads. */
function FilesPanel({
  experimentId,
  runId,
}: {
  experimentId: string;
  runId: string;
}) {
  const files = useRunArtifacts(experimentId, runId, true);
  return (
    <Card>
      <CardHeader>
        <CardTitle>Files</CardTitle>
      </CardHeader>
      <CardContent>
        {files.isPending ? (
          <Skeleton className="h-16 w-full" />
        ) : files.isError ? (
          <p role="alert" className="text-sm text-destructive">
            Couldn&apos;t load the files: {files.error.message}
          </p>
        ) : (
          <ul className="flex flex-col gap-2 text-sm">
            {files.data.map((file) => (
              <li
                key={file.id}
                className="flex items-center justify-between gap-2"
              >
                <span>
                  {file.title}{" "}
                  <span className="text-muted-foreground">
                    ({file.fileName})
                  </span>
                </span>
                <a
                  href={artifactContentUrl(experimentId, file.id, true)}
                  download={file.fileName}
                  className={buttonVariants({
                    variant: "ghost",
                    size: "icon-sm",
                  })}
                  aria-label={`Download ${file.title}`}
                >
                  <Download />
                </a>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  );
}

/**
 * One run: its status, progress and latest message, kept live over server-sent events (falling back to polling if the
 * stream keeps dropping), then the pipeline it ran, and once it's done its results and files, or its error.
 * @param experimentId - the experiment
 * @param runId - the run
 * @returns the run's page content
 */
export function RunDetail({
  experimentId,
  runId,
}: {
  experimentId: string;
  runId: string;
}) {
  const events = useRunEvents(experimentId, runId);
  const run = useRun(experimentId, runId, events === "polling");

  if (run.isPending) {
    return <Skeleton className="h-64 w-full" aria-label="Loading the run" />;
  }

  if (run.isError) {
    return (
      <p role="alert" className="text-sm text-destructive">
        Couldn&apos;t load the run: {run.error.message}
      </p>
    );
  }

  const data = run.data;
  const progress = data.status === "succeeded" ? 1 : (data.progress ?? 0);

  return (
    <div className="flex flex-col gap-6">
      <Card>
        <CardHeader>
          <CardTitle className="flex flex-wrap items-center gap-3">
            {data.pipelineName}
            <RunStatusBadge status={data.status} progress={data.progress} />
          </CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-3">
          {isActive(data.status) && (
            <Progress
              value={Math.round(progress * 100)}
              aria-label="Training progress"
            />
          )}
          {data.message && <p className="text-sm">{data.message}</p>}
          <dl className="grid grid-cols-2 gap-x-6 gap-y-1 text-sm sm:grid-cols-4">
            <div>
              <dt className="text-muted-foreground">Created</dt>
              <dd>{dateFormat.format(new Date(data.createdAt))}</dd>
            </div>
            <div>
              <dt className="text-muted-foreground">Started</dt>
              <dd>
                {data.startedAt
                  ? dateFormat.format(new Date(data.startedAt))
                  : "—"}
              </dd>
            </div>
            <div>
              <dt className="text-muted-foreground">Duration</dt>
              <dd className="tabular-nums">{formatDuration(data)}</dd>
            </div>
            <div>
              <dt className="text-muted-foreground">Updates</dt>
              <dd>
                {events === "polling"
                  ? "Every 3 seconds"
                  : isActive(data.status)
                    ? "Live"
                    : "Finished"}
              </dd>
            </div>
          </dl>
          {data.status === "succeeded" && (
            <div>
              <Link
                href={`${experimentPath(experimentId, "evaluations")}/new?${new URLSearchParams({ runId })}`}
                className={buttonVariants({ variant: "outline" })}
              >
                <FlaskConical />
                Evaluate this model
              </Link>
            </div>
          )}
        </CardContent>
      </Card>

      {data.status === "failed" && data.error && (
        <Alert variant="destructive">
          <TriangleAlert />
          <AlertTitle>{data.error.type}</AlertTitle>
          <AlertDescription>{data.error.message}</AlertDescription>
        </Alert>
      )}

      {data.status === "succeeded" && <ResultsPanel run={data} />}
      <SpecPanel run={data} />
      {data.status === "succeeded" && (
        <FilesPanel experimentId={experimentId} runId={runId} />
      )}
    </div>
  );
}
