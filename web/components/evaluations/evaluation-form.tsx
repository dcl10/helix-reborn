"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { Clock, FlaskConical } from "lucide-react";
import { useRouter } from "next/navigation";
import { Controller, useForm, useWatch } from "react-hook-form";

import {
  CheckboxField,
  NumberField,
  SelectField,
} from "@/components/eda/form-fields";
import { PlotOptionsForm } from "@/components/eda/plot-options-form";
import { useStartEvaluation } from "@/components/evaluations/use-evaluations";
import { useRuns } from "@/components/runs/use-runs";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Field,
  FieldDescription,
  FieldError,
  FieldLabel,
  FieldLegend,
  FieldSet,
} from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { Switch } from "@/components/ui/switch";
import { ApiError } from "@/lib/api";
import {
  defaultEvaluationValues,
  evaluationFormSchema,
  type EvaluationFormValues,
  toEvaluationOptions,
} from "@/lib/evaluations";
import { experimentPath } from "@/lib/experiments";

const createdFormat = new Intl.DateTimeFormat(undefined, {
  dateStyle: "medium",
  timeStyle: "short",
});

/** A rule's message, shown under the field it's about. */
function RuleError({ message }: { message?: string }) {
  return message ? <FieldError>{message}</FieldError> : null;
}

/**
 * The new evaluation form: which succeeded run to evaluate, which performance plots and feature importance methods to
 * run with their settings, fuzzy interpretation, and plot options. The dependency rules (fuzzy needs a local and an
 * ensemble method, ensembles need a global method, one cluster name per cluster) show as inline errors and keep
 * submit disabled. On success it opens the new evaluation's page.
 * @param experimentId - the experiment
 * @param initialRunId - the run to preselect (from `?runId=`), if any
 * @returns the form
 */
