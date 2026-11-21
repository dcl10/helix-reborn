"""The RabbitMQ worker base class shared by the training and evaluation workers.

Implements the queue topology, message handling and shutdown rules documented in
``docs/architecture.md`` ("Messaging"). Concrete workers (training #23, evaluation
#28) subclass :class:`Worker` and implement ``handle``.
"""

import asyncio
import logging
import os
import signal
from abc import ABC, abstractmethod
from collections.abc import Callable
from typing import Any, ClassVar, Literal, Protocol
from uuid import UUID

import aio_pika
from pydantic import BaseModel, ValidationError

from helix_core.errors import HelixError
from helix_core.health import start_health_server
from helix_core.logging import configure_logging
from helix_core.messages import JobError, JobEvent
from helix_core.settings import HelixSettings

logger = logging.getLogger(__name__)

TRAINING_QUEUE = "training.jobs"
EVALUATION_QUEUE = "evaluation.jobs"
EVENTS_QUEUE = "jobs.events"
DLX = "helix.dlx"

_DEFAULT_SHUTDOWN_GRACE_SECONDS = 25


class EventPublisher(Protocol):
    """Publishes a :class:`~helix_core.messages.JobEvent` for the job being handled."""

    async def __call__(
        self,
        status: str,
        *,
        progress: float | None = None,
        message: str | None = None,
        result: dict[str, Any] | None = None,
        error: JobError | None = None,
    ) -> None:
        """Publish one job event.

        Args:
            status: One of ``"started"``, ``"progress"``, ``"succeeded"``, ``"failed"``.
            progress: Progress in ``[0, 1]``, or ``None``.
            message: A short human-readable status message, or ``None``.
            result: The job's result payload, only for ``"succeeded"``.
            error: The job's error payload, only for ``"failed"``.
        """
        ...


async def declare_topology(channel: aio_pika.abc.AbstractChannel) -> None:
    """Idempotently declare the exchanges and queues used for jobs and events.

    Both the API and the workers must declare these with identical arguments, or
    RabbitMQ closes the channel with ``PRECONDITION_FAILED``.

    Args:
        channel: An open AMQP channel.
    """
    dlx = await channel.declare_exchange(
        DLX, aio_pika.ExchangeType.DIRECT, durable=True
    )

    for queue_name in (TRAINING_QUEUE, EVALUATION_QUEUE):
        dead_queue_name = f"{queue_name}.dead"
        dead_queue = await channel.declare_queue(dead_queue_name, durable=True)
        await dead_queue.bind(dlx, routing_key=dead_queue_name)
        await channel.declare_queue(
            queue_name,
            durable=True,
            arguments={
                "x-dead-letter-exchange": DLX,
                "x-dead-letter-routing-key": dead_queue_name,
            },
        )

    await channel.declare_queue(EVENTS_QUEUE, durable=True)


