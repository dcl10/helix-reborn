"""The training worker: consumes ``training.jobs`` and writes each model to S3.

The base :class:`~helix_core.worker.Worker` handles the queue, events, ack/reject,
health and shutdown; this class only loads the dataset, trains it with
:func:`~helix_training.train.train` and uploads the results.
"""

from collections.abc import Callable
from typing import Any, ClassVar, Literal
from uuid import UUID

from helix_core.datasets import load_csv
from helix_core.messages import TrainingJob
from helix_core.settings import HelixSettings
from helix_core.storage import Storage, join_key
from helix_core.worker import TRAINING_QUEUE, Worker
from helix_training.train import train


class TrainingWorker(Worker[TrainingJob]):
    """Trains each :class:`~helix_core.messages.TrainingJob` and uploads its model.

    Writes ``model.onnx``, ``metadata.json``, ``split.json`` and, with
    cross-validation, ``cv_metrics.json`` under the job's ``output_prefix``.
    """

    queue_name: ClassVar[str] = TRAINING_QUEUE
    job_type: ClassVar[Literal["training", "evaluation"]] = "training"
    job_model: ClassVar[type[TrainingJob]] = TrainingJob

    def __init__(self, settings: HelixSettings, storage: Storage | None = None) -> None:
        """Initialize the worker.

        Args:
            settings: The process settings.
            storage: The S3 storage to read datasets from and write models to;
                built from ``settings`` when not given.
        """
        super().__init__(settings)
        self._storage = storage or Storage.from_settings(settings)

    def entity_id(self, job: TrainingJob) -> UUID:
        """Return the job's run ID.

        Args:
            job: The training job.

        Returns:
            ``job.run_id``.
        """
        return job.run_id

    def handle(self, job: TrainingJob, emit: Callable[..., None]) -> dict[str, Any]:
        """Load the dataset, train the pipeline and upload the results.

        Args:
            job: The training job.
            emit: Reports progress as a ``progress`` event.

        Returns:
            The ``succeeded`` event's result: the output keys, cross-validation
            metrics, best search parameters and row counts.

        Raises:
            HelixError: For bad data, an invalid spec, or a failed ONNX parity
                check, reported as a ``failed`` event.
        """

        def progress(fraction: float, message: str) -> None:
            emit("progress", progress=fraction, message=message)

        df = load_csv(self._storage, job.dataset_key)
        output = train(job, df, self.n_jobs, self._settings.version, progress)

        prefix = job.output_prefix
        model_key = join_key(prefix, "model.onnx")
        metadata_key = join_key(prefix, "metadata.json")
        split_key = join_key(prefix, "split.json")
        cv_metrics_key = (
            join_key(prefix, "cv_metrics.json")
            if output.cv_metrics is not None
            else None
        )

        self._storage.put_bytes(
            model_key, output.onnx_bytes, "application/octet-stream"
        )
        self._storage.put_json(metadata_key, output.metadata.model_dump(mode="json"))
        self._storage.put_json(
            split_key,
            {"train_index": output.train_index, "test_index": output.test_index},
        )
        if cv_metrics_key is not None:
            self._storage.put_json(cv_metrics_key, output.cv_metrics)
        progress(0.95, "Uploaded")

        return {
            "model_key": model_key,
            "metadata_key": metadata_key,
            "split_key": split_key,
            "cv_metrics_key": cv_metrics_key,
            "cv_metrics": output.cv_metrics,
            "best_params": output.best_params,
            "train_rows": len(output.train_index),
            "test_rows": len(output.test_index),
        }
