"""Entry point: ``python -m helix_training`` or the ``helix-training`` script."""

from helix_core.settings import get_settings
from helix_core.worker import run_worker
from helix_training.worker import TrainingWorker


def main() -> None:
    """Run the training worker until it receives SIGTERM or SIGINT."""
    run_worker(TrainingWorker(get_settings()))


if __name__ == "__main__":
    main()