class Worker[JobT: BaseModel](ABC):
    """Consumes one job queue, one job at a time, and reports JobEvents.

    Subclasses set the class variables ``queue_name``, ``job_type`` and ``job_model``,
    and implement :meth:`entity_id` and :meth:`handle`.
    """

    queue_name: ClassVar[str]
    job_type: ClassVar[Literal["training", "evaluation"]]
    job_model: ClassVar[type[BaseModel]]

    def __init__(self, settings: HelixSettings) -> None:
        """Initialize the worker.

        Sets ``OMP_NUM_THREADS``, ``OPENBLAS_NUM_THREADS`` and ``MKL_NUM_THREADS``
        from ``settings.worker_cpus`` before any job code (and therefore numpy) is
        imported.

        Args:
            settings: The process settings, including the RabbitMQ URL and
                ``worker_cpus``.
        """
        self._settings = settings
        self.n_jobs = settings.worker_cpus
        for env_var in ("OMP_NUM_THREADS", "OPENBLAS_NUM_THREADS", "MKL_NUM_THREADS"):
            os.environ[env_var] = str(settings.worker_cpus)

        self.shutdown_grace_seconds = _DEFAULT_SHUTDOWN_GRACE_SECONDS
        self._stop_requested = False
        self._stop_event: asyncio.Event | None = None
        self._connection: aio_pika.abc.AbstractRobustConnection | None = None
        self._consuming = False
        self._loop: asyncio.AbstractEventLoop | None = None

    @abstractmethod
    def entity_id(self, job: JobT) -> UUID:
        r"""Return the entity id (``run_id`` or ``evaluation_id``) a job refers to.

        Args:
            job: The parsed job message.

        Returns:
            The id to stamp onto published :class:`JobEvent`\\ s as ``entity_id``.
        """

    @abstractmethod
    def handle(self, job: JobT, emit: Callable[..., None]) -> dict[str, Any]:
        """Do the job's work synchronously. Runs in a worker thread.

        Args:
            job: The parsed job message.
            emit: Thread-safe callback, ``emit(status, *, progress=None,
                message=None)``, to report progress while the job runs.

        Returns:
            The ``result`` dict to publish on the ``succeeded`` event.

        Raises:
            HelixError: For expected failures (bad data, invalid spec). Reported as
                a ``failed`` event and acked, rather than dead-lettered.
        """

    def is_ready(self) -> bool:
        """Return whether this worker is ready to serve traffic.

        Returns:
            ``True`` once the AMQP connection is open and the worker is consuming.
        """
        return (
            self._connection is not None
            and not self._connection.is_closed
            and self._consuming
        )

    def request_stop(self) -> None:
        """Request that the worker stop consuming and shut down.

        Called from the SIGTERM/SIGINT handler. Safe to call more than once.
        """
        self._stop_requested = True
        if self._stop_event is not None and self._loop is not None:
            self._loop.call_soon_threadsafe(self._stop_event.set)

    async def run(self) -> None:
        """Connect, declare topology, set prefetch 1, and consume until stopped."""
        self._loop = asyncio.get_running_loop()
        self._stop_event = asyncio.Event()
        if self._stop_requested:
            self._stop_event.set()
        connection = await aio_pika.connect_robust(
            self._settings.rabbitmq_url.get_secret_value()
        )
        self._connection = connection

        async def on_reconnect(_: aio_pika.abc.AbstractRobustConnection) -> None:
            logger.info("Reconnected to RabbitMQ; redeclaring topology")
            async with connection.channel() as channel:
                await declare_topology(channel)

        connection.reconnect_callbacks.add(on_reconnect)

        channel = await connection.channel()
        await channel.set_qos(prefetch_count=1)
        await declare_topology(channel)
        queue = await channel.get_queue(self.queue_name)

        self._consuming = True
        try:
            async with queue.iterator() as queue_iter:
                async for message in queue_iter:
                    if self._stop_requested:
                        await message.nack(requeue=True)
                        break
                    await self._process_with_grace(message)
                    if self._stop_requested:
                        break
        finally:
            self._consuming = False
            await connection.close()

    async def _process_with_grace(
        self, message: aio_pika.abc.AbstractIncomingMessage
    ) -> None:
        assert self._stop_event is not None
        task = asyncio.ensure_future(self.process(message))
        stop_wait = asyncio.ensure_future(self._stop_event.wait())
        try:
            done, _ = await asyncio.wait(
                {task, stop_wait}, return_when=asyncio.FIRST_COMPLETED
            )
            if task in done:
                task.result()
                return
            try:
                await asyncio.wait_for(task, timeout=self.shutdown_grace_seconds)
            except TimeoutError:
                task.cancel()
                await message.nack(requeue=True)
        finally:
            stop_wait.cancel()

    async def process(self, message: aio_pika.abc.AbstractIncomingMessage) -> None:
        """Handle one incoming message: parse, run the job, publish events, ack/reject.

        Extracted from :meth:`run` so tests can call it directly against a fake
        message, without a real AMQP connection.

        Args:
            message: The incoming AMQP message carrying a job body.
        """
        try:
            job = self.job_model.model_validate_json(message.body)
        except ValidationError:
            logger.exception("Rejecting message with an invalid body")
            await message.reject(requeue=False)
            return

        job_id = job.job_id
        entity_id = self.entity_id(job)
        publish = self._make_publisher(job_id, entity_id)
        loop = asyncio.get_running_loop()

        await publish("started")

        def emit(
            status: str,
            *,
            progress: float | None = None,
            message: str | None = None,
        ) -> None:
            future = asyncio.run_coroutine_threadsafe(
                publish(status, progress=progress, message=message), loop
            )
            future.result()

        try:
            result = await asyncio.to_thread(self.handle, job, emit)
        except HelixError as exc:
            await publish(
                "failed",
                error=JobError(type=exc.__class__.__name__, message=str(exc)),
            )
            await message.ack()
        except Exception as exc:
            logger.exception("Unhandled exception while processing job %s", job_id)
            await publish(
                "failed",
                error=JobError(type=exc.__class__.__name__, message=str(exc)),
            )
            await message.reject(requeue=False)
        else:
            await publish("succeeded", result=result)
            await message.ack()

    def _make_publisher(self, job_id: UUID, entity_id: UUID) -> EventPublisher:
        async def publish(
            status: str,
            *,
            progress: float | None = None,
            message: str | None = None,
            result: dict[str, Any] | None = None,
            error: JobError | None = None,
        ) -> None:
            event = JobEvent(
                job_id=job_id,
                job_type=self.job_type,
                entity_id=entity_id,
                status=status,  # type: ignore[arg-type]
                progress=progress,
                message=message,
                result=result,
                error=error,
            )
            await self._publish_event(event)

        return publish

    async def _publish_event(self, event: JobEvent) -> None:
        assert self._connection is not None
        channel = await self._connection.channel()
        try:
            await channel.default_exchange.publish(
                aio_pika.Message(
                    body=event.model_dump_json().encode("utf-8"),
                    content_type="application/json",
                    delivery_mode=aio_pika.DeliveryMode.PERSISTENT,
                    message_id=str(event.job_id),
                ),
                routing_key=EVENTS_QUEUE,
            )
        finally:
            await channel.close()


def run_worker(worker: Worker[Any]) -> None:
    """Configure logging, install signal handlers, start the health server and run.

    Args:
        worker: The worker instance to run.
    """
    configure_logging(level=worker._settings.log_level, service=worker.job_type)

    loop = asyncio.new_event_loop()
    asyncio.set_event_loop(loop)

    def handle_signal() -> None:
        logger.info("Received stop signal; finishing the current job if any")
        worker.request_stop()

    for sig in (signal.SIGTERM, signal.SIGINT):
        loop.add_signal_handler(sig, handle_signal)

    async def main() -> None:
        health_server = await start_health_server(
            worker._settings.health_port, worker.is_ready, worker._settings.version
        )
        try:
            await worker.run()
        finally:
            health_server.close()
            await health_server.wait_closed()

    loop.run_until_complete(main())
