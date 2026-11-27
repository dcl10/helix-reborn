from types import SimpleNamespace

import pytest

from helix_core.messages import EvaluationOptions
from helix_evaluation import steps


class _FakeStep:
    def __init__(self, name: str, weight: float) -> None:
        self.name = name
        self.weight = weight

    def enabled(self, options: EvaluationOptions) -> bool:
        return True

    def run(self, ctx: object) -> None:
        pass


def test_run_steps_progress_never_exceeds_one(monkeypatch: pytest.MonkeyPatch) -> None:
    # The default options' steps (metrics, plots, global importance): adding
    # their weights one by one gives 1.4000000000000001, while sum() gives 1.4,
    # so the last fraction was 1.0000000000000002.
    monkeypatch.setattr(
        steps,
        "STEPS",
        [_FakeStep("metrics", 1.0), _FakeStep("plots", 0.1), _FakeStep("global", 0.3)],
    )
    ctx = SimpleNamespace(job=SimpleNamespace(options=EvaluationOptions()))
    reported: list[float] = []

    steps.run_steps(ctx, lambda fraction, _message: reported.append(fraction))

    assert all(0 <= fraction <= 1 for fraction in reported)
    assert reported[-1] == 1.0
