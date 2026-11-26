"use client";

import { type Control, Controller, type FieldPath } from "react-hook-form";

import { Checkbox } from "@/components/ui/checkbox";
import {
  Field,
  FieldDescription,
  FieldError,
  FieldLabel,
} from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  describeFields,
  type FieldDescriptor,
  type JsonSchema,
} from "@/lib/json-schema-form";
import type { PipelineFormValues } from "@/lib/pipelines";

/** The field's limits as a hint, e.g. "10 to 1000", "more than 0" or "1 to 5 values, each 1 to 1000". */
function boundsHint(field: FieldDescriptor): string | undefined {
  let range: string | undefined;
  if (field.min !== undefined && field.max !== undefined) {
    range = `${field.min} to ${field.max}`;
  } else {
    const parts = [
      field.min !== undefined && `at least ${field.min}`,
      field.exclusiveMin !== undefined && `more than ${field.exclusiveMin}`,
      field.max !== undefined && `at most ${field.max}`,
      field.exclusiveMax !== undefined && `less than ${field.exclusiveMax}`,
    ].filter(Boolean);
    range = parts.length > 0 ? parts.join(" and ") : undefined;
  }

  if (field.kind === "intArray") {
    const count =
      field.minItems !== undefined && field.maxItems !== undefined
        ? `${field.minItems} to ${field.maxItems} comma-separated values`
        : "comma-separated values";
    return range ? `${count}, each ${range}` : count;
  }
  return range;
}

/** One param's input, by its kind. */
function ParamField({
  control,
  name,
  field,
  idPrefix,
}: {
  control: Control<PipelineFormValues>;
  name: FieldPath<PipelineFormValues>;
  field: FieldDescriptor;
  idPrefix: string;
}) {
  const id = `${idPrefix}-${field.name}`;
  const hint = [field.description, boundsHint(field)]
    .filter(Boolean)
    .join(" · ");

  return (
    <Controller
      control={control}
      name={name}
      render={({ field: input, fieldState }) => {
        if (field.kind === "boolean") {
          return (
            <Field orientation="horizontal" data-invalid={fieldState.invalid}>
              <Checkbox
                id={id}
                checked={Boolean(input.value)}
                onCheckedChange={(checked) => input.onChange(checked)}
              />
              <FieldLabel htmlFor={id}>{field.label}</FieldLabel>
            </Field>
          );
        }

        return (
          <Field data-invalid={fieldState.invalid}>
            <FieldLabel htmlFor={id}>{field.label}</FieldLabel>
            {field.kind === "enum" ? (
              <Select
                items={(field.options ?? []).map((o) => ({
                  value: String(o),
                  label: String(o),
                }))}
                value={
                  input.value === undefined || input.value === null
                    ? null
                    : String(input.value)
                }
                // Options may be numbers; the select works in strings, so map back to the option itself.
                onValueChange={(value) =>
                  input.onChange(
                    (field.options ?? []).find((o) => String(o) === value) ??
                      value,
                  )
                }
              >
                <SelectTrigger
                  id={id}
                  className="w-full"
                  aria-invalid={fieldState.invalid}
                >
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {(field.options ?? []).map((option) => (
                    <SelectItem key={String(option)} value={String(option)}>
                      {String(option)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            ) : (
              <Input
                id={id}
                name={input.name}
                ref={input.ref}
                onBlur={input.onBlur}
                value={(input.value as string | undefined) ?? ""}
                onChange={(event) => input.onChange(event.target.value)}
                inputMode={
                  field.kind === "integer" || field.kind === "intArray"
                    ? "numeric"
                    : "decimal"
                }
                placeholder={
                  field.nullable
                    ? "None"
                    : field.kind === "intArray"
                      ? "100, 50"
                      : undefined
                }
                aria-invalid={fieldState.invalid}
              />
            )}
            {hint && <FieldDescription>{hint}</FieldDescription>}
            {fieldState.invalid && <FieldError errors={[fieldState.error]} />}
          </Field>
        );
      }}
    />
  );
}

/**
 * A step's params form, generated from its `params_schema`: a number input per number (blank means None for a
 * nullable one), a checkbox per boolean, a select per enum, and a comma-separated input per int list.
 * @param control - the editor form's control
 * @param name - where the step's params are in the form, e.g. `steps.0.params` or `model.params`
 * @param schema - the step's `params_schema`
 * @returns the fields, or a note when the step has no params
 */
export function StepParamsForm({
  control,
  name,
  schema,
}: {
  control: Control<PipelineFormValues>;
  name: `steps.${number}.params` | "model.params";
  schema: JsonSchema;
}) {
  const fields = describeFields(schema);
  if (fields.length === 0) {
    return <p className="text-sm text-muted-foreground">No settings.</p>;
  }

  return (
    <div className="grid gap-4 sm:grid-cols-2">
      {fields.map((field) => (
        <ParamField
          key={field.name}
          control={control}
          name={`${name}.${field.name}` as FieldPath<PipelineFormValues>}
          field={field}
          idPrefix={name.replace(/\./g, "-")}
        />
      ))}
    </div>
  );
}
