"""The evaluation worker: consumes ``evaluation.jobs`` and writes metrics to S3.

The base :class:`~helix_core.worker.Worker` handles the queue, events, ack/reject,
health and shutdown; this class loads the context, runs the enabled steps and returns
the result.
"""

from collections.abc import Callable
from typing import Any, ClassVar, Literal
from uuid import UUID

from helix_core.messages import EvaluationJob
from helix_core.settings import HelixSettings
from helix_core.storage import Storage
from helix_core.worker import EVALUATION_QUEUE, Worker
from helix_evaluation.context import load_context
from helix_evaluation.steps import run_steps


class EvaluationWorker(Worker[EvaluationJob]):
    """Evaluates each :class:`~helix_core.messages.EvaluationJob`'s ONNX model.

    Writes ``metrics.json`` and, as later issues add steps, plots and tables, under
    the job's ``output_prefix``.
    """

    queue_name: ClassVar[str] = EVALUATION_QUEUE
    job_type: ClassVar[Literal["training", "evaluation"]] = "evaluation"
    job_model: ClassVar[type[EvaluationJob]] = EvaluationJob

    def __init__(self, settings: HelixSettings, storage: Storage | None = None) -> None:
        """Initialize the worker.

        Args:
            settings: The process settings.
            storage: The S3 storage to read the model and dataset from and write
                results to; built from ``settings`` when not given.
        """
        super().__init__(settings)
        self._storage = storage or Storage.from_settings(settings)

    def entity_id(self, job: EvaluationJob) -> UUID:
        """Return the job's evaluation ID.

        Args:
            job: The evaluation job.

        Returns:
            ``job.evaluation_id``.
        """
        return job.evaluation_id

    def handle(self, job: EvaluationJob, emit: Callable[..., None]) -> dict[str, Any]:
        """Load the model and dataset, run the enabled steps and return the result.

        Args:
            job: The evaluation job.
            emit: Reports progress as a ``progress`` event.

        Returns:
            The ``succeeded`` event's result: the test-set metrics and every
            artifact written.

        Raises:
            HelixError: For bad data or a missing/invalid model, reported as a
                ``failed`` event.
        """

        def progress(fraction: float, message: str) -> None:
            emit("progress", progress=fraction, message=message)

        ctx = load_context(job, self._storage, self.n_jobs)
        run_steps(ctx, progress)

        return {
            "metrics": ctx.results["metrics"]["test"],
            "artifacts": ctx.artifacts,
        }
