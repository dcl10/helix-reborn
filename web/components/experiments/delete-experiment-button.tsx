"use client";

import { Trash2 } from "lucide-react";
import { useRouter } from "next/navigation";
import { toast } from "sonner";

import { useDeleteExperiment } from "@/components/experiments/use-experiments";
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
import { Button } from "@/components/ui/button";
import type { Experiment } from "@/lib/experiments";

/**
 * Deletes an experiment, after confirming in an alert dialog, then navigates back to the experiments
 * list.
 * @param experiment - the experiment to delete
 * @returns the delete button and its confirmation dialog
 */
export function DeleteExperimentButton({
  experiment,
}: {
  experiment: Experiment;
}) {
  const router = useRouter();
  const deleteExperiment = useDeleteExperiment();

  const onDelete = () =>
    deleteExperiment.mutate(experiment.id, {
      onSuccess: () => {
        toast.success(`${experiment.name} deleted`);
        router.push("/experiments");
      },
      onError: (error) => toast.error(error.message),
    });

  return (
    <AlertDialog>
      <AlertDialogTrigger
        render={
          <Button variant="destructive" disabled={deleteExperiment.isPending} />
        }
      >
        <Trash2 />
        Delete
      </AlertDialogTrigger>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Delete this experiment?</AlertDialogTitle>
          <AlertDialogDescription>
            {experiment.name} and everything in it, including its dataset, will
            be deleted. This can&apos;t be undone.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>Cancel</AlertDialogCancel>
          <AlertDialogAction variant="destructive" onClick={onDelete}>
            Delete
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
