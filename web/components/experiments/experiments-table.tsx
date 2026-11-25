"use client";

import {
  type ColumnDef,
  flexRender,
  tableFeatures,
  useTable,
} from "@tanstack/react-table";
import Link from "next/link";

import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { type Experiment, experimentPath } from "@/lib/experiments";

const features = tableFeatures({});

const dateFormat = new Intl.DateTimeFormat(undefined, {
  dateStyle: "medium",
  timeStyle: "short",
});

const columns: ColumnDef<typeof features, Experiment>[] = [
  {
    accessorKey: "name",
    header: "Name",
    cell: ({ row }) => (
      <Link
        href={experimentPath(row.original.id)}
        className="font-medium hover:underline"
      >
        {row.original.name}
      </Link>
    ),
  },
  {
    id: "dataset",
    header: "Dataset",
    cell: ({ row }) =>
      row.original.dataset ? (
        row.original.dataset.fileName
      ) : (
        <span className="text-muted-foreground">No dataset</span>
      ),
  },
  {
    id: "rows",
    header: "Rows",
    cell: ({ row }) => row.original.dataset?.rowCount ?? "—",
  },
  {
    accessorKey: "createdAt",
    header: "Created",
    cell: ({ row }) => dateFormat.format(new Date(row.original.createdAt)),
  },
];

/**
 * Every experiment, with its dataset (if any), row count and creation date. Shows an empty state with
 * a call to action when there are none.
 * @param experiments - the experiments to show, newest first
 * @returns the table, or an empty state
 */
export function ExperimentsTable({
  experiments,
}: {
  experiments: Experiment[];
}) {
  const table = useTable({
    features,
    columns,
    data: experiments,
    getRowId: (row) => row.id,
  });

  if (experiments.length === 0) {
    return (
      <div className="flex flex-col items-center gap-2 rounded-lg border border-dashed py-12 text-center">
        <p className="text-muted-foreground">
          No experiments yet. Create one to upload a dataset and get started.
        </p>
      </div>
    );
  }

  return (
    <div className="overflow-x-auto rounded-lg border">
      <Table>
        <TableHeader>
          {table.getHeaderGroups().map((headerGroup) => (
            <TableRow key={headerGroup.id}>
              {headerGroup.headers.map((header) => (
                <TableHead key={header.id}>
                  {header.isPlaceholder
                    ? null
                    : flexRender(
                        header.column.columnDef.header,
                        header.getContext(),
                      )}
                </TableHead>
              ))}
            </TableRow>
          ))}
        </TableHeader>
        <TableBody>
          {table.getRowModel().rows.map((row) => (
            <TableRow key={row.id}>
              {row.getAllCells().map((cell) => (
                <TableCell key={cell.id}>
                  {flexRender(cell.column.columnDef.cell, cell.getContext())}
                </TableCell>
              ))}
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}
