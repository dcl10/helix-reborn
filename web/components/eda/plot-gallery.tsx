"use client";

import { ArtifactGallery } from "@/components/artifacts/artifact-gallery";
import { useEdaArtifacts } from "@/components/eda/use-eda";
import { Skeleton } from "@/components/ui/skeleton";
import type { Artifact } from "@/lib/eda";

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
    <ArtifactGallery experimentId={experimentId} plots={plots} showCreated />
  );
}
