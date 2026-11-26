"""Entry point: ``python -m helix_evaluation`` or the ``helix-evaluation`` script."""

from helix_core.settings import get_settings
from helix_core.worker import run_worker
from helix_evaluation.worker import EvaluationWorker


def main() -> None:
    """Run the evaluation worker until it receives SIGTERM or SIGINT."""
    run_worker(EvaluationWorker(get_settings()))


if __name__ == "__main__":
    main()
