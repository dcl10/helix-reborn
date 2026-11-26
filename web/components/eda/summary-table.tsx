import { Badge } from "@/components/ui/badge";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import type { ColumnSummary, DatasetSummary } from "@/lib/eda";

const statFormat = new Intl.NumberFormat(undefined, {
  maximumSignificantDigits: 4,
});
const percentFormat = new Intl.NumberFormat(undefined, {
  maximumFractionDigits: 1,
});

/**
 * Formats a statistic, or an em dash when it's null (a non-numeric column).
 * @param value - the statistic
 * @returns the formatted value
 */
export function formatStat(value: number | null): string {
  return value === null ? "—" : statFormat.format(value);
}

/** A text column's name, with its most common values in a tooltip. */
function ColumnName({ column }: { column: ColumnSummary }) {
  if (!column.top_values?.length) return <>{column.name}</>;

  return (
    <Tooltip>
      <TooltipTrigger
        render={
          <span className="cursor-help underline decoration-dotted underline-offset-4" />
        }
      >
        {column.name}
      </TooltipTrigger>
      <TooltipContent>
        <p className="mb-1 font-medium">Top values</p>
        <ul>
          {column.top_values.map((top) => (
            <li key={top.value}>
              {top.value}: {top.count}
            </li>
          ))}
        </ul>
      </TooltipContent>
    </Tooltip>
  );
}

/**
 * The dataset summary: one row per column with its type, missing values, distinct values and numeric statistics
 * (`—` where a statistic doesn't apply). Text columns show their most common values in a tooltip on the name.
 * @param summary - the EDA summary
 * @returns the summary table
 */
export function SummaryTable({ summary }: { summary: DatasetSummary }) {
  return (
    <div className="overflow-x-auto rounded-lg border">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Column</TableHead>
            <TableHead>Type</TableHead>
            <TableHead className="text-right">Missing</TableHead>
            <TableHead className="text-right">Unique</TableHead>
            <TableHead className="text-right">Mean</TableHead>
            <TableHead className="text-right">Std</TableHead>
            <TableHead className="text-right">Min</TableHead>
            <TableHead className="text-right">Median</TableHead>
            <TableHead className="text-right">Max</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {summary.columns.map((column) => (
            <TableRow key={column.name}>
              <TableCell className="font-medium">
                <ColumnName column={column} />
              </TableCell>
              <TableCell>
                <Badge variant={column.is_numeric ? "default" : "secondary"}>
                  {column.is_numeric ? "numeric" : "text"}
                </Badge>
              </TableCell>
              <TableCell className="text-right tabular-nums">
                {column.missing} ({percentFormat.format(column.missing_pct)}%)
              </TableCell>
              <TableCell className="text-right tabular-nums">
                {column.unique}
              </TableCell>
              {(["mean", "std", "min", "median", "max"] as const).map(
                (stat) => (
                  <TableCell key={stat} className="text-right tabular-nums">
                    {formatStat(column[stat])}
                  </TableCell>
                ),
              )}
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}
