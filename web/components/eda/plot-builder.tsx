"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { ChartScatter, Loader2 } from "lucide-react";
import { useForm, useWatch } from "react-hook-form";
import { toast } from "sonner";

import {
  CheckboxField,
  ColumnChecklist,
  NumberField,
  SelectField,
  type SelectOption,
} from "@/components/eda/form-fields";
import { PlotOptionsForm } from "@/components/eda/plot-options-form";
import { useCreatePlot } from "@/components/eda/use-eda";
import { Button } from "@/components/ui/button";
import {
  type ColumnSummary,
  defaultPlotValues,
  PLOT_TYPE_LABELS,
  PLOT_TYPES,
  plotSchema,
  type PlotValues,
  toPlotRequest,
} from "@/lib/eda";

const typeOptions = PLOT_TYPES.map((type) => ({
  value: type,
  label: PLOT_TYPE_LABELS[type],
}));

const methodOptions: SelectOption[] = [
  { value: "pearson", label: "Pearson" },
  { value: "spearman", label: "Spearman" },
  { value: "kendall", label: "Kendall" },
];

const diagonalOptions: SelectOption[] = [
  { value: "hist", label: "Histogram" },
  { value: "kde", label: "KDE" },
];

const scalingOptions: SelectOption[] = [
  { value: "standard", label: "Standard (z-score)" },
  { value: "minmax", label: "Min-max" },
  { value: "none", label: "None" },
];

/**
 * The "Create a plot" form: a plot type, that type's fields (column pickers built from the dataset summary,
 * numeric-only where EDA needs numbers), optional plot styling, and a Generate button. A plot can take up to a
 * minute; the button shows a spinner meanwhile, and the API's error shows below it.
 * @param experimentId - the experiment to plot
 * @param columns - the dataset's columns, from the EDA summary
 * @returns the form
 */
export function PlotBuilder({
  experimentId,
  columns,
}: {
  experimentId: string;
  columns: ColumnSummary[];
}) {
  const createPlot = useCreatePlot(experimentId);
  const form = useForm<PlotValues>({
    resolver: zodResolver(plotSchema),
    defaultValues: defaultPlotValues,
  });
  const type = useWatch({ control: form.control, name: "type" });

  const allColumns = columns.map((column) => column.name);
  const numericColumns = columns
    .filter((column) => column.is_numeric)
    .map((column) => column.name);
  const allColumnOptions = allColumns.map((name) => ({
    value: name,
    label: name,
  }));

  const onSubmit = (values: PlotValues) =>
    createPlot.mutateAsync(toPlotRequest(values)).then(
      (artifact) => toast.success(`Created “${artifact.title}”`),
      () => {
        // Shown inline from createPlot.error.
      },
    );

  const { control } = form;

  return (
    <form
      noValidate
      onSubmit={form.handleSubmit(onSubmit)}
      className="flex flex-col gap-5"
      aria-label="Create a plot"
    >
      <div className="max-w-xs">
        <SelectField
          control={control}
          name="type"
          label="Plot type"
          options={typeOptions}
        />
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        {type === "targetDistribution" && (
          <>
            <SelectField
              control={control}
              name="targetColumn"
              label="Target column"
              options={allColumnOptions}
              noneLabel="Choose a column"
            />
            <NumberField
              control={control}
              name="bins"
              label="Bins"
              min={5}
              max={200}
            />
            <CheckboxField
              control={control}
              name="showKde"
              label="Overlay a KDE curve"
            />
          </>
        )}

        {type === "correlationHeatmap" && (
          <>
            <ColumnChecklist
              control={control}
              name="columns"
              label="Columns"
              description="Numeric columns only. Leave empty for all of them."
              columns={numericColumns}
            />
            <div className="flex flex-col gap-4">
              <SelectField
                control={control}
                name="method"
                label="Method"
                options={methodOptions}
              />
              <CheckboxField
                control={control}
                name="annotate"
                label="Show values in the cells"
              />
            </div>
          </>
        )}

        {type === "pairplot" && (
          <>
            <ColumnChecklist
              control={control}
              name="columns"
              label="Columns"
              description="Choose 2 to 10 numeric columns."
              columns={numericColumns}
            />
            <div className="flex flex-col gap-4">
              <SelectField
                control={control}
                name="hueColumn"
                label="Colour by"
                options={allColumnOptions}
                noneLabel="None"
              />
              <SelectField
                control={control}
                name="diagKind"
                label="Diagonal"
                options={diagonalOptions}
              />
              <CheckboxField
                control={control}
                name="corner"
                label="Lower triangle only"
              />
            </div>
          </>
        )}

        {(type === "pca" || type === "tsne") && (
          <>
            <ColumnChecklist
              control={control}
              name="columns"
              label="Columns"
              description="Numeric columns only. Leave empty for all of them."
              columns={numericColumns}
            />
            <div className="flex flex-col gap-4">
              <SelectField
                control={control}
                name="colourColumn"
                label="Colour by"
                options={allColumnOptions}
                noneLabel="None"
              />
              <SelectField
                control={control}
                name="scaling"
                label="Scaling"
                options={scalingOptions}
              />
              {type === "tsne" && (
                <NumberField
                  control={control}
                  name="perplexity"
                  label="Perplexity"
                  description="Between 5 and 100, and less than the number of rows."
                  min={5}
                  max={100}
                />
              )}
            </div>
          </>
        )}
      </div>

      <PlotOptionsForm control={control} />

      {createPlot.isError && (
        <p role="alert" className="text-sm text-destructive">
          {createPlot.error.message}
        </p>
      )}

      <div className="flex items-center gap-3">
        <Button type="submit" disabled={createPlot.isPending}>
          {createPlot.isPending ? (
            <Loader2 className="animate-spin" />
          ) : (
            <ChartScatter />
          )}
          {createPlot.isPending ? "Generating…" : "Generate"}
        </Button>
        {createPlot.isPending && (
          <span className="text-sm text-muted-foreground">
            This can take up to a minute.
          </span>
        )}
      </div>
    </form>
  );
}
