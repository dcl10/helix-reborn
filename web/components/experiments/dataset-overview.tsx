"use client";

import { DatasetColumnsTable } from "@/components/experiments/dataset-columns-table";
import { DatasetUpload } from "@/components/experiments/dataset-upload";
import { useDataset } from "@/components/experiments/use-experiments";
import { Skeleton } from "@/components/ui/skeleton";

const sizeFormat = new Intl.NumberFormat(undefined, {
  maximumFractionDigits: 1,
});

/**
 * Formats a byte count as a human-readable size.
 * @param bytes - the size in bytes
 * @returns the formatted size, e.g. "1.2 MB"
 */
function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  const kb = bytes / 1024;
  if (kb < 1024) return `${sizeFormat.format(kb)} KB`;
  return `${sizeFormat.format(kb / 1024)} MB`;
}

/**
 * The Data tab: the upload form when the experiment has no dataset yet, or a summary and columns
 * table once it does.
 * @param experimentId - the experiment whose dataset to show
 * @returns the tab content
 */
export function DatasetOverview({ experimentId }: { experimentId: string }) {
  const dataset = useDataset(experimentId);

  if (dataset.isPending) {
    return (
      <div className="flex flex-col gap-2" aria-label="Loading dataset">
        <Skeleton className="h-32 w-full" />
      </div>
    );
  }

  if (dataset.isError) {
    return (
      <p role="alert" className="text-sm text-destructive">
        Couldn&apos;t load the dataset: {dataset.error.message}
      </p>
    );
  }

  if (!dataset.data) {
    return <DatasetUpload experimentId={experimentId} />;
  }

  return (
    <div className="flex flex-col gap-4">
      <dl className="grid grid-cols-2 gap-x-6 gap-y-2 text-sm sm:grid-cols-4">
        <div>
          <dt className="text-muted-foreground">File name</dt>
          <dd className="font-medium">{dataset.data.fileName}</dd>
        </div>
        <div>
          <dt className="text-muted-foreground">Size</dt>
          <dd className="font-medium">{formatSize(dataset.data.sizeBytes)}</dd>
        </div>
        <div>
          <dt className="text-muted-foreground">Rows</dt>
          <dd className="font-medium">{dataset.data.rowCount}</dd>
        </div>
        <div>
          <dt className="text-muted-foreground">Columns</dt>
          <dd className="font-medium">{dataset.data.columns.length}</dd>
        </div>
      </dl>
      <DatasetColumnsTable dataset={dataset.data} />
      <p className="text-sm text-muted-foreground">
        Numeric columns can be used as features.
      </p>
    </div>
  );
}
