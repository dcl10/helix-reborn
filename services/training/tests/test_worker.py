import asyncio
import json
from uuid import uuid4

import aio_pika
import boto3
import pandas as pd
import pytest
from moto import mock_aws
from sklearn.datasets import make_classification

from helix_core.messages import PipelineSpec, TrainingJob
from helix_core.settings import HelixSettings
from helix_core.storage import Storage
from helix_core.worker import EVENTS_QUEUE, TRAINING_QUEUE
from helix_training.worker import TrainingWorker

BUCKET = "helix"


@pytest.fixture
def s3_storage():
    with mock_aws():
        client = boto3.client("s3", region_name="us-east-1")
        client.create_bucket(Bucket=BUCKET)
        yield Storage(client, BUCKET)


def _csv() -> bytes:
    x, y = make_classification(n_samples=60, n_features=4, random_state=42)
    df = pd.DataFrame(x, columns=["a", "b", "c", "d"])
    df["label"] = y
    return df.to_csv(index=False).encode("utf-8")


def _job(dataset_key: str, prefix: str, cv_folds: int | None = 3) -> TrainingJob:
    return TrainingJob(
        job_id=uuid4(),
        run_id=uuid4(),
        experiment_id=uuid4(),
        dataset_key=dataset_key,
        output_prefix=prefix,
        pipeline=PipelineSpec.model_validate(
            {
                "problem_type": "classification",
                "target_column": "label",
                "feature_columns": ["a", "b", "c", "d"],
                "split": {"cv_folds": cv_folds},
                "steps": [{"step": "standard_scaler"}],
                "model": {"step": "logistic_regression"},
            }
        ),
    )


def test_handle_writes_all_four_outputs_and_returns_the_result(
    s3_storage: Storage,
) -> None:
    s3_storage.put_bytes("experiments/e/datasets/d/data.csv", _csv(), "text/csv")
    worker = TrainingWorker(HelixSettings(), storage=s3_storage)
    events = []

    result = worker.handle(
        _job("experiments/e/datasets/d/data.csv", "experiments/e/runs/r/"),
        lambda status, **kwargs: events.append((status, kwargs)),
    )

    assert result == {
        "model_key": "experiments/e/runs/r/model.onnx",
        "metadata_key": "experiments/e/runs/r/metadata.json",
        "split_key": "experiments/e/runs/r/split.json",
        "cv_metrics_key": "experiments/e/runs/r/cv_metrics.json",
        "cv_metrics": result["cv_metrics"],
        "best_params": None,
        "train_rows": 48,
        "test_rows": 12,
    }
    assert set(result["cv_metrics"]) == {
        "accuracy",
        "f1_weighted",
        "precision_weighted",
        "recall_weighted",
        "roc_auc",
    }
    assert s3_storage.get_bytes(result["model_key"])[:2]  # non-empty ONNX
    metadata = s3_storage.get_json(result["metadata_key"])
    assert metadata["feature_columns"] == ["a", "b", "c", "d"]
    assert metadata["classes"] == [0, 1]
    split = s3_storage.get_json(result["split_key"])
    assert len(split["train_index"]) == 48
    assert s3_storage.get_json(result["cv_metrics_key"]) == result["cv_metrics"]
    assert json.dumps(result)  # the result is valid JSON for the event
    assert [status for status, _ in events] == ["progress"] * len(events)
    assert events[-1][1] == {"progress": 0.95, "message": "Uploaded"}


def test_handle_without_cv_writes_no_cv_metrics(s3_storage: Storage) -> None:
    s3_storage.put_bytes("experiments/e/datasets/d/data.csv", _csv(), "text/csv")
    worker = TrainingWorker(HelixSettings(), storage=s3_storage)

    result = worker.handle(
        _job(
            "experiments/e/datasets/d/data.csv", "experiments/e/runs/r/", cv_folds=None
        ),
        lambda status, **kwargs: None,
    )

    assert result["cv_metrics_key"] is None
    assert result["cv_metrics"] is None
    assert not s3_storage.exists("experiments/e/runs/r/cv_metrics.json")


@pytest.mark.integration
async def test_worker_trains_a_job_from_the_queue_end_to_end() -> None:
    settings = HelixSettings()
    storage = Storage.from_settings(settings)
    prefix = f"integration-tests/training/{uuid4()}/"
    dataset_key = f"{prefix}data.csv"
    storage.put_bytes(dataset_key, _csv(), "text/csv")
    job = _job(dataset_key, f"{prefix}run/")

    worker = TrainingWorker(settings, storage=storage)
    run_task = asyncio.ensure_future(worker.run())
    connection = await aio_pika.connect_robust(settings.rabbitmq_url.get_secret_value())
    try:
        for _ in range(50):
            if worker.is_ready():
                break
            await asyncio.sleep(0.1)
        assert worker.is_ready()

        channel = await connection.channel()
        events_queue = await channel.get_queue(EVENTS_QUEUE)
        await channel.default_exchange.publish(
            aio_pika.Message(
                body=job.model_dump_json().encode("utf-8"),
                content_type="application/json",
                delivery_mode=aio_pika.DeliveryMode.PERSISTENT,
            ),
            routing_key=TRAINING_QUEUE,
        )

        events = []
        async with events_queue.iterator() as queue_iter:
            async for message in queue_iter:
                async with message.process():
                    event = json.loads(message.body)
                if event["job_id"] != str(job.job_id):
                    continue
                events.append(event)
                if event["status"] in ("succeeded", "failed"):
                    break

        statuses = [event["status"] for event in events]
        assert statuses[0] == "started"
        assert "progress" in statuses
        assert statuses[-1] == "succeeded", events[-1]
        assert storage.exists(events[-1]["result"]["model_key"])
    finally:
        worker.request_stop()
        await asyncio.wait_for(run_task, timeout=10)
        await connection.close()
        for name in (
            "data.csv",
            "run/model.onnx",
            "run/metadata.json",
            "run/split.json",
            "run/cv_metrics.json",
        ):
            storage._client.delete_object(
                Bucket=settings.s3_bucket, Key=f"{prefix}{name}"
            )
