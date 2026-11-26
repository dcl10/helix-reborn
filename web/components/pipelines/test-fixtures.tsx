import { type ReactNode } from "react";
import { useForm, useWatch } from "react-hook-form";

import type { DatasetColumn } from "@/lib/experiments";
import {
  emptyForm,
  type PipelineCatalog,
  type PipelineFormValues,
} from "@/lib/pipelines";

import catalogJson from "../../../contracts/pipeline-catalog.json";

/** The repo's real pipeline catalog. */
export const catalog = catalogJson as unknown as PipelineCatalog;

/** A dataset with numeric and text columns. */
export const columns: DatasetColumn[] = [
  { name: "a", inferredType: "numeric", missingCount: 0 },
  { name: "site", inferredType: "text", missingCount: 0 },
  { name: "b", inferredType: "numeric", missingCount: 0 },
  { name: "y", inferredType: "numeric", missingCount: 0 },
  { name: "label", inferredType: "text", missingCount: 0 },
];

/**
 * Renders form-bound components in a real react-hook-form, and shows the form's values as JSON for assertions.
 * @param children - a render function given the form
 * @param defaults - overrides for the empty form's values
 * @returns the harness
 */
export function FormHarness({
  children,
  defaults,
}: {
  children: (form: ReturnType<typeof useForm<PipelineFormValues>>) => ReactNode;
  defaults?: Partial<PipelineFormValues>;
}) {
  const form = useForm<PipelineFormValues>({
    defaultValues: { ...emptyForm(columns), ...defaults },
  });
  const values = useWatch({ control: form.control });
  return (
    <>
      {children(form)}
      <output data-testid="values">{JSON.stringify(values)}</output>
    </>
  );
}

/**
 * The harness's current form values.
 * @param element - the `values` output
 * @returns the values
 */
export function valuesOf(element: HTMLElement): PipelineFormValues {
  return JSON.parse(element.textContent ?? "{}") as PipelineFormValues;
}
