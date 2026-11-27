"use client";

import { ChevronDown, Download, TriangleAlert } from "lucide-react";
import Link from "next/link";

import { ArtifactGallery } from "@/components/artifacts/artifact-gallery";
import { CsvTable } from "@/components/evaluations/csv-table";
import { MetricsTable } from "@/components/evaluations/metrics-table";
import { useEvaluationEvents } from "@/components/evaluations/use-evaluation-events";
import {
  useEvaluation,
  useEvaluationArtifacts,
  useMetricsFile,
} from "@/components/evaluations/use-evaluations";
import { RunStatusBadge } from "@/components/runs/run-status-badge";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button, buttonVariants } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible";
import { Progress } from "@/components/ui/progress";
import { Skeleton } from "@/components/ui/skeleton";
import { type Artifact, artifactContentUrl } from "@/lib/eda";
import { groupEvaluationPlots } from "@/lib/evaluations";
import { experimentPath } from "@/lib/experiments";
import { formatDuration, isActive } from "@/lib/runs";

const dateFormat = new Intl.DateTimeFormat(undefined, {
  dateStyle: "medium",
  timeStyle: "short",
});

/** Train vs test metrics, read from the `metrics.json` artifact. */
function MetricsPanel({
  experimentId,
  artifacts,
}: {
  experimentId: string;
  artifacts: Artifact[];
}) {
  const metrics = useMetricsFile(experimentId, artifacts);
  return (
    <Card>
      <CardHeader>
        <CardTitle>Metrics</CardTitle>
      </CardHeader>
      <CardContent>
        {!artifacts.some((artifact) => artifact.kind === "metrics") ? (
          <p className="text-sm text-muted-foreground">No metrics file.</p>
        ) : metrics.isPending ? (
          <Skeleton className="h-24 w-full" aria-label="Loading metrics" />
        ) : metrics.isError ? (
          <p role="alert" className="text-sm text-destructive">
            Couldn&apos;t load the metrics: {metrics.error.message}
          </p>
        ) : (
          <MetricsTable metrics={metrics.data} />
        )}
      </CardContent>
    </Card>
  );
}

/** The plots, in sections by kind. */
function PlotsPanel({
  experimentId,
  artifacts,
}: {
  experimentId: string;
  artifacts: Artifact[];
}) {
  const groups = groupEvaluationPlots(artifacts);
  return (
    <Card>
      <CardHeader>
        <CardTitle>Plots</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-6">
        {groups.length === 0 ? (
          <p className="text-sm text-muted-foreground">No plots.</p>
        ) : (
          groups.map((group) => (
            <section key={group.title} className="flex flex-col gap-3">
              <h3 className="font-medium">{group.title}</h3>
              <ArtifactGallery
                experimentId={experimentId}
                plots={group.plots}
              />
            </section>
          ))
        )}
      </CardContent>
    </Card>
  );
}

/** The tables, each collapsible with a preview and a download link. */
function TablesPanel({
  experimentId,
  artifacts,
}: {
  experimentId: string;
  artifacts: Artifact[];
}) {
  const tables = artifacts.filter(
    (artifact) => artifact.kind === "evaluationTable",
  );
  if (tables.length === 0) return null;
  return (
    <Card>
      <CardHeader>
        <CardTitle>Tables</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-2">
        {tables.map((table) => (
          <Collapsible key={table.id}>
            <div className="flex items-center justify-between gap-2">
              <CollapsibleTrigger
                render={
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    className="group -ml-2"
                  />
                }
              >
                <ChevronDown className="transition-transform group-data-panel-open:rotate-180" />
                {table.title}
              </CollapsibleTrigger>
              <a
                href={artifactContentUrl(experimentId, table.id, true)}
                download={table.fileName}
                className={buttonVariants({
                  variant: "ghost",
                  size: "icon-sm",
                })}
                aria-label={`Download ${table.title}`}
              >
                <Download />
              </a>
            </div>
            <CollapsibleContent className="pt-2">
              <CsvTable experimentId={experimentId} artifact={table} />
            </CollapsibleContent>
          </Collapsible>
        ))}
      </CardContent>
    </Card>
  );
}

/** Everything the succeeded evaluation produced. */
function Results({
  experimentId,
  evaluationId,
}: {
  experimentId: string;
  evaluationId: string;
}) {
  const artifacts = useEvaluationArtifacts(experimentId, evaluationId, true);
  if (artifacts.isPending) {
    return <Skeleton className="h-48 w-full" aria-label="Loading results" />;
  }
  if (artifacts.isError) {
    return (
      <p role="alert" className="text-sm text-destructive">
        Couldn&apos;t load the results: {artifacts.error.message}
      </p>
    );
  }
  return (
    <>
      <MetricsPanel experimentId={experimentId} artifacts={artifacts.data} />
      <PlotsPanel experimentId={experimentId} artifacts={artifacts.data} />
      <TablesPanel experimentId={experimentId} artifacts={artifacts.data} />
    </>
  );
}

/**
 * One evaluation: its status and progress, kept live over server-sent events (falling back to polling), then once
 * it's done its train/test metrics, plots grouped by kind and tables, or its error.
 * @param experimentId - the experiment
 * @param evaluationId - the evaluation
 * @returns the evaluation's page content
 */
export function EvaluationDetail({
  experimentId,
  evaluationId,
}: {
  experimentId: string;
  evaluationId: string;
}) {
  const events = useEvaluationEvents(experimentId, evaluationId);
  const evaluation = useEvaluation(
    experimentId,
    evaluationId,
    events === "polling",
  );

  if (evaluation.isPending) {
    return (
      <Skeleton className="h-64 w-full" aria-label="Loading the evaluation" />
    );
  }

  if (evaluation.isError) {
    return (
      <p role="alert" className="text-sm text-destructive">
        Couldn&apos;t load the evaluation: {evaluation.error.message}
      </p>
    );
  }

  const data = evaluation.data;
  const progress = data.status === "succeeded" ? 1 : (data.progress ?? 0);

  return (
    <div className="flex flex-col gap-6">
      <Card>
        <CardHeader>
          <CardTitle className="flex flex-wrap items-center gap-3">
            Evaluation of{" "}
            <Link
              href={`${experimentPath(experimentId, "runs")}/${data.runId}`}
              className="hover:underline"
            >
              {data.pipelineName}
            </Link>
            <RunStatusBadge status={data.status} progress={data.progress} />
          </CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-3">
          {isActive(data.status) && (
            <Progress
              value={Math.round(progress * 100)}
              aria-label="Evaluation progress"
            />
          )}
          {data.message && isActive(data.status) && (
            <p className="text-sm">Last finished: {data.message}</p>
          )}
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
        </CardContent>
      </Card>

      {data.status === "failed" && data.error && (
        <Alert variant="destructive">
          <TriangleAlert />
          <AlertTitle>{data.error.type}</AlertTitle>
          <AlertDescription>{data.error.message}</AlertDescription>
        </Alert>
      )}

      {data.status === "succeeded" && (
        <Results experimentId={experimentId} evaluationId={evaluationId} />
      )}
    </div>
  );
}
