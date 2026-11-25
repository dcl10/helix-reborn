"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { Plus } from "lucide-react";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { Controller, useForm } from "react-hook-form";
import { toast } from "sonner";

import { useCreateExperiment } from "@/components/experiments/use-experiments";
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
import type { ExperimentValues } from "@/lib/experiments";
import { experimentPath, experimentSchema } from "@/lib/experiments";

/**
 * A "New experiment" button that opens a dialog to create one. On success it navigates to the new
 * experiment's Data tab.
 * @returns the button and its dialog
 */
export function NewExperimentDialog() {
  const [open, setOpen] = useState(false);
  const router = useRouter();
  const create = useCreateExperiment();
  const form = useForm<ExperimentValues>({
    resolver: zodResolver(experimentSchema),
    defaultValues: { name: "", description: "" },
  });

  const onSubmit = (values: ExperimentValues) =>
    create.mutateAsync(values).then(
      (experiment) => {
        setOpen(false);
        form.reset();
        router.push(experimentPath(experiment.id, "data"));
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
        if (!next) form.reset();
      }}
    >
      <DialogTrigger render={<Button />}>
        <Plus />
        New experiment
      </DialogTrigger>
      <DialogContent>
        <form
          noValidate
          onSubmit={form.handleSubmit(onSubmit)}
          className="flex flex-col gap-4"
        >
          <DialogHeader>
            <DialogTitle>New experiment</DialogTitle>
            <DialogDescription>
              Give it a name, then upload a dataset to get started.
            </DialogDescription>
          </DialogHeader>
          <Controller
            name="name"
            control={form.control}
            render={({ field, fieldState }) => (
              <Field data-invalid={fieldState.invalid}>
                <FieldLabel htmlFor="experiment-name">Name</FieldLabel>
                <Input
                  {...field}
                  id="experiment-name"
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
                <FieldLabel htmlFor="experiment-description">
                  Description
                </FieldLabel>
                <Textarea
                  {...field}
                  id="experiment-description"
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
              {form.formState.isSubmitting ? "Creating…" : "Create"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
