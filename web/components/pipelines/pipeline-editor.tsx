"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { Loader2, Save, TriangleAlert } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { Controller, useForm, useWatch } from "react-hook-form";
import { toast } from "sonner";

import { useDataset } from "@/components/experiments/use-experiments";
import { SearchSettings } from "@/components/pipelines/search-settings";
import { StepList } from "@/components/pipelines/step-list";
import { StepParamsForm } from "@/components/pipelines/step-params-form";
import {
  usePipelineCatalog,
  useSavePipeline,
} from "@/components/pipelines/use-pipelines";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button, buttonVariants } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import {
  Field,
  FieldDescription,
  FieldError,
  FieldLabel,
  FieldLegend,
  FieldSet,
} from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import { ScrollArea } from "@/components/ui/scroll-area";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiError } from "@/lib/api";
import { type DatasetColumn, experimentPath } from "@/lib/experiments";
import { paramsToForm } from "@/lib/json-schema-form";
import {
  emptyForm,
  findStep,
  formFromSpec,
  type Pipeline,
  type PipelineCatalog,
  pipelineFormSchema,
  type PipelineFormValues,
  type ProblemType,
  redundantScalerSteps,
  specFromForm,
  stepsFor,
} from "@/lib/pipelines";

const cvFoldOptions = [
  { value: "", label: "Off" },
  ...Array.from({ length: 9 }, (_, i) => ({
    value: String(i + 2),
    label: `${i + 2} folds`,
  })),
];

/** The pipelines list's path for an experiment. */
function pipelinesPath(experimentId: string): string {
  return experimentPath(experimentId, "pipelines");
}

