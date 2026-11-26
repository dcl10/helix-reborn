"use client";

import Link from "next/link";

import { NormalityTable } from "@/components/eda/normality-table";
import { PlotBuilder } from "@/components/eda/plot-builder";
import { PlotGallery } from "@/components/eda/plot-gallery";
import { SummaryTable } from "@/components/eda/summary-table";
import { useEdaSummary } from "@/components/eda/use-eda";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { experimentPath } from "@/lib/experiments";

/**
 * The EDA tab: a summary of the dataset, a plot builder, normality tests and a gallery of the experiment's plots.
 * When the experiment has no dataset yet, a link to the Data tab instead.
 * @param experimentId - the experiment
 * @returns the tab content
 */
export function EdaTab({ experimentId }: { experimentId: string }) {
  const summary = useEdaSummary(experimentId);

  if (summary.isPending) {
    return (
      <div className="flex flex-col gap-4" aria-label="Loading EDA">
        <Skeleton className="h-48 w-full" />
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }

  if (summary.isError) {
    return (
      <p role="alert" className="text-sm text-destructive">
        Couldn&apos;t load the dataset summary: {summary.error.message}
      </p>
    );
  }

  if (!summary.data) {
    return (
      <p className="text-muted-foreground">
        This experiment has no dataset yet.{" "}
        <Link
          href={experimentPath(experimentId, "data")}
          className="font-medium text-foreground underline underline-offset-4"
        >
          Upload one on the Data tab
        </Link>{" "}
        to explore it here.
      </p>
    );
  }

  return (
    <div className="flex flex-col gap-6">
      <Card>
        <CardHeader>
          <CardTitle>Summary</CardTitle>
          <CardDescription>
            {summary.data.row_count} rows, {summary.data.column_count} columns.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <SummaryTable summary={summary.data} />
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Create a plot</CardTitle>
        </CardHeader>
        <CardContent>
          <PlotBuilder
            experimentId={experimentId}
            columns={summary.data.columns}
          />
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Normality tests</CardTitle>
          <CardDescription>
            Shapiro-Wilk and Kolmogorov-Smirnov tests on every numeric column.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <NormalityTable experimentId={experimentId} />
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Plots</CardTitle>
        </CardHeader>
        <CardContent>
          <PlotGallery experimentId={experimentId} />
        </CardContent>
      </Card>
    </div>
  );
}
