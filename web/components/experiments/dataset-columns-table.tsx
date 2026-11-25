import { Badge } from "@/components/ui/badge";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import type { Dataset, DatasetColumnType } from "@/lib/experiments";

const typeVariants: Record<
  DatasetColumnType,
  "default" | "secondary" | "outline"
> = {
  numeric: "default",
  text: "secondary",
  empty: "outline",
};

/**
 * A dataset's columns, with their inferred type and how many values are missing.
 * @param dataset - the dataset whose columns to show
 * @returns the columns table
 */
export function DatasetColumnsTable({ dataset }: { dataset: Dataset }) {
  return (
    <div className="overflow-x-auto rounded-lg border">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Name</TableHead>
            <TableHead>Type</TableHead>
            <TableHead>Missing</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {dataset.columns.map((column) => (
            <TableRow key={column.name}>
              <TableCell className="font-medium">{column.name}</TableCell>
              <TableCell>
                <Badge variant={typeVariants[column.inferredType]}>
                  {column.inferredType}
                </Badge>
              </TableCell>
              <TableCell>{column.missingCount}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}
