import { z } from "zod";

import { API_BASE, apiFetch } from "@/lib/api";

/** A non-numeric column's value and how many rows hold it. */
export interface TopValue {
  value: string;
  count: number;
}

/**
 * One column of the EDA summary. Passed through from the EDA service, so the keys are snake_case. The statistics
 * are null for non-numeric columns; `top_values` is null for numeric ones.
 */
export interface ColumnSummary {
  name: string;
  dtype: string;
  is_numeric: boolean;
  non_null: number;
  missing: number;
  missing_pct: number;
  unique: number;
  mean: number | null;
  std: number | null;
  min: number | null;
  q25: number | null;
  median: number | null;
  q75: number | null;
  max: number | null;
  top_values: TopValue[] | null;
}

/** `GET /api/experiments/{id}/eda/summary`: the dataset's shape and per-column statistics (snake_case). */
export interface DatasetSummary {
  row_count: number;
  column_count: number;
  columns: ColumnSummary[];
}

/** What kind of file an artifact is (the API's `ArtifactKind`, camelCase). */
export type ArtifactKind =
  | "dataset"
  | "edaPlot"
  | "edaTable"
  | "model"
  | "modelMetadata"
  | "split"
  | "cvMetrics"
  | "metrics"
  | "evaluationPlot"
  | "evaluationTable"
  | "predictions";

/** A file an experiment owns, as the API returns it. Dates are ISO 8601 strings. */
export interface Artifact {
  id: string;
  kind: ArtifactKind;
  title: string;
  contentType: string;
  sizeBytes: number;
  sourceType: string | null;
  sourceId: string | null;
  createdAt: string;
  fileName: string;
}

/** The EDA plots the API can make. */
export const PLOT_TYPES = [
  "targetDistribution",
  "correlationHeatmap",
  "pairplot",
  "pca",
  "tsne",
] as const;

/** One of {@link PLOT_TYPES}. */
export type PlotType = (typeof PLOT_TYPES)[number];

/** A label for each plot type, for the plot type select. */
export const PLOT_TYPE_LABELS: Record<PlotType, string> = {
  targetDistribution: "Target distribution",
  correlationHeatmap: "Correlation heatmap",
  pairplot: "Pairplot",
  pca: "PCA",
  tsne: "t-SNE",
};

/** Seaborn themes a plot can use. */
export const PLOT_STYLES = [
  "white",
  "dark",
  "whitegrid",
  "darkgrid",
  "ticks",
] as const;

/**
 * Styling options sent as a plot's `plotOptions`. The API passes these to the EDA service unchanged and EDA rejects
 * unknown keys, so they're snake_case like EDA's `PlotOptions`. Any field left out uses EDA's default.
 */
export interface PlotOptions {
  dpi?: number;
  width?: number;
  height?: number;
  title?: string;
  style?: (typeof PLOT_STYLES)[number];
  colour_map?: string;
  axis_font_size?: number;
  tick_size?: number;
  title_font_size?: number;
  font_family?: string;
  rotate_x_labels?: number;
  rotate_y_labels?: number;
}

/** Body of `POST /api/experiments/{id}/eda/plots`. `options` holds the plot type's own fields, snake_case. */
export interface PlotRequest {
  type: PlotType;
  options: Record<string, unknown>;
  plotOptions: PlotOptions | null;
}

/** One column's normality test results (snake_case, passed through from EDA). */
export interface NormalityRow {
  column: string;
  n: number;
  shapiro_statistic: number;
  shapiro_p: number;
  ks_statistic: number;
  ks_p: number;
  normal_at_0_05: boolean;
}

/** `POST /api/experiments/{id}/eda/tables/normality`: the CSV's artifact and the table's rows. */
export interface NormalityTable {
  artifact: Artifact;
  rows: NormalityRow[];
}

function edaPath(experimentId: string, path: string): string {
  return `/experiments/${encodeURIComponent(experimentId)}/eda/${path}`;
}

/**
 * Gets the experiment's dataset summary. The API answers 409 when there's no dataset yet.
 * @param experimentId - the experiment ID
 * @returns the summary
 */
export function getEdaSummary(experimentId: string): Promise<DatasetSummary> {
  return apiFetch<DatasetSummary>(edaPath(experimentId, "summary"));
}

/**
 * Renders a plot of the experiment's dataset. Can take up to a minute (t-SNE on a large dataset).
 * @param experimentId - the experiment ID
 * @param request - the plot type, its options and styling
 * @returns the new plot's artifact
 */
export function createPlot(
  experimentId: string,
  request: PlotRequest,
): Promise<Artifact> {
  return apiFetch<Artifact>(edaPath(experimentId, "plots"), {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });
}

/**
 * Runs Shapiro-Wilk and Kolmogorov-Smirnov normality tests on the experiment's dataset.
 * @param experimentId - the experiment ID
 * @param columns - the columns to test, or null for every numeric column
 * @returns the table's artifact and rows
 */
export function runNormalityTests(
  experimentId: string,
  columns: string[] | null,
): Promise<NormalityTable> {
  return apiFetch<NormalityTable>(edaPath(experimentId, "tables/normality"), {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ columns }),
  });
}

/**
 * Lists the experiment's EDA plots and tables, oldest first.
 * @param experimentId - the experiment ID
 * @returns the artifacts
 */
export function listEdaArtifacts(experimentId: string): Promise<Artifact[]> {
  return apiFetch<Artifact[]>(edaPath(experimentId, "artifacts"));
}

/**
 * The URL of an artifact's content, through the authenticated proxy.
 * @param experimentId - the experiment ID
 * @param artifactId - the artifact ID
 * @param download - whether to ask the browser to download it rather than show it
 * @returns the URL
 */
