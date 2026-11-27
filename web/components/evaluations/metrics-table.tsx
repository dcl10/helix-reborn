import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import type { MetricsFile } from "@/lib/evaluations";
import { formatMetric } from "@/lib/runs";

/**
 * Train and test metrics side by side, one row per metric (in the order the test set lists them).
 * @param metrics - the evaluation's `metrics.json`
 * @returns the table
 */
export function MetricsTable({ metrics }: { metrics: MetricsFile }) {
  const names = [
    ...new Set([...Object.keys(metrics.test), ...Object.keys(metrics.train)]),
  ];
  return (
    <div className="overflow-x-auto rounded-lg border">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Metric</TableHead>
            <TableHead className="text-right">Train</TableHead>
            <TableHead className="text-right">Test</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {names.map((name) => (
            <TableRow key={name}>
              <TableCell>{name}</TableCell>
              <TableCell className="text-right tabular-nums">
                {formatMetric(metrics.train[name])}
              </TableCell>
              <TableCell className="text-right tabular-nums">
                {formatMetric(metrics.test[name])}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}
