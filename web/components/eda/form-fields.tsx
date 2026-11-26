"use client";

import {
  type Control,
  Controller,
  type FieldPath,
  type FieldValues,
} from "react-hook-form";

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
import { ScrollArea } from "@/components/ui/scroll-area";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

/** Props every field helper takes: the form's control, the field's name and its visible label. */
interface FieldProps<T extends FieldValues> {
  control: Control<T>;
  name: FieldPath<T>;
  label: string;
  description?: string;
}

/** One choice in a {@link SelectField}. */
export interface SelectOption {
  value: string;
  label: string;
}

/**
 * A labelled select bound to a form field. With `noneLabel`, it offers an extra "none" choice stored as `""`.
 * @param props - the field, its options and an optional "none" choice
 * @returns the field
 */
export function SelectField<T extends FieldValues>({
  control,
  name,
  label,
  description,
  options,
  noneLabel,
}: FieldProps<T> & { options: SelectOption[]; noneLabel?: string }) {
  const items = [
    ...(noneLabel ? [{ value: null, label: noneLabel }] : []),
    ...options,
  ];

  return (
    <Controller
      control={control}
      name={name}
      render={({ field, fieldState }) => (
        <Field data-invalid={fieldState.invalid}>
          <FieldLabel htmlFor={name}>{label}</FieldLabel>
          <Select
            items={items}
            value={field.value === "" ? null : field.value}
            onValueChange={(value) => field.onChange(value ?? "")}
          >
            <SelectTrigger
              id={name}
              className="w-full"
              aria-invalid={fieldState.invalid}
            >
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {items.map((item) => (
                <SelectItem key={item.value ?? ""} value={item.value}>
                  {item.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          {description && <FieldDescription>{description}</FieldDescription>}
          {fieldState.invalid && <FieldError errors={[fieldState.error]} />}
        </Field>
      )}
    />
  );
}

/**
 * A labelled number input bound to a form field. An empty input is `undefined` (optional fields use EDA's default).
 * @param props - the field and its input limits
 * @returns the field
 */
export function NumberField<T extends FieldValues>({
  control,
  name,
  label,
  description,
  min,
  max,
  step,
}: FieldProps<T> & { min?: number; max?: number; step?: number }) {
  return (
    <Controller
      control={control}
      name={name}
      render={({ field, fieldState }) => (
        <Field data-invalid={fieldState.invalid}>
          <FieldLabel htmlFor={name}>{label}</FieldLabel>
          <Input
            id={name}
            type="number"
            inputMode="decimal"
            min={min}
            max={max}
            step={step}
            name={field.name}
            ref={field.ref}
            onBlur={field.onBlur}
            value={field.value ?? ""}
            onChange={(event) =>
              field.onChange(
                event.target.value === ""
                  ? undefined
                  : event.target.valueAsNumber,
              )
            }
            aria-invalid={fieldState.invalid}
          />
          {description && <FieldDescription>{description}</FieldDescription>}
          {fieldState.invalid && <FieldError errors={[fieldState.error]} />}
        </Field>
      )}
    />
  );
}

/**
 * A labelled text input bound to a form field.
 * @param props - the field and an optional placeholder
 * @returns the field
 */
export function TextField<T extends FieldValues>({
  control,
  name,
  label,
  description,
  placeholder,
}: FieldProps<T> & { placeholder?: string }) {
  return (
    <Controller
      control={control}
      name={name}
      render={({ field, fieldState }) => (
        <Field data-invalid={fieldState.invalid}>
          <FieldLabel htmlFor={name}>{label}</FieldLabel>
          <Input
            {...field}
            id={name}
            value={field.value ?? ""}
            placeholder={placeholder}
            aria-invalid={fieldState.invalid}
          />
          {description && <FieldDescription>{description}</FieldDescription>}
          {fieldState.invalid && <FieldError errors={[fieldState.error]} />}
        </Field>
      )}
    />
  );
}

/**
 * A checkbox and its label, bound to a boolean form field.
 * @param props - the field
 * @returns the field
 */
export function CheckboxField<T extends FieldValues>({
  control,
  name,
  label,
}: FieldProps<T>) {
  return (
    <Controller
      control={control}
      name={name}
      render={({ field }) => (
        <Field orientation="horizontal">
          <Checkbox
            id={name}
            checked={field.value}
            onCheckedChange={(checked) => field.onChange(checked)}
          />
          <FieldLabel htmlFor={name}>{label}</FieldLabel>
        </Field>
      )}
    />
  );
}

/**
 * A multi-select of columns: a scrollable list of checkboxes, bound to a `string[]` form field, keeping the
 * columns' order.
 * @param props - the field and the columns to choose from
 * @returns the field
 */
export function ColumnChecklist<T extends FieldValues>({
  control,
  name,
  label,
  description,
  columns,
}: FieldProps<T> & { columns: string[] }) {
  return (
    <Controller
      control={control}
      name={name}
      render={({ field, fieldState }) => {
        const selected = new Set<string>(field.value);
        const toggle = (column: string, checked: boolean) => {
          if (checked) selected.add(column);
          else selected.delete(column);
          field.onChange(columns.filter((c) => selected.has(c)));
        };

        return (
          <FieldSet data-invalid={fieldState.invalid}>
            <FieldLegend variant="label">{label}</FieldLegend>
            {description && <FieldDescription>{description}</FieldDescription>}
            <ScrollArea className="max-h-48 rounded-lg border">
              <div className="flex flex-col gap-2 p-3">
                {columns.map((column) => {
                  const id = `${name}-${column}`;
                  return (
                    <Field key={column} orientation="horizontal">
                      <Checkbox
                        id={id}
                        checked={selected.has(column)}
                        onCheckedChange={(checked) => toggle(column, checked)}
                      />
                      <FieldLabel htmlFor={id} className="font-normal">
                        {column}
                      </FieldLabel>
                    </Field>
                  );
                })}
              </div>
            </ScrollArea>
            {fieldState.invalid && <FieldError errors={[fieldState.error]} />}
          </FieldSet>
        );
      }}
    />
  );
}
