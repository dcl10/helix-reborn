"use client";

import { Download } from "lucide-react";

import { useEdaArtifacts } from "@/components/eda/use-eda";
import { buttonVariants } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import { Skeleton } from "@/components/ui/skeleton";
import { type Artifact, artifactContentUrl } from "@/lib/eda";

const createdFormat = new Intl.DateTimeFormat(undefined, {
  dateStyle: "medium",
  timeStyle: "short",
});

/**
 * Orders an experiment's EDA artifacts for the gallery: plots only, newest first.
 * @param artifacts - the EDA artifacts, in any order
 * @returns the plots, newest first
 */
export function galleryPlots(artifacts: Artifact[]): Artifact[] {
  return artifacts
    .filter((artifact) => artifact.kind === "edaPlot")
    .toSorted((a, b) => b.createdAt.localeCompare(a.createdAt));
}

/** One plot: its title, the image (click for full size), when it was made and a download link. */
function PlotCard({
  experimentId,
  plot,
}: {
  experimentId: string;
  plot: Artifact;
}) {
  const src = artifactContentUrl(experimentId, plot.id);

  return (
    <figure className="flex flex-col gap-2 rounded-lg border p-3">
      <Dialog>
        <DialogTrigger
          render={
            <button
              type="button"
              className="overflow-hidden rounded-md bg-white"
              aria-label={`Open ${plot.title} full size`}
            />
          }
        >
          {/* eslint-disable-next-line @next/next/no-img-element -- served by the authenticated API proxy, which next/image's optimiser can't call */}
          <img
            src={src}
            alt={plot.title}
            loading="lazy"
            className="aspect-[5/4] w-full object-contain"
          />
        </DialogTrigger>
        <DialogContent className="sm:max-w-5xl">
          <DialogHeader>
            <DialogTitle>{plot.title}</DialogTitle>
          </DialogHeader>
          {/* eslint-disable-next-line @next/next/no-img-element -- served by the authenticated API proxy, which next/image's optimiser can't call */}
          <img
            src={src}
            alt={plot.title}
            className="max-h-[75vh] w-full rounded-md bg-white object-contain"
          />
        </DialogContent>
      </Dialog>
      <figcaption className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="truncate font-medium" title={plot.title}>
            {plot.title}
          </p>
          <p className="text-xs text-muted-foreground">
            {createdFormat.format(new Date(plot.createdAt))}
          </p>
        </div>
        <a
          href={artifactContentUrl(experimentId, plot.id, true)}
          download={plot.fileName}
          className={buttonVariants({ variant: "ghost", size: "icon-sm" })}
          aria-label={`Download ${plot.title}`}
        >
          <Download />
        </a>
      </figcaption>
    </figure>
  );
}

/**
 * The experiment's EDA plots as a grid, newest first. Each image opens full size in a dialog.
 * @param experimentId - the experiment
 * @returns the gallery
 */
export function PlotGallery({ experimentId }: { experimentId: string }) {
  const artifacts = useEdaArtifacts(experimentId);

  if (artifacts.isPending) {
    return <Skeleton className="h-48 w-full" aria-label="Loading plots" />;
  }

  if (artifacts.isError) {
    return (
      <p role="alert" className="text-sm text-destructive">
        Couldn&apos;t load the plots: {artifacts.error.message}
      </p>
    );
  }

  const plots = galleryPlots(artifacts.data);
  if (plots.length === 0) {
    return (
      <p className="text-sm text-muted-foreground">
        No plots yet. Create one above.
      </p>
    );
  }

  return (
    <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
      {plots.map((plot) => (
        <PlotCard key={plot.id} experimentId={experimentId} plot={plot} />
      ))}
    </div>
  );
}