export function artifactContentUrl(
  experimentId: string,
  artifactId: string,
  download = false,
): string {
  const url = `${API_BASE}/experiments/${encodeURIComponent(experimentId)}/artifacts/${encodeURIComponent(artifactId)}/content`;
  return download ? `${url}?download=true` : url;
}

/** An optional number field: empty means "use EDA's default". */
const optionalNumber = z.number().optional();

/** The plot options form's fields: every one optional, checked against EDA's own limits where it has them. */
export const plotOptionsSchema = z.object({
  title: z.string().trim().optional(),
  dpi: z
    .number()
    .int("Enter a whole number.")
    .min(50, "Use at least 50 DPI.")
    .max(600, "Use at most 600 DPI.")
    .optional(),
  width: z.number().positive("Enter a positive width.").optional(),
  height: z.number().positive("Enter a positive height.").optional(),
  style: z.enum(PLOT_STYLES).optional(),
  colour_map: z.string().trim().optional(),
  axis_font_size: optionalNumber,
  tick_size: optionalNumber,
  title_font_size: optionalNumber,
  font_family: z.string().trim().optional(),
  rotate_x_labels: optionalNumber,
  rotate_y_labels: optionalNumber,
});

/** Values of the plot options form. */
export type PlotOptionsValues = z.infer<typeof plotOptionsSchema>;

/**
 * The plot builder's fields and their validation. Every plot type's fields are here at once so switching type keeps
 * what was entered; {@link toPlotRequest} sends only the chosen type's. Limits are the EDA service's own.
 */
export const plotSchema = z
  .object({
    type: z.enum(PLOT_TYPES),
    targetColumn: z.string(),
    showKde: z.boolean(),
    bins: z.number().int("Enter a whole number."),
    columns: z.array(z.string()),
    method: z.enum(["pearson", "spearman", "kendall"]),
    annotate: z.boolean(),
    hueColumn: z.string(),
    corner: z.boolean(),
    diagKind: z.enum(["hist", "kde"]),
    colourColumn: z.string(),
    scaling: z.enum(["standard", "minmax", "none"]),
    perplexity: z.number(),
    plotOptions: plotOptionsSchema,
  })
  .superRefine((values, ctx) => {
    const issue = (path: string, message: string) =>
      ctx.addIssue({ code: "custom", path: [path], message });

    switch (values.type) {
      case "targetDistribution":
        if (!values.targetColumn) issue("targetColumn", "Choose a column.");
        if (
          !Number.isFinite(values.bins) ||
          values.bins < 5 ||
          values.bins > 200
        ) {
          issue("bins", "Use between 5 and 200 bins.");
        }
        break;
      case "pairplot":
        if (values.columns.length < 2 || values.columns.length > 10) {
          issue("columns", "Choose between 2 and 10 columns.");
        }
        break;
      case "pca":
      case "tsne":
        if (values.columns.length === 1) {
          issue(
            "columns",
            "Choose at least 2 columns, or none to use them all.",
          );
        }
        if (
          values.type === "tsne" &&
          (!Number.isFinite(values.perplexity) ||
            values.perplexity < 5 ||
            values.perplexity > 100)
        ) {
          issue("perplexity", "Use a perplexity between 5 and 100.");
        }
        break;
      case "correlationHeatmap":
        if (values.columns.length === 1) {
          issue(
            "columns",
            "Choose at least 2 columns, or none to use them all.",
          );
        }
        break;
    }
  });

/** Values of the plot builder form. */
export type PlotValues = z.infer<typeof plotSchema>;

/** The plot builder's starting values: EDA's defaults. */
export const defaultPlotValues: PlotValues = {
  type: "correlationHeatmap",
  targetColumn: "",
  showKde: true,
  bins: 30,
  columns: [],
  method: "pearson",
  annotate: true,
  hueColumn: "",
  corner: true,
  diagKind: "hist",
  colourColumn: "",
  scaling: "standard",
  perplexity: 30,
  plotOptions: {},
};

/**
 * Builds the API request for the plot builder's values: only the chosen type's fields, in EDA's snake_case. An empty
 * column list or column choice becomes null (EDA's "all numeric columns" / "none"), and plot options left empty are
 * left out so EDA's defaults apply.
 * @param values - the form's values
 * @returns the request body
 */
export function toPlotRequest(values: PlotValues): PlotRequest {
  const columnsOrAll = values.columns.length > 0 ? values.columns : null;

  const options: Record<PlotType, Record<string, unknown>> = {
    targetDistribution: {
      target_column: values.targetColumn,
      show_kde: values.showKde,
      bins: values.bins,
    },
    correlationHeatmap: {
      columns: columnsOrAll,
      method: values.method,
      annotate: values.annotate,
    },
    pairplot: {
      columns: values.columns,
      hue_column: values.hueColumn || null,
      corner: values.corner,
      diag_kind: values.diagKind,
    },
    pca: {
      columns: columnsOrAll,
      colour_column: values.colourColumn || null,
      scaling: values.scaling,
    },
    tsne: {
      columns: columnsOrAll,
      colour_column: values.colourColumn || null,
      scaling: values.scaling,
      perplexity: values.perplexity,
    },
  };

  const plotOptions = Object.fromEntries(
    Object.entries(values.plotOptions).filter(
      ([, value]) => value !== undefined && value !== "",
    ),
  ) as PlotOptions;

  return {
    type: values.type,
    options: options[values.type],
    plotOptions: Object.keys(plotOptions).length > 0 ? plotOptions : null,
  };
}
