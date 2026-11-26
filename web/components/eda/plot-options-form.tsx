"use client";

import { ChevronDown } from "lucide-react";
import type { Control } from "react-hook-form";

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
import { PLOT_STYLES, type PlotValues } from "@/lib/eda";

const styleOptions = PLOT_STYLES.map((style) => ({
  value: style,
  label: style,
}));

/**
 * The plot builder's collapsible "Plot options" section: title, style, size and fonts. Every field is optional;
 * anything left empty uses the EDA service's default.
 * @param control - the plot builder form's control
 * @returns the section
 */
export function PlotOptionsForm({ control }: { control: Control<PlotValues> }) {
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
            name="plotOptions.title"
            label="Title"
            placeholder="Default title"
          />
          <SelectField
            control={control}
            name="plotOptions.style"
            label="Style"
            options={styleOptions}
            noneLabel="Default (whitegrid)"
          />
          <TextField
            control={control}
            name="plotOptions.colour_map"
            label="Colour map"
            placeholder="viridis"
          />
          <NumberField
            control={control}
            name="plotOptions.width"
            label="Width (inches)"
            min={0}
            step={0.5}
          />
          <NumberField
            control={control}
            name="plotOptions.height"
            label="Height (inches)"
            min={0}
            step={0.5}
          />
          <NumberField
            control={control}
            name="plotOptions.dpi"
            label="DPI"
            min={50}
            max={600}
          />
          <TextField
            control={control}
            name="plotOptions.font_family"
            label="Font family"
            placeholder="sans-serif"
          />
          <NumberField
            control={control}
            name="plotOptions.title_font_size"
            label="Title font size"
          />
          <NumberField
            control={control}
            name="plotOptions.axis_font_size"
            label="Axis font size"
          />
          <NumberField
            control={control}
            name="plotOptions.tick_size"
            label="Tick label size"
          />
          <NumberField
            control={control}
            name="plotOptions.rotate_x_labels"
            label="Rotate x labels (°)"
          />
          <NumberField
            control={control}
            name="plotOptions.rotate_y_labels"
            label="Rotate y labels (°)"
          />
        </div>
      </CollapsibleContent>
    </Collapsible>
  );
}
