"use client";

import { Download } from "lucide-react";

import { buttonVariants } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import { type Artifact, artifactContentUrl } from "@/lib/eda";

const createdFormat = new Intl.DateTimeFormat(undefined, {
  dateStyle: "medium",
  timeStyle: "short",
});

/** One plot: its title, the image (click for full size), optionally when it was made, and a download link. */
function PlotCard({
  experimentId,
  plot,
  showCreated,
}: {
  experimentId: string;
  plot: Artifact;
  showCreated: boolean;
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
          {showCreated && (
            <p className="text-xs text-muted-foreground">
              {createdFormat.format(new Date(plot.createdAt))}
            </p>
          )}
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
 * Plot artifacts as a grid of cards. Each image opens full size in a dialog and has a download link.
 * @param experimentId - the experiment the plots belong to
 * @param plots - the plots, in display order
 * @param showCreated - whether to show when each plot was made
 * @returns the gallery
 */
export function ArtifactGallery({
  experimentId,
  plots,
  showCreated = false,
}: {
  experimentId: string;
  plots: Artifact[];
  showCreated?: boolean;
}) {
  return (
    <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
      {plots.map((plot) => (
        <PlotCard
          key={plot.id}
          experimentId={experimentId}
          plot={plot}
          showCreated={showCreated}
        />
      ))}
    </div>
  );
}
