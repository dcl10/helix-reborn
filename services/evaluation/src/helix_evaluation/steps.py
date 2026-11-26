"""The evaluation step framework.

An evaluation job runs a fixed, ordered list of steps (:data:`STEPS`). Each step
decides for itself whether it applies to a job's options
(:meth:`EvaluationStep.enabled`) and, when it does, reads and writes the shared
:class:`~helix_evaluation.context.EvaluationContext`. Keeping the list in one place
(rather than scattered registrations) makes the order plots, global/local feature
importance, ensembling and fuzzy interpretability run in obvious at a glance; later
issues (#29-#33) insert their step at the right position.
"""

from collections.abc import Callable
from typing import Protocol

from helix_core.messages import EvaluationOptions
from helix_evaluation.context import EvaluationContext
from helix_evaluation.metrics import MetricsStep

Progress = Callable[[float, str], None]
"""Reports evaluation progress: ``progress(fraction, message)``."""


class EvaluationStep(Protocol):
    """One stage of an evaluation job.

    Attributes:
        name: The step's name, used as a progress message and results key.
        weight: The step's share of the overall progress bar (0-1); weights across
            :data:`STEPS` should sum to roughly 1.
    """

    name: str
    weight: float

    def enabled(self, options: EvaluationOptions) -> bool:
        """Decide whether this step runs for a job's options.

        Args:
            options: The evaluation job's options.

        Returns:
            ``True`` if the step should run.
        """
        ...

    def run(self, ctx: EvaluationContext) -> None:
        """Run the step, reading and writing ``ctx`` in place.

        Args:
            ctx: The evaluation context to read from and write results, artifacts
                and plots/tables/JSON into.
        """
        ...


STEPS: list[EvaluationStep] = [MetricsStep()]
"""Every evaluation step, in run order: metrics, plots, global FI, local FI, ensemble,
fuzzy. Only ``metrics`` is implemented so far (#29-#33 append the rest)."""


def run_steps(ctx: EvaluationContext, progress: Progress) -> None:
    """Run every enabled step in :data:`STEPS`, in order, reporting progress.

    Args:
        ctx: The evaluation context the steps read and write.
        progress: Called with an increasing fraction and the step's name as each
            enabled step finishes.
    """
    enabled_steps = [step for step in STEPS if step.enabled(ctx.job.options)]
    total_weight = sum(step.weight for step in enabled_steps) or 1.0
    completed_weight = 0.0
    for step in enabled_steps:
        step.run(ctx)
        completed_weight += step.weight
        progress(completed_weight / total_weight, step.name)
