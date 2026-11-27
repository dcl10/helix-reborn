"use client";

import { useArtifactContent } from "@/components/evaluations/use-evaluations";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import type { Artifact } from "@/lib/eda";
import { parseCsv } from "@/lib/evaluations";

/** How many rows of a table artifact to show. */
export const CSV_PREVIEW_ROWS = 50;

/**
 * A CSV table artifact, fetched through the proxy and shown as a table of its first 50 rows.
 * @param experimentId - the experiment
 * @param artifact - the table artifact
 * @returns the table
 */
export function CsvTable({
  experimentId,
  artifact,
}: {
  experimentId: string;
  artifact: Artifact;
}) {
  const content = useArtifactContent<string>(experimentId, artifact, "text");

  if (content.isPending) {
    return (
      <Skeleton
        className="h-24 w-full"
        aria-label={`Loading ${artifact.title}`}
      />
    );
  }

  if (content.isError) {
    return (
      <p role="alert" className="text-sm text-destructive">
        Couldn&apos;t load {artifact.title}: {content.error.message}
      </p>
    );
  }

  const table = parseCsv(content.data, CSV_PREVIEW_ROWS);
  return (
    <div className="flex flex-col gap-2">
      <div className="max-h-96 overflow-auto rounded-lg border">
        <Table>
          <TableHeader>
            <TableRow>
              {table.header.map((column, i) => (
                <TableHead key={`${column}-${i}`}>{column}</TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {table.rows.map((row, r) => (
              <TableRow key={r}>
                {row.map((cell, c) => (
                  <TableCell key={c} className="tabular-nums">
                    {cell}
                  </TableCell>
                ))}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
      {table.truncated && (
        <p className="text-xs text-muted-foreground">
          Showing the first {CSV_PREVIEW_ROWS} rows. Download the file for the
          rest.
        </p>
      )}
    </div>
  );
}
