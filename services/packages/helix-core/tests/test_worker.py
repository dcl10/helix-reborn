import asyncio
import json
from collections.abc import Callable
from typing import Any
from uuid import UUID, uuid4

import aio_pika
import pytest
from pydantic import BaseModel

from helix_core.errors import InvalidDataError
from helix_core.messages import JobEvent, PipelineSpec, StepSpec, TrainingJob
from helix_core.settings import HelixSettings
from helix_core.worker import EVENTS_QUEUE, TRAINING_QUEUE, Worker


class _Job(BaseModel):
    job_id: UUID
    run_id: UUID


class FakeIncomingMessage:
    """A fake AMQP message with the ack/reject/nack surface `Worker.process` uses."""

    def __init__(self, body: bytes) -> None:
        self.body = body
        self.acked = False
        self.rejected: bool | None = None
        self.nacked: bool | None = None

    async def ack(self) -> None:
        self.acked = True

    async def reject(self, requeue: bool = False) -> None:
        self.rejected = requeue

    async def nack(self, requeue: bool = False) -> None:
        self.nacked = requeue


class _RecordingWorker(Worker[_Job]):
    queue_name = "test.jobs"
    job_type = "training"
    job_model = _Job

    def __init__(
        self,
        settings: HelixSettings,
        handle_fn: Callable[[_Job, Callable[..., None]], dict[str, Any]],
    ) -> None:
        super().__init__(settings)
        self.events: list[JobEvent] = []
        self._handle_fn = handle_fn

    def entity_id(self, job: _Job) -> UUID:
        return job.run_id

    def handle(self, job: _Job, emit: Callable[..., None]) -> dict[str, Any]:
        return self._handle_fn(job, emit)

    async def _publish_event(self, event: JobEvent) -> None:
        self.events.append(event)


@pytest.fixture
def settings() -> HelixSettings:
    return HelixSettings()


def _job_body() -> bytes:
    return _Job(job_id=uuid4(), run_id=uuid4()).model_dump_json().encode("utf-8")


async def test_process_success_publishes_started_then_succeeded_then_acks(
    settings: HelixSettings,
) -> None:
    worker = _RecordingWorker(settings, lambda job, emit: {"ok": True})
    message = FakeIncomingMessage(_job_body())

    await worker.process(message)

    assert [event.status for event in worker.events] == ["started", "succeeded"]
    assert worker.events[-1].result == {"ok": True}
    assert message.acked is True
    assert message.rejected is None


async def test_process_helix_error_publishes_failed_then_acks(
    settings: HelixSettings,
) -> None:
    def handle_fn(job: _Job, emit: Callable[..., None]) -> dict[str, Any]:
        raise InvalidDataError("bad data")

    worker = _RecordingWorker(settings, handle_fn)
    message = FakeIncomingMessage(_job_body())

    await worker.process(message)

    assert [event.status for event in worker.events] == ["started", "failed"]
    assert worker.events[-1].error is not None
    assert worker.events[-1].error.type == "InvalidDataError"
    assert worker.events[-1].error.message == "bad data"
    assert message.acked is True


async def test_process_unexpected_exception_rejects_without_requeue(
    settings: HelixSettings,
) -> None:
    def handle_fn(job: _Job, emit: Callable[..., None]) -> dict[str, Any]:
        raise RuntimeError("boom")

    worker = _RecordingWorker(settings, handle_fn)
    message = FakeIncomingMessage(_job_body())

    await worker.process(message)

    assert [event.status for event in worker.events] == ["started", "failed"]
    assert worker.events[-1].error is not None
    assert worker.events[-1].error.type == "RuntimeError"
    assert message.acked is False
    assert message.rejected is False


async def test_process_invalid_body_rejects_without_requeue_and_publishes_no_event(
    settings: HelixSettings,
) -> None:
    worker = _RecordingWorker(settings, lambda job, emit: {})
    message = FakeIncomingMessage(b"not valid json")

    await worker.process(message)

    assert worker.events == []
    assert message.rejected is False
    assert message.acked is False


async def test_process_emits_progress_from_the_handler_thread_in_order(
    settings: HelixSettings,
) -> None:
    def handle_fn(job: _Job, emit: Callable[..., None]) -> dict[str, Any]:
        emit("progress", progress=0.3, message="a third done")
        emit("progress", progress=0.6, message="two thirds done")
        return {"ok": True}

    worker = _RecordingWorker(settings, handle_fn)
    message = FakeIncomingMessage(_job_body())

    await worker.process(message)

    statuses = [(event.status, event.progress) for event in worker.events]
    assert statuses == [
        ("started", None),
        ("progress", 0.3),
        ("progress", 0.6),
        ("succeeded", None),
    ]


class _TrivialTrainingWorker(Worker[TrainingJob]):
    """A worker whose `handle` does nothing but succeed, for the integration test."""

    queue_name = TRAINING_QUEUE
    job_type = "training"
    job_model = TrainingJob

    def entity_id(self, job: TrainingJob) -> UUID:
        return job.run_id

    def handle(self, job: TrainingJob, emit: Callable[..., None]) -> dict[str, Any]:
        return {"ok": True}


def _training_job_body() -> bytes:
    job = TrainingJob(
        job_id=uuid4(),
        run_id=uuid4(),
        experiment_id=uuid4(),
        dataset_key="experiments/e/datasets/d/data.csv",
        output_prefix="experiments/e/runs/r/",
        pipeline=PipelineSpec(
            problem_type="classification",
            target_column="label",
            feature_columns=["a", "b"],
            model=StepSpec(step="random_forest_classifier"),
        ),
    )
    return job.model_dump_json().encode("utf-8")


@pytest.mark.integration
async def test_worker_publishes_started_and_succeeded_to_jobs_events(
    settings: HelixSettings,
) -> None:
    worker = _TrivialTrainingWorker(settings)
    run_task = asyncio.ensure_future(worker.run())

    connection = await aio_pika.connect_robust(settings.rabbitmq_url.get_secret_value())
    try:
        channel = await connection.channel()
        events_queue = await channel.get_queue(EVENTS_QUEUE)

        for _ in range(50):
            if worker.is_ready():
                break
            await asyncio.sleep(0.1)
        assert worker.is_ready()

        await channel.default_exchange.publish(
            aio_pika.Message(
                body=_training_job_body(),
                content_type="application/json",
                delivery_mode=aio_pika.DeliveryMode.PERSISTENT,
            ),
            routing_key=TRAINING_QUEUE,
        )

        events = []
        async with events_queue.iterator() as queue_iter:
            async for message in queue_iter:
                async with message.process():
                    events.append(json.loads(message.body))
                if len(events) == 2:
                    break

        assert [event["status"] for event in events] == ["started", "succeeded"]
        assert events[1]["result"] == {"ok": True}
    finally:
        worker.request_stop()
        await asyncio.wait_for(run_task, timeout=5)
        await connection.close()
