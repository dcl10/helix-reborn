"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { Pencil } from "lucide-react";
import { useState } from "react";
import { Controller, useForm } from "react-hook-form";
import { toast } from "sonner";

import { useUpdateExperiment } from "@/components/experiments/use-experiments";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import {
  Field,
  FieldDescription,
  FieldError,
  FieldLabel,
} from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import type { Experiment, ExperimentValues } from "@/lib/experiments";
import { experimentSchema } from "@/lib/experiments";

/**
 * Edits an experiment's name and description.
 * @param experiment - the experiment to edit
 * @returns the edit button and its dialog
 */
export function EditExperimentDialog({
  experiment,
}: {
  experiment: Experiment;
}) {
  const [open, setOpen] = useState(false);
  const update = useUpdateExperiment();
  const form = useForm<ExperimentValues>({
    resolver: zodResolver(experimentSchema),
    defaultValues: {
      name: experiment.name,
      description: experiment.description ?? "",
    },
  });

  const onSubmit = (values: ExperimentValues) =>
    update.mutateAsync({ id: experiment.id, ...values }).then(
      () => {
        setOpen(false);
        toast.success("Experiment updated");
      },
      (error: Error) => {
        toast.error(error.message);
      },
    );

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        if (next) {
          form.reset({
            name: experiment.name,
            description: experiment.description ?? "",
          });
        }
      }}
    >
      <DialogTrigger render={<Button variant="outline" />}>
        <Pencil />
        Edit
      </DialogTrigger>
      <DialogContent>
        <form
          noValidate
          onSubmit={form.handleSubmit(onSubmit)}
          className="flex flex-col gap-4"
        >
          <DialogHeader>
            <DialogTitle>Edit experiment</DialogTitle>
            <DialogDescription>
              Change the name or description.
            </DialogDescription>
          </DialogHeader>
          <Controller
            name="name"
            control={form.control}
            render={({ field, fieldState }) => (
              <Field data-invalid={fieldState.invalid}>
                <FieldLabel htmlFor="edit-experiment-name">Name</FieldLabel>
                <Input
                  {...field}
                  id="edit-experiment-name"
                  autoComplete="off"
                  aria-invalid={fieldState.invalid}
                />
                <FieldError errors={[fieldState.error]} />
              </Field>
            )}
          />
          <Controller
            name="description"
            control={form.control}
            render={({ field, fieldState }) => (
              <Field data-invalid={fieldState.invalid}>
                <FieldLabel htmlFor="edit-experiment-description">
                  Description
                </FieldLabel>
                <Textarea
                  {...field}
                  id="edit-experiment-description"
                  aria-invalid={fieldState.invalid}
                />
                {fieldState.invalid ? (
                  <FieldError errors={[fieldState.error]} />
                ) : (
                  <FieldDescription>Optional.</FieldDescription>
                )}
              </Field>
            )}
          />
          <DialogFooter>
            <Button type="submit" disabled={form.formState.isSubmitting}>
              {form.formState.isSubmitting ? "Saving…" : "Save"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
