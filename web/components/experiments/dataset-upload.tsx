"use client";

import { Upload } from "lucide-react";
import { type ChangeEvent, type DragEvent, useRef, useState } from "react";

import { useUploadDataset } from "@/components/experiments/use-experiments";
import { Button } from "@/components/ui/button";
import { MAX_DATASET_BYTES } from "@/lib/experiments";
import { cn } from "@/lib/utils";

const MAX_DATASET_MB = MAX_DATASET_BYTES / (1024 * 1024);

/**
 * Checks a file client-side before it's uploaded.
 * @param file - the chosen file
 * @returns an error message, or null when the file is fine to upload
 */
function validate(file: File): string | null {
  if (!file.name.toLowerCase().endsWith(".csv")) {
    return "Choose a CSV file.";
  }
  if (file.size > MAX_DATASET_BYTES) {
    return `That file is larger than the ${MAX_DATASET_MB} MB limit.`;
  }
  return null;
}

/**
 * The Data tab with no dataset yet: a drop zone and file input for a CSV, up to 50 MB, with an upload
 * button. Shows the API's error message on failure.
 * @param experimentId - the experiment to upload the dataset to
 * @returns the upload form
 */
export function DatasetUpload({ experimentId }: { experimentId: string }) {
  const [file, setFile] = useState<File | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [dragOver, setDragOver] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  const upload = useUploadDataset(experimentId);

  const chooseFile = (candidate: File) => {
    const problem = validate(candidate);
    setError(problem);
    setFile(problem ? null : candidate);
  };

  const onInputChange = (event: ChangeEvent<HTMLInputElement>) => {
    const candidate = event.target.files?.[0];
    if (candidate) chooseFile(candidate);
  };

  const onDrop = (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    setDragOver(false);
    const candidate = event.dataTransfer.files[0];
    if (candidate) chooseFile(candidate);
  };

  const onUpload = () => {
    if (!file) return;
    upload.mutate(file, {
      onError: (uploadError) => setError(uploadError.message),
    });
  };

  return (
    <div className="flex flex-col gap-4">
      <div
        role="button"
        tabIndex={0}
        onClick={() => inputRef.current?.click()}
        onKeyDown={(event) => {
          if (event.key === "Enter" || event.key === " ") {
            event.preventDefault();
            inputRef.current?.click();
          }
        }}
        onDragOver={(event) => {
          event.preventDefault();
          setDragOver(true);
        }}
        onDragLeave={() => setDragOver(false)}
        onDrop={onDrop}
        className={cn(
          "flex cursor-pointer flex-col items-center gap-2 rounded-lg border border-dashed p-10 text-center transition-colors",
          dragOver && "border-foreground bg-muted/50",
        )}
      >
        <Upload className="size-6 text-muted-foreground" />
        <p className="text-sm text-muted-foreground">
          Drag a CSV file here, or click to choose one. Up to {MAX_DATASET_MB}{" "}
          MB.
        </p>
        {file && <p className="text-sm font-medium">{file.name}</p>}
        <input
          ref={inputRef}
          type="file"
          accept=".csv"
          className="sr-only"
          onChange={onInputChange}
        />
      </div>
      {error && (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      )}
      <Button
        onClick={onUpload}
        disabled={!file || upload.isPending}
        className="self-start"
      >
        {upload.isPending ? "Uploading…" : "Upload"}
      </Button>
    </div>
  );
}
