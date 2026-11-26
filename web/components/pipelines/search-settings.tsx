"use client";

import {
  type Control,
  Controller,
  type UseFormSetValue,
  useWatch,
} from "react-hook-form";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Field,
  FieldDescription,
  FieldError,
  FieldLabel,
} from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import { Switch } from "@/components/ui/switch";
import { describeFields } from "@/lib/json-schema-form";
import {
  findStep,
  formatGridValue,
  gridSeparator,
  gridToText,
  type PipelineCatalog,
  type PipelineFormValues,
} from "@/lib/pipelines";

/**
 * The optional hyperparameter search: method (grid or random), its CV folds and, for random, how many candidates to
 * try. The grid is the model's default from the catalog, shown read-only, until "Customise" turns it into one text
 * field per param (comma-separated candidates, `;`-separated for a list param).
 * @param control - the editor form's control
 * @param setValue - the editor form's setValue
 * @param catalog - the pipeline catalog
 * @returns the search settings
 */
export function SearchSettings({
  control,
  setValue,
  catalog,
}: {
  control: Control<PipelineFormValues>;
  setValue: UseFormSetValue<PipelineFormValues>;
  catalog: PipelineCatalog;
}) {
  const [enabled, method, customGrid, modelId] = useWatch({
    control,
    name: ["searchEnabled", "searchMethod", "customGrid", "model.step"],
  });
  const model = findStep(catalog, modelId);
  const fields = model ? describeFields(model.params_schema) : [];

  const customise = () => {
    const grid = model?.search_grid ?? {};
    setValue(
      "paramGrid",
      Object.fromEntries(
        fields.map((field) => [
          field.name,
          grid[field.name] ? gridToText(field, grid[field.name]) : "",
        ]),
      ),
    );
    setValue("customGrid", true, { shouldValidate: true });
  };

  return (
    <div className="flex flex-col gap-4">
      <Controller
        control={control}
        name="searchEnabled"
        render={({ field }) => (
          <Field orientation="horizontal">
            <Switch
              id="search-enabled"
              checked={field.value}
              onCheckedChange={field.onChange}
            />
            <FieldLabel htmlFor="search-enabled">
              Search for the best model settings
            </FieldLabel>
          </Field>
        )}
      />

      {enabled && (
        <>
          <Controller
            control={control}
            name="searchMethod"
            render={({ field }) => (
              <Field>
                <FieldLabel>Method</FieldLabel>
                <RadioGroup
                  value={field.value}
                  onValueChange={field.onChange}
                  className="flex gap-6"
                >
                  <Field orientation="horizontal">
                    <RadioGroupItem id="search-grid" value="grid" />
                    <FieldLabel htmlFor="search-grid">
                      Grid: try every combination
                    </FieldLabel>
                  </Field>
                  <Field orientation="horizontal">
                    <RadioGroupItem id="search-random" value="random" />
                    <FieldLabel htmlFor="search-random">
                      Random: try some combinations
                    </FieldLabel>
                  </Field>
                </RadioGroup>
              </Field>
            )}
          />

          <div className="grid gap-4 sm:grid-cols-2">
            <Controller
              control={control}
              name="searchCvFolds"
              render={({ field, fieldState }) => (
                <Field data-invalid={fieldState.invalid}>
                  <FieldLabel htmlFor="search-cv-folds">
                    Search CV folds
                  </FieldLabel>
                  <Input
                    id="search-cv-folds"
                    inputMode="numeric"
                    {...field}
                    aria-invalid={fieldState.invalid}
                  />
                  <FieldDescription>2 to 10.</FieldDescription>
                  {fieldState.invalid && (
                    <FieldError errors={[fieldState.error]} />
                  )}
                </Field>
              )}
            />
            {method === "random" && (
              <Controller
                control={control}
                name="nIter"
                render={({ field, fieldState }) => (
                  <Field data-invalid={fieldState.invalid}>
                    <FieldLabel htmlFor="search-n-iter">
                      Combinations to try
                    </FieldLabel>
                    <Input
                      id="search-n-iter"
                      inputMode="numeric"
                      {...field}
                      aria-invalid={fieldState.invalid}
                    />
                    <FieldDescription>1 to 500.</FieldDescription>
                    {fieldState.invalid && (
                      <FieldError errors={[fieldState.error]} />
                    )}
                  </Field>
                )}
              />
            )}
          </div>

          {!model ? (
            <p className="text-sm text-muted-foreground">
              Choose a model to see its search grid.
            </p>
          ) : customGrid ? (
            <div className="flex flex-col gap-4">
              <div className="grid gap-4 sm:grid-cols-2">
                {fields.map((param) => (
                  <Controller
                    key={param.name}
                    control={control}
                    name={`paramGrid.${param.name}`}
                    render={({ field, fieldState }) => (
                      <Field data-invalid={fieldState.invalid}>
                        <FieldLabel htmlFor={`grid-${param.name}`}>
                          {param.label}
                        </FieldLabel>
                        <Input
                          id={`grid-${param.name}`}
                          {...field}
                          value={field.value ?? ""}
                          placeholder="Not searched"
                          aria-invalid={fieldState.invalid}
                        />
                        <FieldDescription>
                          {gridSeparator(param) === ";"
                            ? "Candidates separated by ;, each a comma-separated list."
                            : `Comma-separated candidates${param.nullable ? " (None allowed)" : ""}.`}
                        </FieldDescription>
                        {fieldState.invalid && (
                          <FieldError errors={[fieldState.error]} />
                        )}
                      </Field>
                    )}
                  />
                ))}
              </div>
              {model.search_grid && (
                <div>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onClick={() =>
                      setValue("customGrid", false, { shouldValidate: true })
                    }
                  >
                    Use the default grid
                  </Button>
                </div>
              )}
            </div>
          ) : (
            <Controller
              control={control}
              name="customGrid"
              render={({ fieldState }) => (
                <Field data-invalid={fieldState.invalid}>
                  <FieldLabel>Default grid</FieldLabel>
                  {model.search_grid ? (
                    <dl className="flex flex-col gap-2 text-sm">
                      {Object.entries(model.search_grid).map(
                        ([param, values]) => (
                          <div
                            key={param}
                            className="flex flex-wrap items-center gap-1.5"
                          >
                            <dt className="mr-1 font-medium">
                              {fields.find((f) => f.name === param)?.label ??
                                param}
                            </dt>
                            {values.map((value) => (
                              <dd key={formatGridValue(value)}>
                                <Badge variant="secondary">
                                  {formatGridValue(value)}
                                </Badge>
                              </dd>
                            ))}
                          </div>
                        ),
                      )}
                    </dl>
                  ) : (
                    <p className="text-sm text-muted-foreground">
                      {model.label} has no default grid.
                    </p>
                  )}
                  {fieldState.invalid && (
                    <FieldError errors={[fieldState.error]} />
                  )}
                  <div>
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      onClick={customise}
                    >
                      Customise
                    </Button>
                  </div>
                </Field>
              )}
            />
          )}
        </>
      )}
    </div>
  );
}
