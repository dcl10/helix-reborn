"use client";

import { ChevronDown } from "lucide-react";
import type { Control, FieldPath, FieldValues } from "react-hook-form";

import {
  NumberField,
  SelectField,
  TextField,
} from "@/components/eda/form-fields";
import { Button } from "@/components/ui/button";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible";
import { PLOT_STYLES } from "@/lib/eda";

const styleOptions = PLOT_STYLES.map((style) => ({
  value: style,
  label: style,
}));

/**
 * A collapsible "Plot options" section: title, style, size and fonts, bound to the form's `plotOptions` fields
 * (`plotOptionsSchema`). Every field is optional; anything left empty uses the service's default. Used by the EDA plot
 * builder and the new evaluation form.
 * @param control - the form's control; its values have a `plotOptions` object
 * @returns the section
 */
export function PlotOptionsForm<T extends FieldValues>({
  control,
}: {
  control: Control<T>;
}) {
  const name = (field: string) => `plotOptions.${field}` as FieldPath<T>;
  return (
    <Collapsible>
      <CollapsibleTrigger
        render={
          <Button
            type="button"
            variant="ghost"
            size="sm"
            className="group -ml-2"
          />
        }
      >
        <ChevronDown className="transition-transform group-data-panel-open:rotate-180" />
        Plot options
      </CollapsibleTrigger>
      <CollapsibleContent className="pt-3">
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <TextField
            control={control}
            name={name("title")}
            label="Title"
            placeholder="Default title"
          />
          <SelectField
            control={control}
            name={name("style")}
            label="Style"
            options={styleOptions}
            noneLabel="Default (whitegrid)"
          />
          <TextField
            control={control}
            name={name("colour_map")}
            label="Colour map"
            placeholder="viridis"
          />
          <NumberField
            control={control}
            name={name("width")}
            label="Width (inches)"
            min={0}
            step={0.5}
          />
          <NumberField
            control={control}
            name={name("height")}
            label="Height (inches)"
            min={0}
            step={0.5}
          />
          <NumberField
            control={control}
            name={name("dpi")}
            label="DPI"
            min={50}
            max={600}
          />
          <TextField
            control={control}
            name={name("font_family")}
            label="Font family"
            placeholder="sans-serif"
          />
          <NumberField
            control={control}
            name={name("title_font_size")}
            label="Title font size"
          />
          <NumberField
            control={control}
            name={name("axis_font_size")}
            label="Axis font size"
          />
          <NumberField
            control={control}
            name={name("tick_size")}
            label="Tick label size"
          />
          <NumberField
            control={control}
            name={name("rotate_x_labels")}
            label="Rotate x labels (°)"
          />
          <NumberField
            control={control}
            name={name("rotate_y_labels")}
            label="Rotate y labels (°)"
          />
        </div>
      </CollapsibleContent>
    </Collapsible>
  );
}
