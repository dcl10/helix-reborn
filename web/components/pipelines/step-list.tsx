"use client";

import { ArrowDown, ArrowUp, Plus, Trash2 } from "lucide-react";
import { type Control, useFieldArray } from "react-hook-form";

import { StepParamsForm } from "@/components/pipelines/step-params-form";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { paramsToForm } from "@/lib/json-schema-form";
import {
  findStep,
  type PipelineCatalog,
  type PipelineFormValues,
  type ProblemType,
  stepsFor,
} from "@/lib/pipelines";

const groups = [
  { kind: "preprocessor", label: "Preprocessing" },
  { kind: "feature_selector", label: "Feature selection" },
] as const;

/**
 * The pipeline's preprocessing and feature-selection steps, applied in order: an "Add step" menu of the catalog's
 * steps for the problem type, grouped by kind, and for each added step its description, params form and
 * move up / move down / remove buttons.
 * @param control - the editor form's control
 * @param catalog - the pipeline catalog
 * @param problemType - the chosen problem type, which filters the menu
 * @returns the step list
 */
export function StepList({
  control,
  catalog,
  problemType,
}: {
  control: Control<PipelineFormValues>;
  catalog: PipelineCatalog;
  problemType: ProblemType;
}) {
  const { fields, append, remove, move } = useFieldArray({
    control,
    name: "steps",
  });

  return (
    <div className="flex flex-col gap-4">
      {fields.length === 0 && (
        <p className="text-sm text-muted-foreground">
          No steps: the model gets the feature columns as they are.
        </p>
      )}

      <ol className="flex flex-col gap-3">
        {fields.map((item, index) => {
          const definition = findStep(catalog, item.step);
          const label = definition?.label ?? item.step;
          return (
            <li
              key={item.id}
              className="rounded-lg border p-4"
              aria-label={`Step ${index + 1}: ${label}`}
            >
              <div className="mb-3 flex items-start justify-between gap-2">
                <div>
                  <p className="font-medium">
                    {index + 1}. {label}
                  </p>
                  {definition && (
                    <p className="text-sm text-muted-foreground">
                      {definition.description}
                    </p>
                  )}
                </div>
                <div className="flex shrink-0 gap-1">
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon-sm"
                    aria-label={`Move ${label} up`}
                    disabled={index === 0}
                    onClick={() => move(index, index - 1)}
                  >
                    <ArrowUp />
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon-sm"
                    aria-label={`Move ${label} down`}
                    disabled={index === fields.length - 1}
                    onClick={() => move(index, index + 1)}
                  >
                    <ArrowDown />
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon-sm"
                    aria-label={`Remove ${label}`}
                    onClick={() => remove(index)}
                  >
                    <Trash2 />
                  </Button>
                </div>
              </div>
              {definition ? (
                <StepParamsForm
                  control={control}
                  name={`steps.${index}.params`}
                  schema={definition.params_schema}
                />
              ) : (
                <p className="text-sm text-destructive">
                  This step is no longer in the catalog. Remove it.
                </p>
              )}
            </li>
          );
        })}
      </ol>

      <div>
        <DropdownMenu>
          <DropdownMenuTrigger
            render={<Button type="button" variant="outline" />}
          >
            <Plus />
            Add step
          </DropdownMenuTrigger>
          <DropdownMenuContent align="start" className="min-w-64">
            {groups.map((group, i) => (
              <DropdownMenuGroup key={group.kind}>
                {i > 0 && <DropdownMenuSeparator />}
                <DropdownMenuLabel>{group.label}</DropdownMenuLabel>
                {stepsFor(catalog, [group.kind], problemType).map((step) => (
                  <DropdownMenuItem
                    key={step.id}
                    onClick={() =>
                      append({
                        step: step.id,
                        params: paramsToForm(step.params_schema),
                      })
                    }
                  >
                    {step.label}
                  </DropdownMenuItem>
                ))}
              </DropdownMenuGroup>
            ))}
          </DropdownMenuContent>
        </DropdownMenu>
      </div>
    </div>
  );
}