export function EvaluationForm({
  experimentId,
  initialRunId,
}: {
  experimentId: string;
  initialRunId?: string;
}) {
  const router = useRouter();
  const runs = useRuns(experimentId);
  const start = useStartEvaluation(experimentId);
  const form = useForm<EvaluationFormValues>({
    resolver: zodResolver(evaluationFormSchema),
    defaultValues: defaultEvaluationValues(initialRunId ?? ""),
    mode: "onChange",
  });
  const { control } = form;

  // The cross-field rules depend on several fields at once, so they're checked on every change of any of them.
  const values = useWatch({ control }) as EvaluationFormValues;
  const parsed = evaluationFormSchema.safeParse(values);
  const ruleError = (path: string) =>
    parsed.success
      ? undefined
      : parsed.error.issues.find((issue) => issue.path.join(".") === path)
          ?.message;

  if (runs.isPending) {
    return <Skeleton className="h-96 w-full" aria-label="Loading runs" />;
  }

  if (runs.isError) {
    return (
      <p role="alert" className="text-sm text-destructive">
        Couldn&apos;t load the runs: {runs.error.message}
      </p>
    );
  }

  const succeeded = runs.data.filter((run) => run.status === "succeeded");
  if (succeeded.length === 0) {
    return (
      <p className="text-muted-foreground">
        No trained models yet. Evaluate a run once it has succeeded.
      </p>
    );
  }

  const runOptions = succeeded.map((run) => ({
    value: run.id,
    label: `${run.pipelineName} · ${createdFormat.format(new Date(run.createdAt))}`,
  }));

  const onSubmit = (submitted: EvaluationFormValues) =>
    start.mutate(
      { runId: submitted.runId, options: toEvaluationOptions(submitted) },
      {
        onSuccess: (evaluation) =>
          router.push(
            `${experimentPath(experimentId, "evaluations")}/${evaluation.id}`,
          ),
      },
    );

  const apiErrors =
    start.error instanceof ApiError ? start.error.errors?.options : undefined;

  return (
    <form
      onSubmit={form.handleSubmit(onSubmit)}
      className="flex flex-col gap-6"
      noValidate
    >
      <Card>
        <CardHeader>
          <CardTitle>Model</CardTitle>
        </CardHeader>
        <CardContent>
          <SelectField
            control={control}
            name="runId"
            label="Run"
            description="Only succeeded runs have a model to evaluate."
            options={runOptions}
          />
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>What to compute</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-6">
          <FieldSet>
            <FieldLegend>Performance plots</FieldLegend>
            <CheckboxField
              control={control}
              name="plots"
              label="Confusion matrix, ROC and precision-recall curves, or predicted vs actual and residuals"
            />
          </FieldSet>

          <FieldSet>
            <FieldLegend>Global importance</FieldLegend>
            <FieldDescription>
              How much each feature matters to the model overall.
            </FieldDescription>
            <div className="flex flex-wrap gap-6">
              <CheckboxField
                control={control}
                name="globalPermutation"
                label="Permutation"
              />
              <CheckboxField control={control} name="globalShap" label="SHAP" />
            </div>
            <div className="max-w-56">
              <NumberField
                control={control}
                name="permutationRepeats"
                label="Permutation repeats"
                min={1}
                max={100}
              />
            </div>
          </FieldSet>

          <FieldSet>
            <FieldLegend>Local importance</FieldLegend>
            <FieldDescription>
              How much each feature mattered to individual test predictions.
            </FieldDescription>
            <div className="flex flex-wrap gap-6">
              <CheckboxField control={control} name="localLime" label="LIME" />
              <CheckboxField control={control} name="localShap" label="SHAP" />
            </div>
            <div className="max-w-56">
              <NumberField
                control={control}
                name="localMaxSamples"
                label="Max samples to explain"
                min={1}
                max={1000}
              />
            </div>
          </FieldSet>

          <FieldSet>
            <FieldLegend>Ensemble importance</FieldLegend>
            <FieldDescription>
              Combines the global methods into one importance per feature.
            </FieldDescription>
            <div className="flex flex-wrap gap-6">
              <CheckboxField
                control={control}
                name="ensembleMean"
                label="Mean"
              />
              <CheckboxField
                control={control}
                name="ensembleMajorityVote"
                label="Majority vote"
              />
            </div>
            <RuleError message={ruleError("ensembleMean")} />
          </FieldSet>

          <FieldSet>
            <FieldLegend>Fuzzy interpretation</FieldLegend>
            <FieldDescription>
              Turns local importances into rules such as &ldquo;when the target
              is high, feature A matters most&rdquo;. Needs a local and an
              ensemble method.
            </FieldDescription>
            <Controller
              control={control}
              name="fuzzyEnabled"
              render={({ field }) => (
                <Field orientation="horizontal">
                  <Switch
                    id="fuzzyEnabled"
                    checked={field.value}
                    onCheckedChange={(checked) => field.onChange(checked)}
                  />
                  <FieldLabel htmlFor="fuzzyEnabled">
                    Run fuzzy interpretation
                  </FieldLabel>
                </Field>
              )}
            />
            <RuleError message={ruleError("fuzzyEnabled")} />
            {values.fuzzyEnabled && (
              <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
                <NumberField
                  control={control}
                  name="fuzzy.numberFuzzyFeatures"
                  label="Features in the rules"
                  min={1}
                />
                <NumberField
                  control={control}
                  name="fuzzy.numberClusters"
                  label="Target clusters"
                  min={2}
                  max={10}
                />
                <NumberField
                  control={control}
                  name="fuzzy.numberRules"
                  label="Rules per context"
                  min={1}
                />
                <Controller
                  control={control}
                  name="fuzzy.clusterNames"
                  render={({ field }) => {
                    const message = ruleError("fuzzy.clusterNames");
                    return (
                      <Field
                        data-invalid={message !== undefined}
                        className="sm:col-span-2"
                      >
                        <FieldLabel htmlFor="fuzzy.clusterNames">
                          Cluster names
                        </FieldLabel>
                        <Input
                          {...field}
                          id="fuzzy.clusterNames"
                          aria-invalid={message !== undefined}
                        />
                        <FieldDescription>
                          Comma-separated, lowest first, one per cluster.
                        </FieldDescription>
                        <RuleError message={message} />
                      </Field>
                    );
                  }}
                />
                <CheckboxField
                  control={control}
                  name="fuzzy.granularFeatures"
                  label="Plot membership functions per feature"
                />
              </div>
            )}
          </FieldSet>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Plots</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <div className="max-w-56">
            <NumberField
              control={control}
              name="numFeaturesToPlot"
              label="Features to plot"
              min={1}
              max={50}
            />
          </div>
          <PlotOptionsForm control={control} />
        </CardContent>
      </Card>

      {start.isError && (
        <Alert variant="destructive" role="alert">
          <AlertDescription>
            {apiErrors && apiErrors.length > 0 ? (
              <ul className="list-disc pl-4">
                {apiErrors.map((error) => (
                  <li key={error}>{error}</li>
                ))}
              </ul>
            ) : (
              start.error.message
            )}
          </AlertDescription>
        </Alert>
      )}

      <div className="flex flex-wrap items-center gap-4">
        <Button type="submit" disabled={!parsed.success || start.isPending}>
          <FlaskConical />
          {start.isPending ? "Starting…" : "Evaluate"}
        </Button>
        <p className="flex items-center gap-1.5 text-sm text-muted-foreground">
          <Clock className="size-4" />
          Local importance and fuzzy interpretation can take several minutes.
        </p>
      </div>
    </form>
  );
}
