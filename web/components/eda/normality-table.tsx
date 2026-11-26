"use client";

import { Check, FlaskConical, Loader2, X } from "lucide-react";

import { formatStat } from "@/components/eda/summary-table";
import { useRunNormalityTests } from "@/components/eda/use-eda";
import { Button } from "@/components/ui/button";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import type { NormalityRow } from "@/lib/eda";

/** The test results, one row per numeric column. */
function ResultsTable({ rows }: { rows: NormalityRow[] }) {
  if (rows.length === 0) {
    return (
      <p className="text-sm text-muted-foreground">
        No numeric columns with at least 3 values to test.
      </p>
    );
  }

  return (
    <div className="overflow-x-auto rounded-lg border">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Column</TableHead>
            <TableHead className="text-right">n</TableHead>
            <TableHead className="text-right">Shapiro-Wilk W</TableHead>
            <TableHead className="text-right">Shapiro-Wilk p</TableHead>
            <TableHead className="text-right">KS statistic</TableHead>
            <TableHead className="text-right">KS p</TableHead>
            <TableHead>Normal (p &gt; 0.05)</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.map((row) => (
            <TableRow key={row.column}>
              <TableCell className="font-medium">{row.column}</TableCell>
              <TableCell className="text-right tabular-nums">{row.n}</TableCell>
              <TableCell className="text-right tabular-nums">
                {formatStat(row.shapiro_statistic)}
              </TableCell>
              <TableCell className="text-right tabular-nums">
                {formatStat(row.shapiro_p)}
              </TableCell>
              <TableCell className="text-right tabular-nums">
                {formatStat(row.ks_statistic)}
              </TableCell>
              <TableCell className="text-right tabular-nums">
                {formatStat(row.ks_p)}
              </TableCell>
              <TableCell>
                {row.normal_at_0_05 ? (
                  <Check className="size-4 text-green-600" aria-label="Yes" />
                ) : (
                  <X className="size-4 text-destructive" aria-label="No" />
                )}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}

/**
 * Runs Shapiro-Wilk and Kolmogorov-Smirnov tests on every numeric column and shows the results. The table is also
 * saved as a CSV artifact; the results shown here are the latest run's.
 * @param experimentId - the experiment
 * @returns the button and, once run, the results
 */
export function NormalityTable({ experimentId }: { experimentId: string }) {
  const run = useRunNormalityTests(experimentId);

  return (
    <div className="flex flex-col gap-4">
      <div>
        <Button
          type="button"
          variant="outline"
          disabled={run.isPending}
          onClick={() => run.mutate()}
        >
          {run.isPending ? (
            <Loader2 className="animate-spin" />
          ) : (
            <FlaskConical />
          )}
          {run.isPending
            ? "Running…"
            : run.data
              ? "Run again"
              : "Run normality tests"}
        </Button>
      </div>
      {run.isError && (
        <p role="alert" className="text-sm text-destructive">
          {run.error.message}
        </p>
      )}
      {run.data && <ResultsTable rows={run.data.rows} />}
    </div>
  );
}
