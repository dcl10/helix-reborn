"use client";

import { Loader2, Play } from "lucide-react";
import { useRouter } from "next/navigation";
import { toast } from "sonner";

import { useStartRun } from "@/components/runs/use-runs";
import { Button } from "@/components/ui/button";
import { experimentPath } from "@/lib/experiments";

/**
 * Starts a run of a pipeline, then opens the run. The API's error (the spec no longer fits the dataset, or the
 * training queue is down) shows as a toast.
 * @param experimentId - the experiment
 * @param pipelineId - the pipeline to run
 * @param pipelineName - its name, for the button's label
 * @param iconOnly - show just the icon (the list's row actions)
 * @returns the button
 */
export function RunPipelineButton({
  experimentId,
  pipelineId,
  pipelineName,
  iconOnly = false,
}: {
  experimentId: string;
  pipelineId: string;
  pipelineName: string;
  iconOnly?: boolean;
}) {
  const router = useRouter();
  const start = useStartRun(experimentId);

  const run = () =>
    start.mutate(pipelineId, {
      onSuccess: (created) =>
        router.push(`${experimentPath(experimentId, "runs")}/${created.id}`),
      onError: (error) =>
        toast.error(`Couldn't start “${pipelineName}”: ${error.message}`),
    });

  const icon = start.isPending ? (
    <Loader2 className="animate-spin" />
  ) : (
    <Play />
  );
  return iconOnly ? (
    <Button
      type="button"
      variant="ghost"
      size="icon-sm"
      aria-label={`Run ${pipelineName}`}
      disabled={start.isPending}
      onClick={run}
    >
      {icon}
    </Button>
  ) : (
    <Button type="button" disabled={start.isPending} onClick={run}>
      {icon}
      {start.isPending ? "Starting…" : "Run"}
    </Button>
  );
}