/** The editor form, once the catalog and dataset are loaded. */
function EditorForm({
  experimentId,
  pipeline,
  catalog,
  columns,
}: {
  experimentId: string;
  pipeline?: Pipeline;
  catalog: PipelineCatalog;
  columns: DatasetColumn[];
}) {
  const router = useRouter();
  const save = useSavePipeline(experimentId, pipeline?.id);
  const [apiErrors, setApiErrors] = useState<string[]>([]);

  const schema = useMemo(
    () => pipelineFormSchema(catalog, columns),
    [catalog, columns],
  );
  const form = useForm<PipelineFormValues>({
    resolver: zodResolver(schema),
    defaultValues: pipeline
      ? formFromSpec(pipeline.name, pipeline.spec, catalog)
      : emptyForm(columns),
  });
  const { control, setValue, getValues } = form;

  const [problemType, targetColumn, idColumn, steps, modelStep] = useWatch({
    control,
    name: ["problemType", "targetColumn", "idColumn", "steps", "model.step"],
  });
  const model = findStep(catalog, modelStep);
  const models = stepsFor(catalog, ["model"], problemType);

  const redundant = redundantScalerSteps(
    steps.map((s) => s.step),
    modelStep,
  ).map((id) => findStep(catalog, id)?.label ?? id);

  const changeProblemType = (next: ProblemType) => {
    const removed: string[] = [];
    const keptSteps = getValues("steps").filter((step) => {
      const ok =
        findStep(catalog, step.step)?.problem_types.includes(next) ?? false;
      if (!ok) removed.push(findStep(catalog, step.step)?.label ?? step.step);
      return ok;
    });
    if (keptSteps.length !== getValues("steps").length)
      setValue("steps", keptSteps);
    if (model && !model.problem_types.includes(next)) {
      removed.push(model.label);
      setValue("model", { step: "", params: {} });
      setValue("customGrid", false);
      setValue("paramGrid", {});
    }
    setValue("problemType", next, {
      shouldValidate: form.formState.isSubmitted,
    });
    if (removed.length > 0) {
      toast.info(
        `Removed what doesn't support ${next}: ${removed.join(", ")}.`,
      );
    }
  };

  const chooseModel = (id: string) => {
    const definition = findStep(catalog, id);
    setValue(
      "model",
      {
        step: id,
        params: definition ? paramsToForm(definition.params_schema) : {},
      },
      { shouldValidate: form.formState.isSubmitted },
    );
    setValue("customGrid", false);
    setValue("paramGrid", {});
  };

  /** A column chosen as the target or ID stops being a feature. */
  const chooseColumn = (name: "targetColumn" | "idColumn", value: string) => {
    setValue(name, value, { shouldValidate: form.formState.isSubmitted });
    if (value) {
      setValue(
        "featureColumns",
        getValues("featureColumns").filter((c) => c !== value),
        { shouldValidate: form.formState.isSubmitted },
      );
    }
  };

  const onSubmit = async (values: PipelineFormValues) => {
    setApiErrors([]);
    try {
      const saved = await save.mutateAsync({
        name: values.name.trim(),
        spec: specFromForm(values, catalog, columns),
      });
      toast.success(`Saved “${saved.name}”`);
      router.push(pipelinesPath(experimentId));
    } catch (error) {
      if (error instanceof ApiError && error.errors?.spec?.length) {
        setApiErrors(error.errors.spec);
      } else {
        setApiErrors([(error as Error).message]);
      }
      window.scrollTo({ top: 0, behavior: "smooth" });
    }
  };

  const columnItems = columns.map((c) => ({ value: c.name, label: c.name }));
  const featureCandidates = columns.filter(
    (c) => c.name !== targetColumn && c.name !== idColumn,
  );

  return (
    <form
      noValidate
      onSubmit={form.handleSubmit(onSubmit)}
      className="flex flex-col gap-6"
      aria-label={pipeline ? `Edit ${pipeline.name}` : "New pipeline"}
    >
      {apiErrors.length > 0 && (
        <Alert variant="destructive">
          <TriangleAlert />
          <AlertTitle>The pipeline couldn&apos;t be saved</AlertTitle>
          <AlertDescription>
            <ul className="list-disc pl-4">
              {apiErrors.map((error) => (
                <li key={error}>{error}</li>
              ))}
            </ul>
          </AlertDescription>
        </Alert>
      )}

      <Card>
        <CardHeader>
          <CardTitle>Name</CardTitle>
        </CardHeader>
        <CardContent>
          <Controller
            control={control}
            name="name"
            render={({ field, fieldState }) => (
              <Field data-invalid={fieldState.invalid} className="max-w-md">
                <FieldLabel htmlFor="pipeline-name">Pipeline name</FieldLabel>
                <Input
                  id="pipeline-name"
                  {...field}
                  aria-invalid={fieldState.invalid}
                />
                {fieldState.invalid && (
                  <FieldError errors={[fieldState.error]} />
                )}
              </Field>
            )}
          />
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Problem and data</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-5">
          <Controller
            control={control}
            name="problemType"
            render={({ field }) => (
              <FieldSet>
                <FieldLegend variant="label">Problem type</FieldLegend>
                <RadioGroup
                  value={field.value}
                  onValueChange={(value) =>
                    changeProblemType(value as ProblemType)
                  }
                  className="flex gap-6"
                >
                  <Field orientation="horizontal">
                    <RadioGroupItem
                      id="problem-classification"
                      value="classification"
                    />
                    <FieldLabel htmlFor="problem-classification">
                      Classification
                    </FieldLabel>
                  </Field>
                  <Field orientation="horizontal">
                    <RadioGroupItem
                      id="problem-regression"
                      value="regression"
                    />
                    <FieldLabel htmlFor="problem-regression">
                      Regression
                    </FieldLabel>
                  </Field>
                </RadioGroup>
              </FieldSet>
            )}
          />

          <div className="grid gap-4 sm:grid-cols-2">
            <Controller
              control={control}
              name="targetColumn"
              render={({ field, fieldState }) => (
                <Field data-invalid={fieldState.invalid}>
                  <FieldLabel htmlFor="target-column">Target column</FieldLabel>
                  <Select
                    items={columnItems}
                    value={field.value || null}
                    onValueChange={(value) =>
                      chooseColumn(
                        "targetColumn",
                        (value as string | null) ?? "",
                      )
                    }
                  >
                    <SelectTrigger
                      id="target-column"
                      className="w-full"
                      aria-invalid={fieldState.invalid}
                    >
                      <SelectValue placeholder="Choose the column to predict" />
                    </SelectTrigger>
                    <SelectContent>
                      {columnItems.map((item) => (
                        <SelectItem key={item.value} value={item.value}>
                          {item.label}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                  {fieldState.invalid && (
                    <FieldError errors={[fieldState.error]} />
                  )}
                </Field>
              )}
            />
            <Controller
              control={control}
              name="idColumn"
              render={({ field, fieldState }) => (
                <Field data-invalid={fieldState.invalid}>
                  <FieldLabel htmlFor="id-column">ID column</FieldLabel>
                  <Select
                    items={[{ value: null, label: "None" }, ...columnItems]}
                    value={field.value || null}
                    onValueChange={(value) =>
                      chooseColumn("idColumn", (value as string | null) ?? "")
                    }
                  >
                    <SelectTrigger
                      id="id-column"
                      className="w-full"
                      aria-invalid={fieldState.invalid}
                    >
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value={null}>None</SelectItem>
                      {columnItems.map((item) => (
                        <SelectItem key={item.value} value={item.value}>
                          {item.label}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                  <FieldDescription>
                    Optional. Kept out of the features, and shown with
                    predictions.
                  </FieldDescription>
                  {fieldState.invalid && (
                    <FieldError errors={[fieldState.error]} />
                  )}
                </Field>
              )}
            />
          </div>

          <Controller
            control={control}
            name="featureColumns"
            render={({ field, fieldState }) => {
              const chosen = new Set(field.value);
              return (
                <FieldSet data-invalid={fieldState.invalid}>
                  <FieldLegend variant="label">Feature columns</FieldLegend>
                  <FieldDescription>
                    The numeric columns the model learns from.
                  </FieldDescription>
                  <ScrollArea className="rounded-lg border *:data-[slot=scroll-area-viewport]:max-h-56">
                    <div className="flex flex-col gap-2 p-3">
                      {featureCandidates.map((column) => {
                        const numeric = column.inferredType === "numeric";
                        const id = `feature-${column.name}`;
                        return (
                          <Field
                            key={column.name}
                            orientation="horizontal"
                            data-disabled={!numeric}
                          >
                            <Checkbox
                              id={id}
                              disabled={!numeric}
                              checked={chosen.has(column.name)}
                              onCheckedChange={(checked) =>
                                field.onChange(
                                  checked
                                    ? [...field.value, column.name]
                                    : field.value.filter(
                                        (name) => name !== column.name,
                                      ),
                                )
                              }
                            />
                            <FieldLabel htmlFor={id} className="font-normal">
                              {column.name}
                              {!numeric && (
                                <span className="text-muted-foreground">
                                  {" "}
                                  (not numeric)
                                </span>
                              )}
                            </FieldLabel>
                          </Field>
                        );
                      })}
                    </div>
                  </ScrollArea>
                  {fieldState.invalid && (
                    <FieldError errors={[fieldState.error]} />
                  )}
                </FieldSet>
              );
            }}
          />
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Split</CardTitle>
          <CardDescription>
            How rows are held out to test the trained model.
          </CardDescription>
        </CardHeader>
        <CardContent className="grid gap-4 sm:grid-cols-3">
          <Controller
            control={control}
            name="testSize"
            render={({ field, fieldState }) => (
              <Field data-invalid={fieldState.invalid}>
                <FieldLabel htmlFor="test-size">Test size</FieldLabel>
                <Input
                  id="test-size"
                  inputMode="decimal"
                  {...field}
                  aria-invalid={fieldState.invalid}
                />
                <FieldDescription>
                  Share of rows held out, 0.05 to 0.5.
                </FieldDescription>
                {fieldState.invalid && (
                  <FieldError errors={[fieldState.error]} />
                )}
              </Field>
            )}
          />
          <Controller
            control={control}
            name="randomSeed"
            render={({ field, fieldState }) => (
              <Field data-invalid={fieldState.invalid}>
                <FieldLabel htmlFor="random-seed">Random seed</FieldLabel>
                <Input
                  id="random-seed"
                  inputMode="numeric"
                  {...field}
                  aria-invalid={fieldState.invalid}
                />
                {fieldState.invalid && (
                  <FieldError errors={[fieldState.error]} />
                )}
              </Field>
            )}
          />
          <Controller
            control={control}
            name="cvFolds"
            render={({ field, fieldState }) => (
              <Field data-invalid={fieldState.invalid}>
                <FieldLabel htmlFor="cv-folds">Cross-validation</FieldLabel>
                <Select
                  items={cvFoldOptions.map((o) => ({
                    value: o.value || null,
                    label: o.label,
                  }))}
                  value={field.value || null}
                  onValueChange={(value) =>
                    field.onChange((value as string | null) ?? "")
                  }
                >
                  <SelectTrigger id="cv-folds" className="w-full">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {cvFoldOptions.map((option) => (
                      <SelectItem
                        key={option.label}
                        value={option.value || null}
                      >
                        {option.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <FieldDescription>On the training rows.</FieldDescription>
                {fieldState.invalid && (
                  <FieldError errors={[fieldState.error]} />
                )}
              </Field>
            )}
          />
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Preprocessing and feature selection</CardTitle>
          <CardDescription>Applied in order before the model.</CardDescription>
        </CardHeader>
        <CardContent>
          <StepList
            control={control}
            catalog={catalog}
            problemType={problemType}
          />
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Model</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <Controller
            control={control}
            name="model.step"
            render={({ field, fieldState }) => (
              <Field data-invalid={fieldState.invalid} className="max-w-md">
                <FieldLabel htmlFor="model">Model</FieldLabel>
                <Select
                  items={models.map((m) => ({ value: m.id, label: m.label }))}
                  value={field.value || null}
                  onValueChange={(value) =>
                    chooseModel((value as string | null) ?? "")
                  }
                >
                  <SelectTrigger
                    id="model"
                    className="w-full"
                    aria-invalid={fieldState.invalid}
                  >
                    <SelectValue placeholder="Choose a model" />
                  </SelectTrigger>
                  <SelectContent>
                    {models.map((m) => (
                      <SelectItem key={m.id} value={m.id}>
                        {m.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                {model && (
                  <FieldDescription>{model.description}</FieldDescription>
                )}
                {fieldState.invalid && (
                  <FieldError errors={[fieldState.error]} />
                )}
              </Field>
            )}
          />
          {model && (
            <StepParamsForm
              control={control}
              name="model.params"
              schema={model.params_schema}
            />
          )}
          {redundant.length > 0 && (
            <Alert>
              <TriangleAlert />
              <AlertTitle>Scaling has no effect on this model</AlertTitle>
              <AlertDescription>
                Tree models don&apos;t need scaling, so{" "}
                {redundant.join(" and ")}{" "}
                {redundant.length === 1 ? "has" : "have"} no effect on this
                model, and it can make the exported model fail its accuracy
                check after training. Consider removing{" "}
                {redundant.length === 1 ? "it" : "them"}.
              </AlertDescription>
            </Alert>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Hyperparameter search</CardTitle>
          <CardDescription>
            Optional: try several model settings on the training rows and keep
            the best.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <SearchSettings
            control={control}
            setValue={setValue}
            catalog={catalog}
          />
        </CardContent>
      </Card>

      <div className="flex items-center gap-3">
        <Button type="submit" disabled={save.isPending}>
          {save.isPending ? <Loader2 className="animate-spin" /> : <Save />}
          {save.isPending ? "Saving…" : "Save pipeline"}
        </Button>
        <Link
          href={pipelinesPath(experimentId)}
          className={buttonVariants({ variant: "ghost" })}
        >
          Cancel
        </Link>
      </div>
    </form>
  );
}

/**
 * The pipeline editor: name, problem type and columns, split, ordered preprocessing and feature-selection steps,
 * model, and optional hyperparameter search, every step's settings generated from the catalog. Saves with POST, or PUT
 * when editing, then returns to the pipelines list; the API's spec errors show at the top.
 * @param experimentId - the experiment
 * @param pipeline - the pipeline to edit, or undefined for a new one
 * @returns the editor, or a loading state, an error, or a link to upload a dataset first
 */
export function PipelineEditor({
  experimentId,
  pipeline,
}: {
  experimentId: string;
  pipeline?: Pipeline;
}) {
  const catalog = usePipelineCatalog();
  const dataset = useDataset(experimentId);

  if (catalog.isPending || dataset.isPending) {
    return (
      <Skeleton
        className="h-96 w-full"
        aria-label="Loading the pipeline editor"
      />
    );
  }

  const error = catalog.error ?? dataset.error;
  if (error) {
    return (
      <p role="alert" className="text-sm text-destructive">
        Couldn&apos;t load the editor: {error.message}
      </p>
    );
  }

  if (!dataset.data) {
    return (
      <p className="text-muted-foreground">
        Upload a dataset on the{" "}
        <Link
          href={experimentPath(experimentId, "data")}
          className="font-medium text-foreground underline underline-offset-4"
        >
          Data tab
        </Link>{" "}
        before building a pipeline.
      </p>
    );
  }

  return (
    <EditorForm
      experimentId={experimentId}
      pipeline={pipeline}
      catalog={catalog.data!}
      columns={dataset.data.columns}
    />
  );
}
