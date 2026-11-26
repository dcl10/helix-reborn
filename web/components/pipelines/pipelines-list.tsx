"use client";

import { Pencil, Plus, Trash2 } from "lucide-react";
import Link from "next/link";
import { toast } from "sonner";

import {
  useDeletePipeline,
  usePipelineCatalog,
  usePipelines,
} from "@/components/pipelines/use-pipelines";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/components/ui/alert-dialog";
import { Button, buttonVariants } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { experimentPath } from "@/lib/experiments";
import { findStep, type Pipeline } from "@/lib/pipelines";

const updatedFormat = new Intl.DateTimeFormat(undefined, {
  dateStyle: "medium",
  timeStyle: "short",
});

/** Deletes a pipeline after a confirm. */
function DeletePipelineButton({
  experimentId,
  pipeline,
}: {
  experimentId: string;
  pipeline: Pipeline;
}) {
  const remove = useDeletePipeline(experimentId);

  return (
    <AlertDialog>
      <AlertDialogTrigger
        render={
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label={`Delete ${pipeline.name}`}
          />
        }
      >
        <Trash2 />
      </AlertDialogTrigger>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Delete “{pipeline.name}”?</AlertDialogTitle>
          <AlertDialogDescription>
            Its runs are kept, with the settings they used.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>Cancel</AlertDialogCancel>
          <AlertDialogAction
            variant="destructive"
            onClick={() =>
              remove.mutate(pipeline.id, {
                onSuccess: () => toast.success(`Deleted “${pipeline.name}”`),
                onError: (error) => toast.error(error.message),
              })
            }
          >
            Delete
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}

/**
 * The Pipelines tab's list: each saved pipeline's name, model, feature count and last update, with edit and delete,
 * and a "New pipeline" button. A run button comes with the Runs tab (#27).
 * @param experimentId - the experiment
 * @returns the list
 */
export function PipelinesList({ experimentId }: { experimentId: string }) {
  const pipelines = usePipelines(experimentId);
  const catalog = usePipelineCatalog();
  const base = experimentPath(experimentId, "pipelines");

  const newButton = (
    <Link href={`${base}/new`} className={buttonVariants()}>
      <Plus />
      New pipeline
    </Link>
  );

  if (pipelines.isPending) {
    return <Skeleton className="h-32 w-full" aria-label="Loading pipelines" />;
  }

  if (pipelines.isError) {
    return (
      <p role="alert" className="text-sm text-destructive">
        Couldn&apos;t load the pipelines: {pipelines.error.message}
      </p>
    );
  }

  if (pipelines.data.length === 0) {
    return (
      <div className="flex flex-col items-start gap-3">
        <p className="text-muted-foreground">
          No pipelines yet. Build one to train a model.
        </p>
        {newButton}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-4">
      <div>{newButton}</div>
      <div className="overflow-x-auto rounded-lg border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Name</TableHead>
              <TableHead>Model</TableHead>
              <TableHead className="text-right">Features</TableHead>
              <TableHead>Updated</TableHead>
              <TableHead>
                <span className="sr-only">Actions</span>
              </TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {pipelines.data.map((pipeline) => (
              <TableRow key={pipeline.id}>
                <TableCell className="font-medium">
                  <Link
                    href={`${base}/${pipeline.id}`}
                    className="hover:underline"
                  >
                    {pipeline.name}
                  </Link>
                </TableCell>
                <TableCell>
                  {(catalog.data &&
                    findStep(catalog.data, pipeline.spec.model.step)?.label) ??
                    pipeline.spec.model.step}
                </TableCell>
                <TableCell className="text-right tabular-nums">
                  {pipeline.spec.feature_columns.length}
                </TableCell>
                <TableCell>
                  {updatedFormat.format(new Date(pipeline.updatedAt))}
                </TableCell>
                <TableCell>
                  <div className="flex justify-end gap-1">
                    {/* The Run button goes here (#27). */}
                    <Link
                      href={`${base}/${pipeline.id}`}
                      className={buttonVariants({
                        variant: "ghost",
                        size: "icon-sm",
                      })}
                      aria-label={`Edit ${pipeline.name}`}
                    >
                      <Pencil />
                    </Link>
                    <DeletePipelineButton
                      experimentId={experimentId}
                      pipeline={pipeline}
                    />
                  </div>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
    </div>
  );
}
