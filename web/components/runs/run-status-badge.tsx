import { Badge } from "@/components/ui/badge";
import type { RunStatus } from "@/lib/runs";
import { cn } from "@/lib/utils";

const styles: Record<
  RunStatus,
  {
    label: string;
    className?: string;
    variant: "secondary" | "destructive" | "default";
  }
> = {
  queued: { label: "Queued", variant: "secondary" },
  running: {
    label: "Running",
    variant: "default",
    className: "bg-blue-600 text-white dark:bg-blue-500",
  },
  succeeded: {
    label: "Succeeded",
    variant: "default",
    className: "bg-green-600 text-white dark:bg-green-500",
  },
  failed: { label: "Failed", variant: "destructive" },
};

/**
 * A run's status: queued (grey), running (blue, with its progress), succeeded (green) or failed (red).
 * @param status - the status
 * @param progress - the progress, 0–1, shown while running
 * @returns the badge
 */
export function RunStatusBadge({
  status,
  progress,
}: {
  status: RunStatus;
  progress?: number | null;
}) {
  const style = styles[status];
  const percent =
    status === "running" && progress !== null && progress !== undefined
      ? ` ${Math.round(progress * 100)}%`
      : "";
  return (
    <Badge variant={style.variant} className={cn(style.className)}>
      {style.label}
      {percent}
    </Badge>
  );
}
